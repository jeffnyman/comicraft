using ComiCraft.Comics;
using ComiCraft.Comics.Tests;
using ComiCraft.Viewer.Cli;
using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Tests;

/// <summary>comicraft tag and convert, run on comics in a folder of the test's own, with answers given as typed.</summary>
public sealed class CommandTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-commands-").FullName;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly FakeTrash _trash;

    public CommandTests() => _trash = new FakeTrash(Path.Combine(_folder, ".trash"));

    /// <summary>A Trash that is a folder of the test's own; or, when told, one that will not take anything.</summary>
    private sealed class FakeTrash(string folder, string? refusal = null) : ITrash
    {
        public bool IsAvailable => true;

        public List<string> Moved { get; } = [];

        public Trashed Move(string path)
        {
            if (refusal is not null)
            {
                return Trashed.Not(refusal);
            }

            Directory.CreateDirectory(folder);
            string into = Path.Combine(folder, Path.GetFileName(path));
            File.Move(path, into);
            Moved.Add(Path.GetFileName(path));
            return Trashed.To(into);
        }
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private const string Info = "<ComicInfo><Series>Troll</Series><Publisher>Image</Publisher></ComicInfo>";

    private string Output => _out.ToString().ReplaceLineEndings("\n");

    private string Errors => _error.ToString();

    /// <summary>A terminal someone is at, typing the answers; or, with none, one run from a script.</summary>
    private Terminal Terminal(params string[]? answers) =>
        answers is null
            ? new Terminal(TextReader.Null, _out, _error, canAsk: false)
            : new Terminal(new StringReader(string.Join("\n", answers) + "\n"), _out, _error, canAsk: true);

    private int Tag(Terminal terminal, params string[] args) => TagCommand.Run(args, terminal, _trash);

    private int TagWith(ITrash trash, Terminal terminal, params string[] args) => TagCommand.Run(args, terminal, trash);

    private string Cbz(string name = "troll.cbz") => TestComics.CbzFile(_folder, name, ("1.jpg", "one"), ("2.jpg", "two"), ("ComicInfo.xml", Info));

    private string Cbr(string name = "troll.cbr") => TestComics.CbrFile(_folder, name, ("1.jpg", "one"), ("2.jpg", "two"), ("ComicInfo.xml", Info));

    private string[] Files() => Directory.EnumerateFiles(_folder).Select(Path.GetFileName).Order().ToArray()!;

    [Fact]
    public void Tag_shows_the_fields()
    {
        Assert.Equal(Exit.Done, Tag(Terminal(), Cbz()));

        Assert.Equal("Troll\n  Series     Troll\n  Publisher  Image\n", Output);
    }

    [Fact]
    public void Tag_says_when_there_is_no_comic_info()
    {
        string path = TestComics.CbzFile(_folder, "bare.cbz", ("1.jpg", "one"));

        Tag(Terminal(), path);

        Assert.Equal("bare.cbz has no ComicInfo.xml.\n", Output);
    }

    [Fact]
    public void Tag_changes_fields_and_says_what_changed()
    {
        string path = Cbz();

        Assert.Equal(Exit.Done, Tag(Terminal(), path, "--publisher", "Image Comics", "--cover-artist", "Rob Liefeld", "--Year=1993"));

        Assert.Equal(
            "  Publisher    Image -> Image Comics\n  CoverArtist  (none) -> Rob Liefeld\n  Year         (none) -> 1993\nSaved troll.cbz.\n",
            Output);
        var info = ComicBook.Open(path).Info!;
        Assert.Equal(("Image Comics", 1993), (info.Publisher, info.Year));
        Assert.Equal(["Rob Liefeld"], info.CoverArtists);
    }

    [Fact]
    public void Tag_removes_a_field_given_nothing()
    {
        string path = Cbz();

        Tag(Terminal(), path, "--publisher", "");

        Assert.Contains("Publisher  Image -> (removed)", Output);
        Assert.Null(ComicBook.Open(path).Info!.Publisher);
    }

    [Fact]
    public void Tag_that_changes_nothing_leaves_the_file_alone()
    {
        string path = Cbz();
        byte[] before = File.ReadAllBytes(path);
        var written = File.GetLastWriteTimeUtc(path);

        Assert.Equal(Exit.Done, Tag(Terminal(), path, "--series", " Troll "));

        Assert.Equal("troll.cbz already says that; it is left as it is.\n", Output);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData("--month", "13", "Month must be a whole number from 1 to 12, not '13'.")]
    [InlineData("--artist", "Rob", "ComicInfo.xml has no field 'artist'.")]
    [InlineData("--series", null, "--series needs a value")]
    public void Tag_with_a_mistake_changes_nothing(string option, string? value, string message)
    {
        string path = Cbz();
        byte[] before = File.ReadAllBytes(path);
        string[] args = value is null ? [path, "--year", "1993", option] : [path, "--year", "1993", option, value];

        var ex = Assert.Throws<UsageException>(() => Tag(Terminal(), args));

        Assert.StartsWith(message, ex.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Tag_on_a_cbr_asks_to_convert_it_and_then_whether_to_delete_it()
    {
        string path = Cbr();

        Assert.Equal(Exit.Done, Tag(Terminal("y", "y"), path, "--year", "1993"));

        Assert.Equal(
            "troll.cbr is a CBR, which cannot be changed. Convert it to a CBZ, troll.cbz, and change that? [y/N] "
            + "Converted troll.cbr to troll.cbz.\n"
            + "  Year  (none) -> 1993\n"
            + "Saved troll.cbz.\n"
            + "Move the original, troll.cbr, to the Trash? [y/N] Moved troll.cbr to the Trash.\n",
            Output);
        Assert.Equal(["troll.cbz"], Files());
        Assert.Equal(["troll.cbr"], _trash.Moved);
        Assert.Equal(1993, ComicBook.Open(Path.Combine(_folder, "troll.cbz")).Info!.Year);
    }

    [Fact]
    public void Tag_on_a_cbr_keeps_it_unless_told_to_trash_it()
    {
        string path = Cbr();

        Tag(Terminal("y", ""), path, "--year", "1993");

        Assert.EndsWith("Move the original, troll.cbr, to the Trash? [y/N] Kept troll.cbr.\n", Output);
        Assert.Equal(["troll.cbr", "troll.cbz"], Files());
    }

    [Fact]
    public void Tag_on_a_cbr_told_no_changes_nothing()
    {
        string path = Cbr();
        byte[] before = File.ReadAllBytes(path);

        Assert.Equal(Exit.CannotRun, Tag(Terminal("n"), path, "--year", "1993"));

        Assert.EndsWith("Nothing was changed.\n", Output);
        Assert.Equal(["troll.cbr"], Files());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Tag_on_a_cbr_with_convert_converts_without_asking()
    {
        string path = Cbr();

        Tag(Terminal("n"), path, "--year", "1993", "--convert");

        Assert.StartsWith("Converted troll.cbr to troll.cbz.\n", Output);
        Assert.EndsWith("Move the original, troll.cbr, to the Trash? [y/N] Kept troll.cbr.\n", Output);
    }

    [Fact]
    public void Tag_on_a_cbr_from_a_script_needs_convert()
    {
        string path = Cbr();

        Assert.Equal(Exit.CannotRun, Tag(Terminal(null), path, "--year", "1993"));

        Assert.Contains("Give --convert to convert it", Errors);
        Assert.Equal(["troll.cbr"], Files());
    }

    [Fact]
    public void Tag_on_a_cbr_from_a_script_keeps_the_original_unless_told()
    {
        Tag(Terminal(null), Cbr("a.cbr"), "--year", "1993", "--convert");
        Assert.EndsWith("Kept a.cbr; give --delete-original to move it to the Trash.\n", Output);

        Tag(Terminal(null), Cbr("b.cbr"), "--year", "1993", "--convert", "--delete-original");
        Assert.EndsWith("Moved b.cbr to the Trash.\n", Output);

        Assert.Equal(["a.cbr", "a.cbz", "b.cbz"], Files());
    }

    [Fact]
    public void Tag_on_a_cbr_that_would_change_nothing_does_not_convert_it()
    {
        string path = Cbr();

        Tag(Terminal("y", "y"), path, "--series", "Troll");

        Assert.Equal("troll.cbr already says that; it is left as it is.\n", Output);
        Assert.Equal(["troll.cbr"], Files());
    }

    [Fact]
    public void Tag_shows_a_cbr_as_it_does_a_cbz()
    {
        Tag(Terminal(), Cbr());

        Assert.Equal("Troll\n  Series     Troll\n  Publisher  Image\n", Output);
    }

    [Fact]
    public void Tag_without_changes_takes_no_conversion_options()
    {
        Assert.Throws<UsageException>(() => Tag(Terminal(), Cbz(), "--convert"));
    }

    [Fact]
    public void Convert_makes_a_cbz_and_asks_about_the_original()
    {
        string path = Cbr();

        Assert.Equal(Exit.Done, ConvertCommand.Run([path], Terminal("yes"), _trash));

        Assert.Equal("Converted troll.cbr to troll.cbz (2 pages).\nMove the original, troll.cbr, to the Trash? [y/N] Moved troll.cbr to the Trash.\n", Output);
        Assert.Equal(["troll.cbz"], Files());
    }

    [Fact]
    public void Convert_to_a_name_of_its_own_choosing()
    {
        string path = Cbr();

        ConvertCommand.Run([path, "--to", Path.Combine(_folder, "Troll 001.cbz"), "--keep-original"], Terminal(), _trash);

        Assert.Equal(["Troll 001.cbz", "troll.cbr"], Files());
    }

    [Fact]
    public void Convert_never_writes_over_a_file()
    {
        string path = Cbr();
        File.WriteAllText(Path.Combine(_folder, "troll.cbz"), "someone's file");

        Assert.Throws<ComicEditException>(() => ConvertCommand.Run([path, "--delete-original"], Terminal(), _trash));

        Assert.Equal("someone's file", File.ReadAllText(Path.Combine(_folder, "troll.cbz")));
        Assert.Equal(["troll.cbr", "troll.cbz"], Files());
    }

    [Fact]
    public void The_original_is_not_deleted_when_the_conversion_does_not_have_its_pages()
    {
        string original = Cbr();
        string converted = TestComics.CbzFile(_folder, "troll.cbz", ("1.jpg", "one"));

        Originals.Settle(original, converted, Original.Delete, Terminal(), _trash);

        Assert.Contains("does not have the same pages as troll.cbr, so troll.cbr is kept.", Errors);
        Assert.Equal(["troll.cbr", "troll.cbz"], Files());
    }

    [Fact]
    public void Delete_and_keep_cannot_both_be_given()
    {
        Assert.Throws<UsageException>(() => ConvertCommand.Run([Cbr(), "--delete-original", "--keep-original"], Terminal(), _trash));
    }

    [Fact]
    public void A_missing_comic_is_not_found()
    {
        Assert.Throws<FileNotFoundException>(() => Tag(Terminal(), Path.Combine(_folder, "nowhere.cbz")));
    }

    [Fact]
    public void Where_there_is_no_trash_the_question_says_the_original_goes_for_good()
    {
        string path = Cbr();

        TagWith(new NoTrash(), Terminal("y", "y"), path, "--year", "1993");

        Assert.EndsWith("Delete the original, troll.cbr, permanently? [y/N] Deleted troll.cbr.\n", Output);
        Assert.Equal(["troll.cbz"], Files());
    }

    [Fact]
    public void An_original_the_trash_will_not_take_is_deleted_for_good_only_when_said()
    {
        var refusing = new FakeTrash(Path.Combine(_folder, ".trash"), "the volume has no Trash");

        TagWith(refusing, Terminal("y", "y", "n"), Cbr("a.cbr"), "--year", "1993");
        Assert.EndsWith("Delete a.cbr permanently instead? [y/N] Kept a.cbr.\n", Output);

        TagWith(refusing, Terminal("y", "y", "y"), Cbr("b.cbr"), "--year", "1993");
        Assert.EndsWith("Delete b.cbr permanently instead? [y/N] Deleted b.cbr.\n", Output);

        Assert.Contains("a.cbr could not be moved to the Trash: the volume has no Trash", Errors);
        Assert.Equal(["a.cbr", "a.cbz", "b.cbz"], Files());
    }

    [Fact]
    public void A_script_keeps_an_original_the_trash_will_not_take()
    {
        var refusing = new FakeTrash(Path.Combine(_folder, ".trash"), "the volume has no Trash");

        TagWith(refusing, Terminal(null), Cbr(), "--year", "1993", "--convert", "--delete-original");

        Assert.Contains("troll.cbr could not be moved to the Trash: the volume has no Trash", Errors);
        Assert.EndsWith("Kept troll.cbr.\n", Output);
        Assert.Equal(["troll.cbr", "troll.cbz"], Files());
    }
}
