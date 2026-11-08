using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ComiCraft.Comics;

/// <summary>What a comic archive is, from the first bytes of the file.</summary>
public enum ArchiveKind
{
    /// <summary>Not an archive this library knows.</summary>
    Unknown,

    /// <summary>A zip, as a CBZ is: it can be read and written.</summary>
    Zip,

    /// <summary>A RAR, as a CBR is: it can be read, and converted to a CBZ, but not written.</summary>
    Rar,

    /// <summary>A 7-Zip archive, as a CB7 is.</summary>
    SevenZip,
}

/// <summary>
/// Changing comic archives on disk: writing a comic's ComicInfo.xml, and converting a comic to a
/// CBZ. A comic is never left half written: the change is made to a copy beside it, and the copy
/// then takes its place in one step, so the comic is either as it was or as it is meant to be.
/// Only a zip can be written; RAR is a closed format that free software can read but not write,
/// so a CBR is converted to a CBZ to be changed, which makes a new file and leaves the CBR as it is.
/// </summary>
public static class ComicArchive
{
    /// <summary>The kind of archive the file is, by its signature, whatever its extension says.</summary>
    public static ArchiveKind KindOf(string path)
    {
        Span<byte> start = stackalloc byte[8];
        using var stream = File.OpenRead(path);
        int read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        return KindOf(start[..read]);
    }

