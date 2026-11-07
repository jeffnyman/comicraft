using System.IO.Compression;
using System.Text;
using ComiCraft.Comics;

namespace ComiCraft.Comics.Tests;

public class ComicBookTests
{
    /// <summary>A zip with the given entries, each holding its own name as its bytes.</summary>
    private static MemoryStream Cbz(params string[] entries) => Cbz(entries.Select(name => (name, name)).ToArray());

    /// <summary>A zip with the given entries and their text.</summary>
    private static MemoryStream Cbz(params (string Name, string Text)[] entries)
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

    [Fact]
    public void Pages_are_the_images_in_natural_order()
    {
        using var cbz = Cbz("10.jpg", "ComicInfo.xml", "2.png", "1.jpeg", "notes.txt");
        var comic = ComicBook.Open(cbz, "Test");

        Assert.Equal(["1.jpeg", "2.png", "10.jpg"], comic.PageNames);
        Assert.Equal(3, comic.PageCount);
    }

    [Fact]
    public void A_page_holds_its_entry_bytes()
    {
        using var cbz = Cbz("b.jpg", "a.jpg");
        var comic = ComicBook.Open(cbz, "Test");

        Assert.Equal("a.jpg", Encoding.UTF8.GetString(comic.Page(0).Span));
        Assert.Equal("b.jpg", Encoding.UTF8.GetString(comic.Page(1).Span));
    }

    [Fact]
    public void Pages_in_folders_are_found()
    {
        using var cbz = Cbz("Issue 1/02.jpg", "Issue 1/00Front Cover.jpg", "Issue 1/01.jpg");
        var comic = ComicBook.Open(cbz, "Test");

        Assert.Equal(["Issue 1/00Front Cover.jpg", "Issue 1/01.jpg", "Issue 1/02.jpg"], comic.PageNames);
    }

    [Theory]
    [InlineData("01.jpg", true)]
    [InlineData("01.JPG", true)]
    [InlineData("cover.webp", true)]
    [InlineData("ComicInfo.xml", false)]
    [InlineData(".hidden.jpg", false)]
    [InlineData("__MACOSX/._01.jpg", false)]
    public void Only_images_are_pages(string name, bool isPage)
    {
        Assert.Equal(isPage, ComicBook.IsImage(name));
    }

    [Fact]
    public void Its_ComicInfo_is_read()
    {
        using var cbz = Cbz(("1.jpg", "1"), ("ComicInfo.xml", "<ComicInfo><Series>Troll</Series><Year>1993</Year></ComicInfo>"));
        var comic = ComicBook.Open(cbz, "Troll 001 (1993)");

        Assert.Equal("Troll", comic.Info?.Series);
        Assert.Equal("Troll (1993)", comic.Heading);
        Assert.Equal(["1.jpg"], comic.PageNames);
    }

    [Fact]
    public void Without_a_ComicInfo_the_heading_is_the_name()
    {
        using var cbz = Cbz("1.jpg");
        var comic = ComicBook.Open(cbz, "Troll 001 (1993)");

        Assert.Null(comic.Info);
        Assert.Equal("Troll 001 (1993)", comic.Heading);
    }

    [Fact]
    public void A_broken_ComicInfo_does_not_stop_the_comic_opening()
    {
        using var cbz = Cbz(("1.jpg", "1"), ("ComicInfo.xml", "<ComicInfo><Series>"));
        var comic = ComicBook.Open(cbz, "Troll 001 (1993)");

        Assert.Null(comic.Info);
        Assert.Equal(1, comic.PageCount);
        Assert.Equal("Troll 001 (1993)", comic.Heading);
    }
}
