namespace ComiCraft.Viewer.Tests;

/// <summary>The comics in a folder, as the shelf lists them, in a folder of the test's own.</summary>
public sealed class ShelfTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-shelf-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Make(params string[] names)
    {
        foreach (string name in names)
        {
            string path = Path.Combine(_folder, name);
            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "");
            }
        }

        return _folder;
    }

    private static string[] Names(Shelf shelf) => shelf.Entries.Select(e => e.Name).ToArray();

    [Fact]
    public void The_folder_above_then_folders_then_comics_each_in_natural_order()
    {
        Make("Troll 10.cbz", "Troll 2.cbr", "Volume 10/", "Volume 2/", "notes.txt", "cover.jpg", "Sins.PDF", "Old.cb7");

        var shelf = new Shelf(_folder);

        Assert.Equal(["..", "Volume 2", "Volume 10", "Old.cb7", "Sins.PDF", "Troll 2.cbr", "Troll 10.cbz"], Names(shelf));
        Assert.Equal([ShelfKind.Up, ShelfKind.Folder, ShelfKind.Folder, ShelfKind.Comic, ShelfKind.Comic, ShelfKind.Comic, ShelfKind.Comic], shelf.Entries.Select(e => e.Kind));
    }

    [Fact]
    public void Hidden_files_and_folders_are_left_out()
    {
        Make(".hidden.cbz", ".Trash-1000/", "shown.cbz");

        Assert.Equal(["..", "shown.cbz"], Names(new Shelf(_folder)));
    }

    [Fact]
    public void The_first_entry_past_the_folder_above_is_chosen_unless_one_is_named()
    {
        Make("a.cbz", "b.cbz", "c.cbz");

        Assert.Equal("a.cbz", Chosen(new Shelf(_folder)));
        Assert.Equal("b.cbz", Chosen(new Shelf(_folder, Path.Combine(_folder, "b.cbz"))));
        Assert.Equal("a.cbz", Chosen(new Shelf(_folder, Path.Combine(_folder, "gone.cbz"))));
    }

    private static string Chosen(Shelf shelf) => shelf.Entries[shelf.Selected].Name;

    [Fact]
    public void Choosing_a_folder_goes_into_it_and_up_comes_back_to_it()
    {
        Make("Volume 1/a.cbz", "Volume 2/b.cbz");
        var shelf = new Shelf(_folder);
        shelf.Move(1);

        Assert.Null(shelf.Choose());

        Assert.Equal(Path.Combine(_folder, "Volume 2"), shelf.Folder);
        Assert.Equal(["..", "b.cbz"], Names(shelf));
        Assert.True(shelf.Up());
        Assert.Equal(_folder, shelf.Folder);
        Assert.Equal("Volume 2", Chosen(shelf));
    }

    [Fact]
    public void Choosing_the_folder_above_goes_up()
    {
        Make("Volume 1/a.cbz");
        var shelf = new Shelf(Path.Combine(_folder, "Volume 1"));
        shelf.First();

        Assert.Null(shelf.Choose());

        Assert.Equal(_folder, shelf.Folder);
        Assert.Equal("Volume 1", Chosen(shelf));
    }

    [Fact]
    public void Choosing_a_comic_gives_it_to_be_opened()
    {
        Make("a.cbz");
        var shelf = new Shelf(_folder);

        Assert.Equal(new ShelfEntry(Path.Combine(_folder, "a.cbz"), "a.cbz", ShelfKind.Comic), shelf.Choose());
        Assert.Equal(_folder, shelf.Folder);
    }

    [Fact]
    public void At_the_top_of_a_drive_there_is_no_folder_above()
    {
        var shelf = new Shelf(Path.GetPathRoot(_folder)!);

        Assert.DoesNotContain(shelf.Entries, e => e.Kind == ShelfKind.Up);
        Assert.False(shelf.Up());
        Assert.Equal(Path.GetPathRoot(_folder), shelf.Heading);
    }

    [Fact]
    public void Moving_stops_at_either_end()
    {
        Make("a.cbz", "b.cbz");
        var shelf = new Shelf(_folder);

        shelf.Move(10);
        Assert.Equal("b.cbz", Chosen(shelf));
        shelf.Move(-10);
        Assert.Equal("..", Chosen(shelf));
        shelf.Last();
        Assert.Equal("b.cbz", Chosen(shelf));
        Assert.False(shelf.Select(3));
        Assert.True(shelf.Select(1));
        Assert.Equal("a.cbz", Chosen(shelf));
    }

    [Fact]
    public void The_list_scrolls_to_keep_the_chosen_entry_in_sight()
    {
        Make([.. Enumerable.Range(1, 20).Select(i => $"{i}.cbz")]);
        var shelf = new Shelf(_folder);

        shelf.Last();
        shelf.KeepInSight(5);
        Assert.Equal(16, shelf.Top); // 21 entries, the last 5 in sight

        shelf.Move(-7);
        shelf.KeepInSight(5);
        Assert.Equal(13, shelf.Top);

        shelf.First();
        shelf.KeepInSight(5);
        Assert.Equal(0, shelf.Top);
    }

    [Fact]
    public void The_wheel_scrolls_without_choosing_and_stops_at_either_end()
    {
        Make([.. Enumerable.Range(1, 20).Select(i => $"{i}.cbz")]);
        var shelf = new Shelf(_folder);

        shelf.Scroll(100, 5);
        Assert.Equal(16, shelf.Top);
        Assert.Equal("1.cbz", Chosen(shelf));
        shelf.Scroll(-100, 5);
        Assert.Equal(0, shelf.Top);
    }

    [Fact]
    public void Looking_again_finds_what_is_new_and_keeps_the_choice()
    {
        Make("b.cbz", "c.cbz");
        var shelf = new Shelf(_folder, Path.Combine(_folder, "c.cbz"));
        Make("a.cbz");

        shelf.Refresh();

        Assert.Equal(["..", "a.cbz", "b.cbz", "c.cbz"], Names(shelf));
        Assert.Equal("c.cbz", Chosen(shelf));
    }

    [Fact]
    public void An_empty_folder_has_only_the_folder_above()
    {
        var shelf = new Shelf(_folder);

        Assert.Equal([".."], Names(shelf));
        Assert.Equal(0, shelf.Selected);
        Assert.Null(shelf.Problem);
    }

    // each letter one unit wide, the ellipsis too
    private static float Width(string text) => text.Length;

    [Theory]
    [InlineData("Troll 001.cbz", 20, "Troll 001.cbz")]
    [InlineData("Troll 001.cbz", 13, "Troll 001.cbz")]
    [InlineData("Troll 001.cbz", 8, "Troll 0…")]
    [InlineData("Troll 001.cbz", 7, "Troll…")]
    [InlineData("Troll 001.cbz", 1, "…")]
    [InlineData("Troll 001.cbz", 0, "")]
    public void A_name_too_long_is_shortened_to_fit(string name, float width, string shown)
    {
        Assert.Equal(shown, Shelf.Shorten(name, width, Width));
    }
}
