using SharpCompress.Archives;
using SharpCompress.Common;

namespace ComiCraft.Comics;

/// <summary>
/// A comic book: a CBZ (zip) or a CBR (RAR) archive, or a PDF. The kind is found from the file's
/// contents, not its extension, since comics are often named for the wrong one. An archive's pages
/// are the images in it, in the natural order of their names; everything else is left out, except
/// a ComicInfo.xml, which is read for what it says about the comic. A PDF's pages are the pictures
/// its pages are (see <see cref="PdfComic"/>).
/// </summary>
public sealed class ComicBook
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    private readonly byte[]?[] _pages;
    private readonly string?[] _problems;

    // reads a page that has not been read yet: its image, or, when it cannot be shown, why not
    private readonly Func<int, (byte[] Image, string? Problem)>? _read;

    private ComicBook(string name, ComicInfo? info, List<string> pageNames, byte[]?[] pages, Func<int, (byte[] Image, string? Problem)>? read = null)
    {
        Name = name;
        Info = info;
        PageNames = pageNames;
        _pages = pages;
        _problems = new string?[pages.Length];
        _read = read;
    }

    /// <summary>A PDF's comic, whose pages are read when they are first asked for.</summary>
    internal static ComicBook FromPdf(string name, ComicInfo? info, List<string> pageNames, Func<int, (byte[] Image, string? Problem)> read) =>
        new(name, info, pageNames, new byte[pageNames.Count][], read);

    /// <summary>The comic's file name without the extension.</summary>
    public string Name { get; }

    /// <summary>What the comic's ComicInfo.xml says about it; null when it has none that can be read.</summary>
    public ComicInfo? Info { get; }

    /// <summary>The comic's name to show: from its ComicInfo.xml when that names it, its file name otherwise.</summary>
    public string Heading => Info?.Heading ?? Name;

    /// <summary>The name of each page in the archive, in reading order; a PDF's are "Page 001" and so on.</summary>
    public IReadOnlyList<string> PageNames { get; }

    public int PageCount => _pages.Length;

    /// <summary>
    /// The encoded image (JPEG, PNG and so on) of a page, counted from zero; nothing, for a page
    /// of a PDF that is not a picture (see <see cref="Problem"/>).
    /// </summary>
    public ReadOnlyMemory<byte> Page(int index)
    {
        Read(index);
        return _pages[index];
    }

    /// <summary>Why the page cannot be shown, for a page of a PDF that is not a picture; null for any other.</summary>
    public string? Problem(int index)
    {
        Read(index);
        return _problems[index];
    }

    private void Read(int index)
    {
        if (_pages[index] is null && _read is not null)
        {
            (_pages[index], _problems[index]) = _read(index);
        }
    }

    public static ComicBook Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Open(stream, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Reads every page into memory. A solid archive (common for RAR) can only be read from start
    /// to end, so it is; any other is read one entry at a time.
    /// </summary>
    public static ComicBook Open(Stream stream, string name)
    {
        if (StartsAsPdf(stream))
        {
            using var whole = new MemoryStream();
            stream.CopyTo(whole);
            return PdfComic.Read(whole.ToArray(), name);
        }

        using var archive = ArchiveFactory.OpenArchive(stream);
        var pages = new List<(string Name, byte[] Image)>();
        ComicInfo? info = null;

        void Take(IEntry entry, Func<Stream> open)
        {
            if (entry.IsDirectory || entry.Key is not { } key)
            {
                return;
            }

            if (IsImage(key))
            {
                using var source = open();
                using var image = new MemoryStream();
                source.CopyTo(image);
                // a RAR's folders are written with the system's separator (\ on Windows), a zip's with /
                pages.Add((key.Replace('\\', '/'), image.ToArray()));
            }
            else if (info is null && ComicInfo.IsComicInfo(key))
            {
                using var source = open();
                info = ComicInfo.Parse(source);
            }
        }

        if (archive.IsSolid)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                Take(reader.Entry, reader.OpenEntryStream);
            }
        }
        else
        {
            foreach (var entry in archive.Entries)
            {
                Take(entry, entry.OpenEntryStream);
            }
        }

        pages.Sort((a, b) => NaturalOrder.Instance.Compare(a.Name, b.Name));
        return new ComicBook(name, info, pages.ConvertAll(p => p.Name), pages.ConvertAll(p => (byte[]?)p.Image).ToArray());
    }

    /// <summary>Whether the stream, from where it is, is a PDF; it is left where it was.</summary>
    private static bool StartsAsPdf(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        long at = stream.Position;
        Span<byte> start = stackalloc byte[5];
        int read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        stream.Position = at;
        return PdfComic.IsPdf(start[..read]);
    }

    internal static bool IsImage(string name)
    {
        // macOS leaves "__MACOSX/._page.jpg" files in zips it makes; they are not images
        string fileName = Path.GetFileName(name.Replace('\\', '/'));
        return !fileName.StartsWith('.') && !name.StartsWith("__MACOSX", StringComparison.Ordinal)
            && ImageExtensions.Contains(Path.GetExtension(fileName));
    }
}
