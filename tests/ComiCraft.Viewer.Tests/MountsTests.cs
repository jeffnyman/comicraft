using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Tests;

public class MountsTests
{
    // as WSL's /proc/self/mountinfo has them, shortened: the root, the Windows C: drive, and a
    // drive mounted at a path with a space in it
    private const string Mountinfo = """
        70 52 8:48 / / rw,relatime - ext4 /dev/sdd rw,discard,errors=remount-ro
        91 70 0:58 / /mnt/c rw,noatime - 9p C:\134 rw,dirsync,aname=drvfs;path=C:\
        92 70 8:64 / /media/jeff/My\040Comics rw,nosuid - ext4 /dev/sde1 rw
        """;

    private static readonly IReadOnlyList<string> Parsed = Mounts.Parse(Mountinfo);

    [Fact]
    public void Mount_points_are_read_with_their_escapes_undone()
    {
        Assert.Equal(["/", "/mnt/c", "/media/jeff/My Comics"], Parsed);
    }

    [Theory]
    [InlineData("/home/jeff/Comics", "/")]
    [InlineData("/mnt/c/Users/jeff/Comics", "/mnt/c")]
    [InlineData("/mnt/c", "/mnt/c")]
    [InlineData("/mnt/cdrom", "/")]
    [InlineData("/media/jeff/My Comics/Troll", "/media/jeff/My Comics")]
    public void A_path_is_on_the_longest_mount_it_is_under(string path, string top)
    {
        Assert.Equal(top, Mounts.TopOf(path, Parsed));
    }

    [Fact]
    public void With_no_mounts_known_there_is_no_top()
    {
        Assert.Null(Mounts.TopOf("/home/jeff", []));
    }
}
