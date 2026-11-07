using SharpCompress.Archives;

namespace ComiCraft.Comics;

/// <summary>
/// A comic book archive: a CBZ (zip) or a CBR (RAR). The kind is found from the file's contents,
/// not its extension, since comics are often named for the wrong one. Its pages are the images
/// in the archive, in the natural order of their names; everything else is left out.
/// </summary>
public sealed class ComicBook
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    private readonly List<byte[]> _pages;

    private ComicBook(string title, List<string> pageNames, List<byte[]> pages)
    {
        Title = title;
        PageNames = pageNames;
        _pages = pages;
    }

    /// <summary>The comic's name: its file name without the extension.</summary>
    public string Title { get; }

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
    public static ComicBook Open(Stream stream, string title)
    {
        using var archive = ArchiveFactory.OpenArchive(stream);
        var pages = new List<(string Name, byte[] Image)>();
        if (archive.IsSolid)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (PageName(reader.Entry) is { } name)
                {
                    using var image = new MemoryStream();
                    reader.WriteEntryTo(image);
                    pages.Add((name, image.ToArray()));
                }
            }
        }
        else
        {
            foreach (var entry in archive.Entries)
            {
                if (PageName(entry) is { } name)
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var image = new MemoryStream();
                    entryStream.CopyTo(image);
                    pages.Add((name, image.ToArray()));
                }
            }
        }

        pages.Sort((a, b) => NaturalOrder.Instance.Compare(a.Name, b.Name));
        return new ComicBook(title, pages.ConvertAll(p => p.Name), pages.ConvertAll(p => p.Image));
    }

    /// <summary>The entry's name when it is a page, or null for a folder or anything not an image.</summary>
    private static string? PageName(SharpCompress.Common.IEntry entry) =>
        !entry.IsDirectory && entry.Key is { } name && IsImage(name) ? name : null;

    internal static bool IsImage(string name)
    {
        // macOS leaves "__MACOSX/._page.jpg" files in zips it makes; they are not images
        string fileName = Path.GetFileName(name.Replace('\\', '/'));
        return !fileName.StartsWith('.') && !name.StartsWith("__MACOSX", StringComparison.Ordinal)
            && ImageExtensions.Contains(Path.GetExtension(fileName));
    }
}
