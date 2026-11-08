using System.IO.Compression;
using System.Text;

namespace ComiCraft.Comics.Tests;

/// <summary>Tests that change comics on disk, each in a folder of its own that goes when it is done.</summary>
public sealed class ComicArchiveTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-tests-").FullName;

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_folder))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_folder, recursive: true);
    }

    private const string Info = "<ComicInfo><Series>Troll</Series><Publisher>Image</Publisher></ComicInfo>";

    private string Comic(params (string Name, string Text)[] entries) => TestComics.CbzFile(_folder, "comic.cbz", entries);

    private static Dictionary<string, (long Compressed, uint Crc, string Text)> Entries(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open());
            return (e.CompressedLength, e.Crc32, reader.ReadToEnd());
        });
    }

    private static ComicInfoDocument Edited(string path, string field, string value)
    {
        var document = ComicArchive.ReadComicInfo(path) ?? ComicInfoDocument.New();
        document.Set(field, value);
        return document;
    }

    /// <summary>Nothing is left beside the comics but the comics.</summary>
    private void AssertNoCopiesLeft(params string[] files) =>
        Assert.Equal(files.Order(), Directory.EnumerateFiles(_folder).Select(Path.GetFileName).Order());

    [Theory]
    [InlineData(new byte[] { (byte)'P', (byte)'K', 3, 4, 0, 0, 0, 0 }, ArchiveKind.Zip)]
    [InlineData(new byte[] { (byte)'P', (byte)'K', 5, 6, 0, 0, 0, 0 }, ArchiveKind.Zip)]
    [InlineData(new byte[] { (byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 7, 0, 0xCF }, ArchiveKind.Rar)]
    [InlineData(new byte[] { (byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 7, 1, 0 }, ArchiveKind.Rar)]
    [InlineData(new byte[] { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C, 0, 4 }, ArchiveKind.SevenZip)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ArchiveKind.Unknown)]
    [InlineData(new byte[] { }, ArchiveKind.Unknown)]
    public void An_archive_is_known_by_its_first_bytes(byte[] start, ArchiveKind kind)
    {
        Assert.Equal(kind, ComicArchive.KindOf(start));
    }

    [Fact]
    public void A_cbr_that_is_really_a_zip_is_a_zip()
    {
        string path = TestComics.CbzFile(_folder, "misnamed.cbr", ("1.jpg", "1"));

        Assert.Equal(ArchiveKind.Zip, ComicArchive.KindOf(path));
    }

    [Fact]
    public void The_comic_info_is_read_to_edit()
    {
        string path = Comic(("1.jpg", "1"), ("ComicInfo.xml", Info));

        Assert.Equal("Troll", ComicArchive.ReadComicInfo(path)?.Get("Series"));
    }

    [Fact]
    public void A_comic_without_comic_info_has_none_to_edit()
    {
        Assert.Null(ComicArchive.ReadComicInfo(Comic(("1.jpg", "1"))));
    }

    [Fact]
    public void A_comic_info_that_cannot_be_read_is_an_error()
    {
        string path = Comic(("1.jpg", "1"), ("ComicInfo.xml", "<ComicInfo><Series>"));

        Assert.Throws<ComicEditException>(() => ComicArchive.ReadComicInfo(path));
    }

    [Fact]
    public void Reading_lets_go_of_the_comic()
    {
        string path = Comic(("1.jpg", "1"), ("ComicInfo.xml", Info));
        ComicArchive.ReadComicInfo(path);

        // were the file still open, it could not be opened to itself
        using var alone = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void Writing_changes_the_comic_info_and_copies_the_pages_as_they_are()
    {
        string path = Comic(("1.jpg", "page one"), ("2.jpg", "page two"), ("ComicInfo.xml", Info), ("notes.txt", "notes"));
        var before = Entries(path);

        ComicArchive.WriteComicInfo(path, Edited(path, "Publisher", "Image Comics"));

        var after = Entries(path);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (string name in new[] { "1.jpg", "2.jpg", "notes.txt" })
        {
            Assert.Equal(before[name], after[name]);
        }

        Assert.Contains("<Publisher>Image Comics</Publisher>", after["ComicInfo.xml"].Text);
        Assert.Equal("Image Comics", ComicBook.Open(path).Info?.Publisher);
        AssertNoCopiesLeft("comic.cbz");
    }

    [Fact]
    public void Writing_adds_comic_info_to_a_comic_without_it()
    {
        string path = Comic(("1.jpg", "1"));

        ComicArchive.WriteComicInfo(path, Edited(path, "Series", "Troll"));

        Assert.Equal("Troll", ComicBook.Open(path).Info?.Series);
        Assert.Contains(ComicInfo.FileName, Entries(path).Keys);
    }

    [Fact]
    public void Writing_keeps_the_name_the_comic_info_had_and_leaves_one()
    {
        string path = Comic(("1.jpg", "1"), ("comicinfo.xml", Info), ("COMICINFO.XML", Info));

        ComicArchive.WriteComicInfo(path, Edited(path, "Series", "Troll II"));

        Assert.Equal(["1.jpg", "comicinfo.xml"], Entries(path).Keys.Order());
    }

    [Fact]
    public void A_read_only_comic_is_left_as_it_is()
    {
        string path = Comic(("1.jpg", "1"), ("ComicInfo.xml", Info));
        var document = Edited(path, "Series", "Troll II");
        byte[] before = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var ex = Assert.Throws<ComicEditException>(() => ComicArchive.WriteComicInfo(path, document));

        Assert.Equal("'comic.cbz' is read-only, so it is left as it is.", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
        AssertNoCopiesLeft("comic.cbz");
    }

    [Fact]
    public void A_rar_is_not_written_and_is_left_as_it_is()
    {
        string path = Path.Combine(_folder, "comic.cbr");
        byte[] rar = [(byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 7, 0, 0xCF, 0x90, 0x73];
        File.WriteAllBytes(path, rar);

        var ex = Assert.Throws<ComicEditException>(() => ComicArchive.WriteComicInfo(path, ComicInfoDocument.New()));

        Assert.Contains("Convert it to a CBZ", ex.Message);
        Assert.Equal(rar, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_write_that_fails_leaves_the_comic_as_it_was()
    {
        // not a zip at all, though it starts as one: the copy cannot be opened to update
        string path = Path.Combine(_folder, "comic.cbz");
        byte[] broken = [(byte)'P', (byte)'K', 3, 4, 1, 2, 3, 4, 5, 6];
        File.WriteAllBytes(path, broken);

        Assert.ThrowsAny<Exception>(() => ComicArchive.WriteComicInfo(path, ComicInfoDocument.New()));

        Assert.Equal(broken, File.ReadAllBytes(path));
        AssertNoCopiesLeft("comic.cbz");
    }

    [Fact]
    public void Converting_makes_a_cbz_of_everything_in_the_comic_and_keeps_the_comic()
    {
        // any archive SharpCompress reads converts the same way; a zip stands in for a RAR here
        string path = TestComics.CbzFile(_folder, "comic.cbr", ("Issue 1/1.jpg", "page one"), ("Issue 1/2.jpg", "page two"), ("ComicInfo.xml", Info));
        byte[] original = File.ReadAllBytes(path);

        string cbz = ComicArchive.ConvertToCbz(path);

        Assert.Equal(Path.Combine(_folder, "comic.cbz"), cbz);
        var entries = Entries(cbz);
        Assert.Equal(["ComicInfo.xml", "Issue 1/1.jpg", "Issue 1/2.jpg"], entries.Keys.Order());
        Assert.Equal("page one", entries["Issue 1/1.jpg"].Text);
        Assert.Equal(["Issue 1/1.jpg", "Issue 1/2.jpg"], ComicBook.Open(cbz).PageNames);
        Assert.Equal(original, File.ReadAllBytes(path));
        AssertNoCopiesLeft("comic.cbr", "comic.cbz");
    }

    [Fact]
    public void A_real_rar_is_converted()
    {
        string path = TestComics.CbrFile(_folder, "comic.cbr", ("Issue 1/1.jpg", "page one"), ("Issue 1/2.jpg", "page two"), ("ComicInfo.xml", Info));

        string cbz = ComicArchive.ConvertToCbz(path);

        Assert.Equal(ArchiveKind.Zip, ComicArchive.KindOf(cbz));
        Assert.Equal(["ComicInfo.xml", "Issue 1/1.jpg", "Issue 1/2.jpg"], Entries(cbz).Keys.Order());
        Assert.Equal("page two", Entries(cbz)["Issue 1/2.jpg"].Text);
        Assert.Equal("Troll", ComicArchive.ReadComicInfo(path)?.Get("Series"));
    }

    [Fact]
    public void A_real_rar_is_not_written()
    {
        string path = TestComics.CbrFile(_folder, "comic.cbr", ("1.jpg", "1"), ("ComicInfo.xml", Info));
        byte[] before = File.ReadAllBytes(path);

        Assert.Throws<ComicEditException>(() => ComicArchive.WriteComicInfo(path, Edited(path, "Series", "Troll II")));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Pages_are_stored_as_they_are_when_converted()
    {
        string path = TestComics.CbzFile(_folder, "comic.cbr", ("1.jpg", new string('x', 10_000)), ("ComicInfo.xml", Info + new string(' ', 10_000)));

        string cbz = ComicArchive.ConvertToCbz(path);

        using var zip = ZipFile.OpenRead(cbz);
        Assert.Equal(10_000, zip.GetEntry("1.jpg")!.CompressedLength);
        Assert.True(zip.GetEntry("ComicInfo.xml")!.CompressedLength < 1_000);
    }

    [Fact]
    public void Converting_never_writes_over_a_file()
    {
        string path = TestComics.CbzFile(_folder, "comic.cbr", ("1.jpg", "1"));
        File.WriteAllText(Path.Combine(_folder, "comic.cbz"), "someone's file");

        var ex = Assert.Throws<ComicEditException>(() => ComicArchive.ConvertToCbz(path));

        Assert.Equal("'comic.cbz' is already there, and is left as it is.", ex.Message);
        Assert.Equal("someone's file", File.ReadAllText(Path.Combine(_folder, "comic.cbz")));
        AssertNoCopiesLeft("comic.cbr", "comic.cbz");
    }

    [Fact]
    public void Converting_to_a_name_of_its_own_choosing()
    {
        string path = TestComics.CbzFile(_folder, "comic.cbr", ("1.jpg", "1"));

        string cbz = ComicArchive.ConvertToCbz(path, Path.Combine(_folder, "Troll 001.cbz"));

        Assert.Equal(["1.jpg"], ComicBook.Open(cbz).PageNames);
    }

    [Fact]
    public void A_cbz_is_not_converted_to_itself()
    {
        string path = Comic(("1.jpg", "1"));

        Assert.Throws<ComicEditException>(() => ComicArchive.ConvertToCbz(path));
    }
}
