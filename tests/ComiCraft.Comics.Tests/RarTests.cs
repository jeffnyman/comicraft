namespace ComiCraft.Comics.Tests;

/// <summary>That the RARs the tests make are RARs, as SharpCompress and this library read them.</summary>
public class RarTests
{
    [Fact]
    public void The_end_of_a_test_rar_is_the_end_every_rar_has()
    {
        byte[] rar = TestComics.Rar();

        Assert.Equal([0xC4, 0x3D, 0x7B, 0x00, 0x40, 0x07, 0x00], rar[^7..]);
    }

    [Fact]
    public void A_test_rar_is_read_as_a_comic()
    {
        using var stream = new MemoryStream(TestComics.Rar(("Issue 1/2.jpg", "two"), ("Issue 1/1.jpg", "one"), ("ComicInfo.xml", "<ComicInfo><Series>Troll</Series></ComicInfo>")));
        var comic = ComicBook.Open(stream, "Troll");

        Assert.Equal(["Issue 1/1.jpg", "Issue 1/2.jpg"], comic.PageNames);
        Assert.Equal("one"u8.ToArray(), comic.Page(0).ToArray());
        Assert.Equal("Troll", comic.Info?.Series);
        Assert.Equal(ArchiveKind.Rar, ComicArchive.KindOf(TestComics.Rar()));
    }
}
