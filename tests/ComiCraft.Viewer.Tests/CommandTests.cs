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

    /// <summary>A comic whose pages are images, as a PDF needs them to be.</summary>
    private string Pictured(string name = "troll.cbz") =>
        TestComics.CbzFile(_folder, name, ("1.jpg", TestImages.Jpeg(10, 15)), ("2.jpg", TestImages.Jpeg(10, 15)), ("ComicInfo.xml", System.Text.Encoding.UTF8.GetBytes(Info)));

    [Fact]
    public void Convert_makes_a_pdf_and_asks_about_the_original()
    {
        string path = Pictured();

        Assert.Equal(Exit.Done, ConvertCommand.Run([path, "--format", "pdf"], Terminal("yes"), _trash));

        Assert.Equal("Converted troll.cbz to troll.pdf (2 pages).\nMove the original, troll.cbz, to the Trash? [y/N] Moved troll.cbz to the Trash.\n", Output);
        Assert.Equal(["troll.pdf"], Files());
    }

    [Theory]
    [InlineData("--format=pdf")]
    [InlineData("--format=PDF")]
    public void Convert_takes_its_format_either_way(string option)
    {
        ConvertCommand.Run([Pictured(), option, "--keep-original"], Terminal(), _trash);

        Assert.Equal(["troll.cbz", "troll.pdf"], Files());
    }

    [Fact]
    public void Convert_to_a_pdf_named_by_to_needs_no_format()
    {
        ConvertCommand.Run([Pictured(), "--to", Path.Combine(_folder, "Troll 001.pdf"), "--keep-original"], Terminal(), _trash);

        Assert.Equal(["Troll 001.pdf", "troll.cbz"], Files());
    }

    [Theory]
    [InlineData("--format", "epub")]
    [InlineData("--format", "cbz", "--to", "troll.pdf")]
    [InlineData("--format", "pdf", "--to", "troll.cbz")]
    [InlineData("--format")]
    public void Convert_refuses_a_format_it_cannot_make_or_that_says_two_things(params string[] options)
    {
        Assert.Throws<UsageException>(() => ConvertCommand.Run([Pictured(), .. options], Terminal(), _trash));

        Assert.Equal(["troll.cbz"], Files());
    }

    [Fact]
    public void The_original_is_not_deleted_when_the_pdf_read_back_does_not_have_its_pages()
    {
        string original = Pictured();
        string converted = Path.Combine(_folder, "troll.pdf");
        File.WriteAllText(converted, "not the comic");

        Originals.Settle(original, converted, Original.Delete, Terminal(), _trash, samePages: () => false);

        Assert.Contains("troll.pdf does not have the same pages as troll.cbz, so troll.cbz is kept.", Errors);
        Assert.Equal(["troll.cbz", "troll.pdf"], Files());
    }

    private int ConvertFolder(Terminal terminal, params string[] options) =>
        ConvertCommand.Run([_folder, .. options], terminal, _trash, _ => throw new InvalidOperationException("not an image"));

    [Fact]
    public void A_folders_cbrs_are_converted_asking_once_about_the_originals()
    {
        Cbr("b.cbr");
        Cbr("a.cbr");
        Cbz("c.cbz");

        Assert.Equal(Exit.Done, ConvertFolder(Terminal("yes")));

        Assert.Equal(
            $"2 comics in {Path.GetFileName(_folder)} to convert to CBZs.\n" +
            "Move each original to the Trash once it is converted and checked? [y/N] " +
            "[1/2] Converted a.cbr to a.cbz (2 pages).\nMoved a.cbr to the Trash.\n" +
            "[2/2] Converted b.cbr to b.cbz (2 pages).\nMoved b.cbr to the Trash.\n" +
            "Converted 2 of 2; moved 2 originals to the Trash.\n",
            Output);
        Assert.Equal(["a.cbz", "b.cbz", "c.cbz"], Files());
    }

    [Fact]
    public void A_folders_cbrs_and_cbzs_are_converted_to_pdfs()
    {
        Pictured("a.cbz");
        Pictured("b.CBZ");

        Assert.Equal(Exit.Done, ConvertFolder(Terminal(), "--format", "pdf", "--keep-original"));

        Assert.Equal(["a.cbz", "a.pdf", "b.CBZ", "b.pdf"], Files());
    }

    [Fact]
    public void A_comic_already_converted_is_skipped()
    {
        Cbr("a.cbr");
        Cbr("b.cbr");
        File.WriteAllText(Path.Combine(_folder, "a.cbz"), "someone's file");

        Assert.Equal(Exit.Done, ConvertFolder(Terminal(), "--delete-original"));

        Assert.Contains("[1/2] Skipped a.cbr: a.cbz is already there.\n", Output);
        Assert.Contains("Converted 1 of 2; skipped 1, already converted; moved 1 original to the Trash.\n", Output);
        Assert.Equal("someone's file", File.ReadAllText(Path.Combine(_folder, "a.cbz")));
        Assert.Equal(["a.cbr", "a.cbz", "b.cbz"], Files());
    }

    [Fact]
    public void A_comic_that_cannot_be_converted_is_reported_and_the_rest_go_on()
    {
        Pictured("a.cbz");
        Cbz("b.cbz"); // its pages are not images
        Pictured("c.cbz");

        Assert.Equal(Exit.CannotRun, ConvertFolder(Terminal(), "--format", "pdf", "--delete-original"));

        Assert.Contains("[2/3] b.cbz could not be converted: page 1 (1.jpg) cannot be put in a PDF: not an image", Errors);
        Assert.Contains("Converted 2 of 3; 1 could not be; moved 2 originals to the Trash.\n", Output);
        Assert.Equal(["a.pdf", "b.cbz", "c.pdf"], Files());
    }

    [Fact]
    public void Run_from_a_script_a_folders_originals_are_kept_and_it_says_how_not_to()
    {
        Cbr("a.cbr");

        ConvertFolder(Terminal(null));

        Assert.DoesNotContain("[y/N]", Output);
        Assert.EndsWith("Converted 1 of 1.\nKept the originals; give --delete-original to move them to the Trash.\n", Output);
        Assert.Equal(["a.cbr", "a.cbz"], Files());
    }

    [Fact]
    public void In_a_folder_an_original_the_trash_will_not_take_is_kept_without_asking()
    {
        Cbr("a.cbr");
        Cbr("b.cbr");
        var refusing = new FakeTrash(Path.Combine(_folder, ".trash"), refusal: "the drive has no Trash");

        // the one answer is to the one question; were another asked, it would be answered no
        Assert.Equal(Exit.Done, ConvertCommand.Run([_folder], Terminal("yes"), refusing));

        Assert.DoesNotContain("permanently instead", Output);
        Assert.Contains("a.cbr could not be moved to the Trash: the drive has no Trash", Errors);
        Assert.Equal(["a.cbr", "a.cbz", "b.cbr", "b.cbz"], Files());
    }

    [Fact]
    public void Recursive_goes_into_the_folders_in_it_but_not_hidden_ones()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Volume 2"));
        Directory.CreateDirectory(Path.Combine(_folder, ".Trash-1000"));
        Cbr("a.cbr");
        Cbr(Path.Combine("Volume 2", "b.cbr"));
        Cbr(Path.Combine(".Trash-1000", "c.cbr"));

        ConvertFolder(Terminal(), "--recursive");

        Assert.Contains($"[2/2] Converted {Path.Combine("Volume 2", "b.cbr")} to b.cbz (2 pages).", Output);
        Assert.True(File.Exists(Path.Combine(_folder, "Volume 2", "b.cbz")));
        Assert.False(File.Exists(Path.Combine(_folder, ".Trash-1000", "c.cbz")));
    }

    [Fact]
    public void Without_recursive_the_folders_in_it_are_left_alone()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Volume 2"));
        Cbr(Path.Combine("Volume 2", "b.cbr"));

        Assert.Equal(Exit.Done, ConvertFolder(Terminal()));

        Assert.Equal($"There are no CBRs in {Path.GetFileName(_folder)} to convert.\n", Output);
        Assert.False(File.Exists(Path.Combine(_folder, "Volume 2", "b.cbz")));
    }

    [Fact]
    public void To_is_for_one_comic_and_recursive_for_a_folder()
    {
        Assert.Throws<UsageException>(() => ConvertFolder(Terminal(), "--to", "x.cbz"));
        Assert.Throws<UsageException>(() => ConvertCommand.Run([Cbr(), "--recursive"], Terminal(), _trash));
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

    /// <summary>A Trash that takes the file and then says it did not.</summary>
    private sealed class LyingTrash : ITrash
    {
        public bool IsAvailable => true;

        public Trashed Move(string path)
        {
            File.Delete(path);
            return Trashed.Not("it went somewhere");
        }
    }

    [Fact]
    public void An_original_that_is_gone_though_the_trash_said_no_is_said_to_be_gone()
    {
        TagWith(new LyingTrash(), Terminal("y", "y"), Cbr(), "--year", "1993");

        Assert.Contains("troll.cbr is gone, but the system did not say it went to the Trash: it went somewhere", Errors);
        Assert.DoesNotContain("permanently instead", Output);
        Assert.Equal(["troll.cbz"], Files());
    }
}
