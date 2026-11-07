using SharpCompress.Archives;
using SharpCompress.Common;

namespace ComiCraft.Comics;

/// <summary>
/// A comic book archive: a CBZ (zip) or a CBR (RAR). The kind is found from the file's contents,
/// not its extension, since comics are often named for the wrong one. Its pages are the images
/// in the archive, in the natural order of their names; everything else is left out, except a
/// ComicInfo.xml, which is read for what it says about the comic.
/// </summary>
public sealed class ComicBook
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    private readonly List<byte[]> _pages;

    private ComicBook(string name, ComicInfo? info, List<string> pageNames, List<byte[]> pages)
    {
        Name = name;
        Info = info;
        PageNames = pageNames;
        _pages = pages;
    }

    /// <summary>The comic's file name without the extension.</summary>
    public string Name { get; }

    /// <summary>What the comic's ComicInfo.xml says about it; null when it has none that can be read.</summary>
    public ComicInfo? Info { get; }

    /// <summary>The comic's name to show: from its ComicInfo.xml when that names it, its file name otherwise.</summary>
    public string Heading => Info?.Heading ?? Name;

    /// <summary>The name of each page in the archive, in reading order.</summary>
    public IReadOnlyList<string> PageNames { get; }

    public int PageCount => _pages.Count;

    /// <summary>The encoded image (JPEG, PNG and so on) of a page, counted from zero.</summary>
    public ReadOnlyMemory<byte> Page(int index) => _pages[index];

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
                pages.Add((key, image.ToArray()));
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
        return new ComicBook(name, info, pages.ConvertAll(p => p.Name), pages.ConvertAll(p => p.Image));
    }

    internal static bool IsImage(string name)
    {
        // macOS leaves "__MACOSX/._page.jpg" files in zips it makes; they are not images
        string fileName = Path.GetFileName(name.Replace('\\', '/'));
        return !fileName.StartsWith('.') && !name.StartsWith("__MACOSX", StringComparison.Ordinal)
            && ImageExtensions.Contains(Path.GetExtension(fileName));
    }
}
