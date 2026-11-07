using System.IO.Compression;
using System.Text;

namespace ComiCraft.Comics.Tests;

/// <summary>Small comics made in memory, so the tests need no comic files.</summary>
internal static class TestComics
{
    /// <summary>A zip with the given entries, each holding its own name as its bytes.</summary>
    public static MemoryStream Cbz(params string[] entries) => Cbz(entries.Select(name => (name, name)).ToArray());

    /// <summary>A zip with the given entries and their text.</summary>
    public static MemoryStream Cbz(params (string Name, string Text)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>A comic of the given pages with the given ComicInfo.xml.</summary>
    public static ComicBook WithInfo(string comicInfo, params string[] pages)
    {
        using var cbz = Cbz([.. pages.Select(p => (p, p)), ("ComicInfo.xml", comicInfo)]);
        return ComicBook.Open(cbz, "Test Comic");
    }
}
