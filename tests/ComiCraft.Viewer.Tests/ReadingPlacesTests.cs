using ComiCraft.Viewer.Cli;

namespace ComiCraft.Viewer.Tests;

/// <summary>Where the reader was in each comic, kept in a file of the test's own; and comicraft forget.</summary>
public sealed class ReadingPlacesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-places-").FullName;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // in a folder not yet made, as it is before ComiCraft first remembers anything
    private ReadingPlaces Places() => new(Path.Combine(_folder, "state", "comicraft", "reading.json"));

    private string Comic(string name) => Path.Combine(_folder, name);

    private static Place At(int page, Fit fit = Fit.Page, double zoom = 1, double down = 0, double across = 0, int minutesAgo = 0) =>
        new(page, fit, zoom, down, across) { Read = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero).AddMinutes(-minutesAgo) };

    [Fact]
    public void A_comic_not_read_before_is_not_remembered()
    {
        Assert.Null(Places().Recall(Comic("troll.cbz")));
        Assert.False(File.Exists(Places().File));
    }

    [Fact]
    public void Where_the_reader_was_is_remembered_from_one_run_to_the_next()
    {
        var place = At(12, Fit.Zoom, zoom: 2.5, down: 0.4, across: 0.25);

        Places().Remember(Comic("troll.cbz"), place);

        Assert.Equal(place, Places().Recall(Comic("troll.cbz")));
        Assert.Null(Places().Recall(Comic("other.cbz")));
    }

    [Fact]
    public void A_comic_is_known_by_its_full_path_however_it_was_named()
    {
        Places().Remember(Path.Combine(_folder, "sub", "..", "troll.cbz"), At(3));

        Assert.Equal(3, Places().Recall(Comic("troll.cbz"))?.Page);
    }

    [Fact]
    public void Each_comic_keeps_its_own_place()
    {
        var places = Places();
        places.Remember(Comic("a.cbz"), At(1));
        places.Remember(Comic("b.cbz"), At(2));
        places.Remember(Comic("a.cbz"), At(5));

        Assert.Equal(5, places.Recall(Comic("a.cbz"))?.Page);
        Assert.Equal(2, places.Recall(Comic("b.cbz"))?.Page);
    }

    [Fact]
    public void Past_the_most_comics_those_read_longest_ago_are_forgotten()
    {
        var places = Places();
        for (int i = 0; i <= ReadingPlaces.MostComics; i++)
        {
            places.Remember(Comic($"{i}.cbz"), At(i, minutesAgo: i == 7 ? 100_000 : 0));
        }

        Assert.Null(places.Recall(Comic("7.cbz")));
        Assert.NotNull(places.Recall(Comic("6.cbz")));
        Assert.NotNull(places.Recall(Comic($"{ReadingPlaces.MostComics}.cbz")));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"format\": 2, \"comics\": {}}")]
    [InlineData("")]
    public void A_file_that_cannot_be_read_is_as_good_as_none_and_is_started_again(string contents)
    {
        var places = Places();
        Directory.CreateDirectory(Path.GetDirectoryName(places.File)!);
        File.WriteAllText(places.File, contents);

        Assert.Null(places.Recall(Comic("troll.cbz")));
        places.Remember(Comic("troll.cbz"), At(4));
        Assert.Equal(4, places.Recall(Comic("troll.cbz"))?.Page);
    }

    [Fact]
    public void Nothing_is_left_beside_the_file_once_it_is_written()
    {
        var places = Places();

        places.Remember(Comic("troll.cbz"), At(1));
        places.Remember(Comic("troll.cbz"), At(2));

        Assert.Equal(["reading.json"], Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(places.File)!).Select(Path.GetFileName));
    }

    [Fact]
    public void A_comic_is_forgotten_and_the_rest_are_kept()
    {
        var places = Places();
        places.Remember(Comic("a.cbz"), At(1));
        places.Remember(Comic("b.cbz"), At(2));

        Assert.True(places.Forget(Comic("a.cbz")));

        Assert.Null(places.Recall(Comic("a.cbz")));
        Assert.Equal(2, places.Recall(Comic("b.cbz"))?.Page);
        Assert.False(places.Forget(Comic("a.cbz")));
    }

    [Fact]
    public void Every_comic_is_forgotten_and_the_file_is_gone()
    {
        var places = Places();
        places.Remember(Comic("a.cbz"), At(1));
        places.Remember(Comic("b.cbz"), At(2));

        Assert.Equal(2, places.ForgetAll());

        Assert.False(File.Exists(places.File));
        Assert.Null(places.Recall(Comic("b.cbz")));
        Assert.Equal(0, places.ForgetAll());
    }

    // the view put back as it was

    private static readonly Frame Tall = new(1000, 1500, 1000, 1000);

    [Fact]
    public void A_view_is_restored_zoomed_and_scrolled_as_it_was()
    {
        var view = new PageView();
        view.ZoomBy(Tall, 2, 500, 500);
        view.Scroll(Tall, 100);
        var again = new PageView();

        again.Restore(view.Fit, view.Zoom, view.Top, view.Left);

        Assert.Equal(view.Place(Tall), again.Place(Tall));
    }

    [Theory]
    [InlineData((int)Fit.Zoom, 1.0, (int)Fit.Page)]
    [InlineData((int)Fit.Zoom, 0.2, (int)Fit.Page)]
    [InlineData((int)Fit.Zoom, double.NaN, (int)Fit.Page)]
    [InlineData((int)Fit.Width, 3.0, (int)Fit.Width)]
    [InlineData(42, 3.0, (int)Fit.Page)]
    public void A_view_restored_from_what_makes_no_sense_is_fitted_sensibly(int fit, double zoom, int expected)
    {
        var view = new PageView();

        view.Restore((Fit)fit, zoom, down: 7, across: -3);

        Assert.Equal((Fit)expected, view.Fit);
        Assert.Equal(1, view.Zoom);
        Assert.Equal((1.0, 0.0), (view.Top, view.Left));
    }

    // comicraft forget

    private int Forget(params string[] args) => ForgetCommand.Run(args, new Terminal(TextReader.Null, _out, _error, canAsk: false), Places());

    private string Output => _out.ToString().ReplaceLineEndings("\n");

    [Fact]
    public void Forget_forgets_a_comic_even_one_that_is_gone()
    {
        Places().Remember(Comic("gone.cbz"), At(9));

        Assert.Equal(Exit.Done, Forget(Comic("gone.cbz")));

        Assert.Equal("Forgot where you were in gone.cbz; it opens at its first page.\n", Output);
        Assert.Null(Places().Recall(Comic("gone.cbz")));
    }

    [Fact]
    public void Forget_says_so_when_a_comic_was_not_remembered()
    {
        Assert.Equal(Exit.Done, Forget(Comic("new.cbz")));

        Assert.Equal("new.cbz was not remembered; it opens at its first page.\n", Output);
    }

    [Fact]
    public void Forget_all_forgets_every_comic()
    {
        Places().Remember(Comic("a.cbz"), At(1));
        Places().Remember(Comic("b.cbz"), At(2));

        Assert.Equal(Exit.Done, Forget("--all"));

        Assert.Equal("Forgot where you were in 2 comics.\n", Output);
        Assert.False(File.Exists(Places().File));
    }

    [Fact]
    public void Forget_all_with_nothing_remembered_says_so()
    {
        Forget("--all");

        Assert.Equal("No comic was remembered.\n", Output);
    }

    [Theory]
    [InlineData]
    [InlineData("a.cbz", "b.cbz")]
    [InlineData("--all", "a.cbz")]
    [InlineData("--everything")]
    public void Forget_needs_one_comic_or_all(params string[] args)
    {
        Assert.Throws<UsageException>(() => Forget(args));
    }

    [Fact]
    public void Forget_help_says_where_the_places_are_kept()
    {
        Forget("--help");

        Assert.Contains(Places().File, Output);
    }
}
