using ComiCraft.Comics;
using ComiCraft.Comics.Tests;
using ComiCraft.Viewer.Cli;
using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Tests;

/// <summary>comicraft tag --lookup, on comics in a folder of the test's own, with Metron as the fake one answers.</summary>
public sealed class LookupCommandTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-lookup-").FullName;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly FakeMetron _metron = FakeMetron.WithSecretWars();
    private readonly ReadingPlaces _places;
    private readonly ListTrash _trash = new();

    public LookupCommandTests() => _places = new ReadingPlaces(Path.Combine(_folder, ".state", "reading.json"));

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>A Trash that takes everything, noting what.</summary>
    private sealed class ListTrash : ITrash
    {
        public bool IsAvailable => true;

        public List<string> Moved { get; } = [];

        public Trashed Move(string path)
        {
            File.Delete(path);
            Moved.Add(Path.GetFileName(path));
            return Trashed.To(null);
        }
    }

    private string Output => _out.ToString().ReplaceLineEndings("\n");

    private string Errors => _error.ToString().ReplaceLineEndings("\n");

    private Terminal Terminal(params string[]? answers) =>
        answers is null
            ? new Terminal(TextReader.Null, _out, _error, canAsk: false)
            : new Terminal(new StringReader(string.Join("\n", answers) + "\n"), _out, _error, canAsk: true);

    private int Lookup(Terminal terminal, params string[] args) =>
        TagCommand.Run(["--lookup", .. args], terminal, _trash, () => _metron.Client(_out), _places);

    private string Cbz(string name, string? info = null) =>
        info is null
            ? TestComics.CbzFile(_folder, name, ("1.jpg", "one"), ("2.jpg", "two"))
            : TestComics.CbzFile(_folder, name, ("1.jpg", "one"), ("2.jpg", "two"), ("ComicInfo.xml", info));

    private string Cbr(string name) => TestComics.CbrFile(_folder, name, ("1.jpg", "one"), ("2.jpg", "two"));

    private string[] Files() =>
        Directory.EnumerateFiles(_folder).Select(Path.GetFileName).Where(f => !f!.StartsWith('.')).Order().ToArray()!;

    private string? Field(string file, string field) => ComicArchive.ReadComicInfo(Path.Combine(_folder, file))?.Get(field);

    private void SecretWars(int issues = 3)
    {
        for (int n = 1; n <= issues; n++)
        {
            Cbz($"Marvel Super-Heroes Secret Wars {n:00} (of 12) ({(n > 8 ? 1985 : 1984)}).cbz");
        }
    }

    [Fact]
    public void A_folder_is_looked_up_as_one_series_chosen_once_and_each_comic_found_by_its_number()
    {
        SecretWars();

        Assert.Equal(Exit.Done, Lookup(Terminal("1", "y"), _folder));

        Assert.Contains("1. Marvel Super Heroes Secret Wars (1984): Marvel, 12 issues, 1984-1985  (id 1001)", Output);
        Assert.Equal(1, _metron.Asked.Count(a => a.StartsWith("series/", StringComparison.Ordinal)));
        Assert.Equal("Prisoners of War", Field("Marvel Super-Heroes Secret Wars 02 (of 12) (1984).cbz", "Title"));
        Assert.Equal("6", Field("Marvel Super-Heroes Secret Wars 02 (of 12) (1984).cbz", "Month"));
        Assert.Equal("Jim Shooter", Field("Marvel Super-Heroes Secret Wars 03 (of 12) (1984).cbz", "Writer"));
        Assert.Contains("Done: 3 changed.", Output);
    }

    [Fact]
    public void Rename_names_each_comic_by_its_cover_date_series_and_number()
    {
        SecretWars();

        Assert.Equal(Exit.Done, Lookup(Terminal("1", "y"), _folder, "--rename"));

        Assert.Equal(
            [
                "198405 - Marvel Super Heroes Secret Wars 01.cbz",
                "198406 - Marvel Super Heroes Secret Wars 02.cbz",
                "198407 - Marvel Super Heroes Secret Wars 03.cbz",
            ],
            Files());
        Assert.Equal("Tempest Fugit", Field("198407 - Marvel Super Heroes Secret Wars 03.cbz", "Title"));
        Assert.Contains("  becomes 198405 - Marvel Super Heroes Secret Wars 01.cbz", Output);
    }

    [Fact]
    public void Rename_takes_a_pattern_of_its_own()
    {
        SecretWars(1);

        Lookup(Terminal("1", "y"), _folder, "--rename={series} {number:000} ({year})");

        Assert.Equal(["Marvel Super Heroes Secret Wars 001 (1984).cbz"], Files());
    }

    [Fact]
    public void Everything_is_shown_before_anything_is_done_and_no_changes_nothing()
    {
        SecretWars(2);
        var before = Files();

        Assert.Equal(Exit.CannotRun, Lookup(Terminal("1", "n"), _folder, "--rename"));

        Assert.Equal(before, Files());
        Assert.Null(Field(before[0], "Series"));
        Assert.Contains("  is Marvel Super Heroes Secret Wars #1 – The War Begins! (May 1984)", Output);
        Assert.Contains("Make these changes to 2 comics? [y/N]", Output);
        Assert.Contains("Nothing was changed.", Output);
    }

    [Fact]
    public void Dry_run_shows_the_changes_and_makes_none()
    {
        SecretWars(2);
        var before = Files();

        Assert.Equal(Exit.Done, Lookup(Terminal("1"), _folder, "--rename", "--dry-run"));

        Assert.Equal(before, Files());
        Assert.Contains("sets Title, Series, Number, Count, Volume, Summary, Year, Month, Writer", Output);
        Assert.Contains("Nothing was changed (--dry-run).", Output);
        Assert.DoesNotContain("Make these changes", Output);
    }

    [Fact]
    public void Choosing_none_of_the_series_skips_the_comics()
    {
        SecretWars(2);

        Assert.Equal(Exit.CannotRun, Lookup(Terminal("0"), _folder));

        Assert.Contains("Skipped 2 comics.", Output);
        Assert.DoesNotContain(_metron.Asked, a => a.StartsWith("issue/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_script_takes_the_series_only_when_just_one_fits()
    {
        SecretWars(1);

        Assert.Equal(Exit.Done, Lookup(Terminal(null), _folder, "--rename"));

        Assert.Equal(["198405 - Marvel Super Heroes Secret Wars 01.cbz"], Files());
    }

    [Fact]
    public void A_script_that_cannot_tell_which_series_says_how_to_and_changes_nothing()
    {
        Cbz("Secret Wars 01.cbz");
        var before = Files();

        Assert.Equal(Exit.CannotRun, Lookup(Terminal(null), _folder));

        Assert.Equal(before, Files());
        Assert.Contains("give --series-id", Errors);
        Assert.Contains("1001: Marvel Super Heroes Secret Wars (1984)", Errors);
    }

    [Fact]
    public void Series_id_says_which_series_without_a_search()
    {
        Cbz("Secret Wars 01.cbz");

        Assert.Equal(Exit.Done, Lookup(Terminal(null), _folder, "--series-id", "1001"));

        Assert.Equal("Marvel Super Heroes Secret Wars", Field("Secret Wars 01.cbz", "Series"));
        Assert.DoesNotContain(_metron.Asked, a => a.StartsWith("series/?", StringComparison.Ordinal));
    }

    [Fact]
    public void The_comics_own_series_and_number_say_which_it_is_before_its_name()
    {
        Cbz("scan0042.cbz", "<ComicInfo><Series>Marvel Super Heroes Secret Wars</Series><Number>4</Number><Year>1984</Year><Notes>Mine</Notes></ComicInfo>");

        Assert.Equal(Exit.Done, Lookup(Terminal(null), _folder));

        Assert.Equal("Situation: Hopeless!", Field("scan0042.cbz", "Title"));
        Assert.Equal("Mine", Field("scan0042.cbz", "Notes"));
    }

    [Fact]
    public void A_comic_whose_issue_is_not_in_the_series_is_reported_and_the_rest_are_done()
    {
        SecretWars(1);
        Cbz("Marvel Super-Heroes Secret Wars 13 (of 12) (1984).cbz");

        Assert.Equal(Exit.CannotRun, Lookup(Terminal("1", "y"), _folder));

        Assert.Contains("has no issue 13", Errors);
        Assert.Equal("The War Begins!", Field("Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbz", "Title"));
        Assert.Contains("Done: 1 changed, 1 not.", Output);
    }

    [Fact]
    public void A_comic_whose_name_says_nothing_is_reported()
    {
        Cbz("Watchmen.cbz");

        Assert.Equal(Exit.CannotRun, Lookup(Terminal(null), _folder));

        Assert.Contains("does not say which issue it is", Errors);
        Assert.Empty(_metron.Asked);
    }

    [Fact]
    public void A_cbr_is_converted_to_its_new_name_and_its_original_settled_as_asked()
    {
        Cbr("Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr");

        Assert.Equal(Exit.Done, Lookup(Terminal("1", "y", "y", "y"), _folder, "--rename"));

        Assert.Equal(["198405 - Marvel Super Heroes Secret Wars 01.cbz"], Files());
        Assert.Equal("The War Begins!", Field("198405 - Marvel Super Heroes Secret Wars 01.cbz", "Title"));
        Assert.Equal(["Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr"], _trash.Moved);
        Assert.Contains("  converts to 198405 - Marvel Super Heroes Secret Wars 01.cbz", Output);
        Assert.Contains("is a CBR, which cannot be changed. Convert it to a CBZ and change that?", Output);
    }

    [Fact]
    public void A_script_needs_convert_for_a_cbr_and_keeps_the_original_unless_told()
    {
        Cbr("Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr");

        Assert.Equal(Exit.CannotRun, Lookup(Terminal(null), _folder));
        Assert.Contains("give --convert", Errors);
        Assert.Equal(["Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr"], Files());

        Assert.Equal(Exit.Done, Lookup(Terminal(null), _folder, "--convert", "--rename"));
        Assert.Equal(["198405 - Marvel Super Heroes Secret Wars 01.cbz", "Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr"], Files());
        Assert.Empty(_trash.Moved);
    }

    [Fact]
    public void A_name_already_taken_is_not_written_over()
    {
        SecretWars(1);
        Cbz("198405 - Marvel Super Heroes Secret Wars 01.cbz", "<ComicInfo><Series>Something Else</Series><Number>9</Number></ComicInfo>");

        Lookup(Terminal(null), Path.Combine(_folder, "Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbz"), "--rename");

        Assert.Contains("that is already there; it keeps its name", Errors);
        Assert.Equal("Something Else", Field("198405 - Marvel Super Heroes Secret Wars 01.cbz", "Series"));
        Assert.Equal("The War Begins!", Field("Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbz", "Title"));
    }

    [Fact]
    public void Where_the_reader_was_moves_with_a_renamed_comic()
    {
        SecretWars(1);
        string old = Path.Combine(_folder, "Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbz");
        _places.Remember(old, new Place(1, Fit.Width, 1, 0, 0));

        Lookup(Terminal(null), _folder, "--rename");

        Assert.Null(_places.Recall(old));
        Assert.Equal(1, _places.Recall(Path.Combine(_folder, "198405 - Marvel Super Heroes Secret Wars 01.cbz"))?.Page);
    }

    [Fact]
    public void A_comic_that_already_says_what_Metron_does_is_left_alone()
    {
        SecretWars(1);
        Lookup(Terminal(null), _folder);
        _out.GetStringBuilder().Clear();
        var written = File.GetLastWriteTimeUtc(Path.Combine(_folder, Files()[0]));

        Assert.Equal(Exit.Done, Lookup(Terminal(null), _folder));

        Assert.Contains("Every comic already says what Metron does; nothing was changed.", Output);
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(_folder, Files()[0])));
    }

    [Fact]
    public void Without_credentials_it_says_how_to_give_them_and_changes_nothing()
    {
        SecretWars(1);

        int exit = TagCommand.Run(["--lookup", _folder], Terminal(null), _trash, () => throw new ComiCraft.Viewer.Lookup.MetronException("Looking comics up needs a Metron account"), _places);

        Assert.Equal(Exit.CannotRun, exit);
        Assert.Contains("needs a Metron account", Errors);
        Assert.Contains("Nothing was changed.", Output);
    }

    [Theory]
    [InlineData("--series", "Troll")]
    [InlineData("--rename={issue}")]
    [InlineData("--series-id", "abc")]
    public void Lookup_refuses_what_it_cannot_do(params string[] options)
    {
        SecretWars(1);

        Assert.Throws<UsageException>(() => Lookup(Terminal(null), [_folder, .. options]));
    }
}