    internal static ArchiveKind KindOf(ReadOnlySpan<byte> start) => start switch
    {
        // a zip starts with a file's header, or, when it is empty, the end of its directory
        [(byte)'P', (byte)'K', 3, 4, ..] or [(byte)'P', (byte)'K', 5, 6, ..] => ArchiveKind.Zip,
        // RAR 1.5 to 4, and RAR 5, which adds a 1 after the 7
        [(byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 7, 0, ..] or [(byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 7, 1, 0, ..] => ArchiveKind.Rar,
        [(byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C, ..] => ArchiveKind.SevenZip,
        _ => ArchiveKind.Unknown,
    };

    /// <summary>
    /// The comic's ComicInfo.xml, to edit, or null when it has none. One that cannot be read is an
    /// error, rather than nothing, so that it is never written over unseen.
    /// </summary>
    public static ComicInfoDocument? ReadComicInfo(string path) =>
        ReadComicInfoBytes(path) is { } xml ? ComicInfoDocument.Load(new MemoryStream(xml)) : null;

    /// <summary>The comic's ComicInfo.xml as it is in the archive, byte for byte; null when it has none.</summary>
    internal static byte[]? ReadComicInfoBytes(string path)
    {
        using var archive = Open(path);
        if (archive.IsSolid)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (IsComicInfo(reader.Entry))
                {
                    using var stream = reader.OpenEntryStream();
                    return ReadAll(stream);
                }
            }

            return null;
        }

        var entry = archive.Entries.FirstOrDefault(IsComicInfo);
        if (entry is null)
        {
            return null;
        }

        using var entryStream = entry.OpenEntryStream();
        return ReadAll(entryStream);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>
    /// Writes the comic's ComicInfo.xml, in place of the one it has, or as a new one. Its pages,
    /// and everything else in it, are copied as they are, not compressed again. Only a CBZ can be
    /// written: anything else is an error, and is left as it is.
    /// </summary>
    public static void WriteComicInfo(string path, ComicInfoDocument document)
    {
        var kind = KindOf(path);
        if (kind != ArchiveKind.Zip)
        {
            throw new ComicEditException(kind == ArchiveKind.Rar
                ? $"'{Path.GetFileName(path)}' is a RAR archive (a CBR), which cannot be written. Convert it to a CBZ to change it."
                : $"'{Path.GetFileName(path)}' is not a zip archive (a CBZ), so it cannot be written.");
        }

        ThrowIfReadOnly(path);
        byte[] xml = document.ToBytes();
        ReplaceSafely(path, overwrite: true, copy =>
        {
            File.Copy(path, copy, overwrite: true);
            using var zip = ZipFile.Open(copy, ZipArchiveMode.Update);

            // the name it had is kept, whatever its case; every copy of it goes, so one is left
            var old = zip.Entries.Where(e => ComicInfo.IsComicInfo(e.FullName)).ToList();
            string name = old.FirstOrDefault()?.FullName ?? ComicInfo.FileName;
            old.ForEach(e => e.Delete());

            using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            stream.Write(xml);
        });
    }

    /// <summary>
    /// Makes a CBZ of the comic, with everything that is in it, by the same names. The comic
    /// itself is left as it is, and a file already at <paramref name="target"/> is never written
    /// over. Returns where the CBZ is: <paramref name="target"/>, or, when none is given, beside
    /// the comic, by its name, with the extension .cbz.
    /// </summary>
    public static string ConvertToCbz(string path, string? target = null)
    {
        target ??= Path.ChangeExtension(path, ".cbz");
        if (Path.GetFullPath(target) == Path.GetFullPath(path))
        {
            throw new ComicEditException($"'{Path.GetFileName(path)}' cannot be converted to itself; give the CBZ another name.");
        }

        if (File.Exists(target))
        {
            throw new ComicEditException($"'{Path.GetFileName(target)}' is already there, and is left as it is.");
        }

        using var archive = Open(path);
        ReplaceSafely(target, overwrite: false, copy =>
        {
            using var zip = ZipFile.Open(copy, ZipArchiveMode.Create);
            void Add(IEntry entry, Func<Stream> open)
            {
                if (entry.IsDirectory || entry.Key is not { } key)
                {
                    return;
                }

                // images are compressed already, and gain nothing from being compressed again
                var level = ComicBook.IsImage(key) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var added = zip.CreateEntry(key.Replace('\\', '/'), level);
                if (entry.LastModifiedTime is { } modified && modified.Year is >= 1980 and <= 2107)
                {
                    added.LastWriteTime = modified;
                }

                using var source = open();
                using var destination = added.Open();
                source.CopyTo(destination);
            }

            if (archive.IsSolid)
            {
                using var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    Add(reader.Entry, reader.OpenEntryStream);
                }
            }
            else
            {
                foreach (var entry in archive.Entries)
                {
                    Add(entry, entry.OpenEntryStream);
                }
            }
        });

        return target;
    }

    private static IArchive Open(string path)
    {
        try
        {
            // opened by its path, the archive has the file to itself, and closes it when it is done
            return ArchiveFactory.OpenArchive(path);
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
        {
            throw new ComicEditException($"'{Path.GetFileName(path)}' cannot be read as a comic: {ex.Message}", ex);
        }
    }

    private static bool IsComicInfo(IEntry entry) => !entry.IsDirectory && entry.Key is { } key && ComicInfo.IsComicInfo(key);

    /// <summary>
    /// A comic the reader has made read-only is not changed: replacing the file would get round
    /// that, since what is checked when a file is replaced is its folder, not the file.
    /// </summary>
    private static void ThrowIfReadOnly(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
        {
            throw new ComicEditException($"'{Path.GetFileName(path)}' is read-only, so it is left as it is.");
        }
    }

    /// <summary>
    /// Makes a file by writing it to a copy beside where it goes, a hidden file in the same folder
    /// (so the move is a rename, done in one step), and then moving the copy into place: over the
    /// file there, or, without <paramref name="overwrite"/>, only where there is none. When the
    /// writing fails, the copy is removed and nothing has changed.
    /// </summary>
    internal static void ReplaceSafely(string path, bool overwrite, Action<string> write)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string copy = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.comicraft");
        try
        {
            write(copy);
            try
            {
                File.Move(copy, path, overwrite);
            }
            catch (IOException) when (!overwrite && File.Exists(path))
            {
                throw new ComicEditException($"'{Path.GetFileName(path)}' is already there, and is left as it is.");
            }
        }
        finally
        {
            if (File.Exists(copy))
            {
                File.Delete(copy);
            }
        }
    }
}
