using System.Runtime.Versioning;
using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Tests;

/// <summary>
/// The freedesktop.org Trash, on folders of the test's own: a "home" with its trash, and a "drive",
/// a file system of its own with its own top directory. It is ordinary file handling, so it is
/// tested wherever Unix permissions are, which is everywhere but Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class FreedesktopTrashTests : IDisposable
{
    private const uint Uid = 1000;
    private static readonly DateTime Now = new(2026, 11, 8, 9, 30, 0);

    private readonly string _root = Directory.CreateTempSubdirectory("comicraft-freedesktop-").FullName;

    private string Home => Path.Combine(_root, "home");

    private string Drive => Path.Combine(_root, "drive");

    private string HomeTrash => Path.Combine(Home, ".local", "share", "Trash");

    public FreedesktopTrashTests()
    {
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Drive);
    }

    public void Dispose()
    {
        // what a test made read-only is made writable again, to be cleaned away
        foreach (string dir in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Prepend(_root))
        {
            if (new DirectoryInfo(dir).LinkTarget is null)
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_root, recursive: true);
    }

    /// <summary>The trash, with the home and the drive as the two file systems.</summary>
    private FreedesktopTrash Trash() =>
        new(HomeTrash, path => path.StartsWith(Drive, StringComparison.Ordinal) ? Drive : Home, Uid, () => Now);

    private static string Comic(string folder, string name = "troll.cbr")
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "a comic");
        return path;
    }

    private static bool Skip => OperatingSystem.IsWindows();

    [Fact]
    public void A_file_in_the_home_goes_to_the_home_trash_with_its_info()
    {
        if (Skip) return;
        string path = Comic(Path.Combine(Home, "Comics"), "Troll 001 (1993).cbr");

        var trashed = Trash().Move(path);

        Assert.True(trashed.Moved, trashed.Failure);
        Assert.False(File.Exists(path));
        Assert.Equal(Path.Combine(HomeTrash, "files", "Troll 001 (1993).cbr"), trashed.Into);
        Assert.Equal("a comic", File.ReadAllText(trashed.Into!));
        Assert.Equal(
            $"[Trash Info]\nPath={FreedesktopTrash.EscapePath(path)}\nDeletionDate=2026-11-08T09:30:00\n",
            File.ReadAllText(Path.Combine(HomeTrash, "info", "Troll 001 (1993).cbr.trashinfo")));
    }

    [Fact]
    public void The_trash_is_made_for_the_user_alone()
    {
        if (Skip) return;
        Trash().Move(Comic(Home));

        const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(UserOnly, File.GetUnixFileMode(HomeTrash));
        Assert.Equal(UserOnly, File.GetUnixFileMode(Path.Combine(HomeTrash, "files")));
        Assert.Equal(UserOnly, File.GetUnixFileMode(Path.Combine(HomeTrash, "info")));
    }

    [Fact]
    public void A_second_file_of_the_same_name_takes_the_next_name()
    {
        if (Skip) return;
        Trash().Move(Comic(Home));
        var second = Trash().Move(Comic(Home));

        Assert.Equal(Path.Combine(HomeTrash, "files", "troll.2.cbr"), second.Into);
        Assert.True(File.Exists(Path.Combine(HomeTrash, "files", "troll.cbr")));
        Assert.True(File.Exists(Path.Combine(HomeTrash, "info", "troll.2.cbr.trashinfo")));
    }

    [Fact]
    public void A_name_held_by_a_file_without_info_is_not_taken()
    {
        if (Skip) return;
        Comic(Path.Combine(HomeTrash, "files")); // a trashed troll.cbr whose info file was lost

        var trashed = Trash().Move(Comic(Home));

        Assert.Equal(Path.Combine(HomeTrash, "files", "troll.2.cbr"), trashed.Into);
        Assert.False(File.Exists(Path.Combine(HomeTrash, "info", "troll.cbr.trashinfo")));
    }

    [Fact]
    public void A_file_on_another_drive_goes_to_that_drives_own_trash()
    {
        if (Skip) return;
        string path = Comic(Path.Combine(Drive, "Comics"));

        var trashed = Trash().Move(path);

        string trash = Path.Combine(Drive, ".Trash-1000");
        Assert.Equal(Path.Combine(trash, "files", "troll.cbr"), trashed.Into);
        Assert.Contains("Path=Comics/troll.cbr\n", File.ReadAllText(Path.Combine(trash, "info", "troll.cbr.trashinfo")));
        Assert.False(Directory.Exists(HomeTrash));
    }

    [Fact]
    public void An_administrators_trash_with_its_sticky_bit_is_used_first()
    {
        if (Skip) return;
        string shared = Path.Combine(Drive, ".Trash");
        Directory.CreateDirectory(shared);
        File.SetUnixFileMode(shared, (UnixFileMode)Convert.ToInt32("1777", 8));

        var trashed = Trash().Move(Comic(Drive));

        Assert.Equal(Path.Combine(shared, "1000", "files", "troll.cbr"), trashed.Into);
    }

    [Fact]
    public void An_administrators_trash_without_its_sticky_bit_is_passed_over()
    {
        if (Skip) return;
        Directory.CreateDirectory(Path.Combine(Drive, ".Trash"));
        File.SetUnixFileMode(Path.Combine(Drive, ".Trash"), (UnixFileMode)Convert.ToInt32("0777", 8));

        var trashed = Trash().Move(Comic(Drive));

        Assert.Equal(Path.Combine(Drive, ".Trash-1000", "files", "troll.cbr"), trashed.Into);
        Assert.False(Directory.Exists(Path.Combine(Drive, ".Trash", "1000")));
    }

    [Fact]
    public void An_administrators_trash_that_is_a_link_is_passed_over()
    {
        if (Skip) return;
        string elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.SetUnixFileMode(elsewhere, (UnixFileMode)Convert.ToInt32("1777", 8));
        Directory.CreateSymbolicLink(Path.Combine(Drive, ".Trash"), elsewhere);

        var trashed = Trash().Move(Comic(Drive));

        Assert.Equal(Path.Combine(Drive, ".Trash-1000", "files", "troll.cbr"), trashed.Into);
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    [Fact]
    public void A_drive_without_a_usable_trash_refuses_the_file_and_leaves_it()
    {
        if (Skip) return;
        string path = Comic(Path.Combine(Drive, "Comics"));
        File.SetUnixFileMode(Drive, UnixFileMode.UserRead | UnixFileMode.UserExecute); // no trash can be made

        var trashed = Trash().Move(path);

        Assert.False(trashed.Moved);
        Assert.Contains(".Trash-1000", trashed.Failure);
        Assert.Equal("a comic", File.ReadAllText(path));
    }

    [Fact]
    public void A_file_that_cannot_be_moved_is_left_with_no_info_for_it()
    {
        if (Skip) return;
        string folder = Path.Combine(Home, "Locked");
        string path = Comic(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute); // it cannot be taken out

        var trashed = Trash().Move(path);

        Assert.False(trashed.Moved);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(HomeTrash, "info")));
    }

    [Fact]
    public void A_file_that_is_not_there_is_not_trashed()
    {
        if (Skip) return;
        Assert.False(Trash().Move(Path.Combine(Home, "nowhere.cbr")).Moved);
    }

    [Theory]
    [InlineData("/home/jeff/Comics/troll.cbr", "/home/jeff/Comics/troll.cbr")]
    [InlineData("/home/jeff/Troll 001 (1993).cbr", "/home/jeff/Troll%20001%20%281993%29.cbr")]
    [InlineData("Comics/Ça va.cbz", "Comics/%C3%87a%20va.cbz")]
    public void Paths_are_escaped_as_in_urls(string path, string escaped)
    {
        Assert.Equal(escaped, FreedesktopTrash.EscapePath(path));
    }

    [Fact]
    public void Names_after_the_first_are_numbered_before_the_extension()
    {
        Assert.Equal(["troll.cbr", "troll.2.cbr", "troll.3.cbr"], FreedesktopTrash.Names("troll.cbr").Take(3));
    }

    [Fact]
    public void The_user_id_is_the_real_one_on_the_uid_line()
    {
        const string Status = "Name:\tcomicraft\nUmask:\t0022\nState:\tR (running)\nUid:\t1000\t1001\t1002\t1003\nGid:\t1000\t1000\t1000\t1000\n";

        Assert.Equal(1000u, FreedesktopTrash.UidOf(Status));
        Assert.Null(FreedesktopTrash.UidOf("Name:\tcomicraft\n"));
    }
}
