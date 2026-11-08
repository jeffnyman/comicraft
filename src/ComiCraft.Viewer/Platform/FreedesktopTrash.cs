using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace ComiCraft.Viewer.Platform;

/// <summary>
/// The Trash on Linux, as the freedesktop.org Trash Specification (1.0) lays it out, which GNOME,
/// KDE and the other desktops all read: a file can be restored from it by any of them.
/// <para>
/// A trash directory has <c>files</c>, the trashed files, and <c>info</c>, a <c>.trashinfo</c>
/// for each, saying where it was and when it was trashed. The info file is made first, and made
/// only if it is not already there, so two files of the same name never take the same place;
/// then the file is moved in. A file on the same file system as the home is trashed to the home
/// trash, <c>$XDG_DATA_HOME/Trash</c>. One on another (another drive, or, in WSL, a Windows drive
/// under /mnt) is trashed to that file system's own: <c>$topdir/.Trash/$uid</c> where an
/// administrator has made a <c>.Trash</c> that passes the specification's checks, and otherwise
/// <c>$topdir/.Trash-$uid</c>. Where neither can be used, the file is refused, never deleted.
/// </para>
/// </summary>
[UnsupportedOSPlatform("windows")]
internal sealed class FreedesktopTrash : ITrash
{
    private readonly string _homeTrash;
    private readonly Func<string, string?> _mountOf;
    private readonly uint _uid;
    private readonly Func<DateTime> _now;

    /// <param name="homeTrash">The home trash directory.</param>
    /// <param name="mountOf">The top directory of the file system a path is on; null when it is not known.</param>
    /// <param name="uid">The user's numeric identifier.</param>
    /// <param name="now">The local time, for the deletion date.</param>
    internal FreedesktopTrash(string homeTrash, Func<string, string?> mountOf, uint uid, Func<DateTime> now)
    {
        _homeTrash = homeTrash;
        _mountOf = mountOf;
        _uid = uid;
        _now = now;
    }

    /// <summary>
    /// The Trash of the user running ComiCraft, on this system's file systems; or, where the
    /// user's id cannot be read, none, so that what would be deleted is said to be deleted for good.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public static ITrash ForThisUser()
    {
        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) || !Path.IsPathRooted(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        string? mountinfo = ReadProc("/proc/self/mountinfo");
        var mounts = mountinfo is null ? [] : Mounts.Parse(mountinfo);
        if (UidOf(ReadProc("/proc/self/status") ?? "") is not { } uid)
        {
            return new NoTrash();
        }

        return new FreedesktopTrash(Path.Combine(dataHome, "Trash"), path => Mounts.TopOf(path, mounts), uid, () => DateTime.Now);
    }

    /// <summary>
    /// The real user id in a /proc/[pid]/status: the first of the four on its Uid line (real,
    /// effective, saved, file system). Read so, it needs no call into the C library.
    /// </summary>
    internal static uint? UidOf(string status)
    {
        string? line = status.Split('\n').FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal));
        string? first = line?[4..].Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return uint.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out uint uid) ? uid : null;
    }

    private static string? ReadProc(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsAvailable => true;

    public Trashed Move(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            return Trashed.Not($"{full} is not there.");
        }

        string? fileMount = _mountOf(Path.GetDirectoryName(full)!);
        string? homeMount = _mountOf(NearestExisting(_homeTrash));

        // the home trash takes what is on its own file system; and, when the file systems are not
        // known, it is tried, as a move to another file system fails rather than doing harm
        if (fileMount is null || homeMount is null || fileMount == homeMount)
        {
            return MoveInto(_homeTrash, full, original: full);
        }

        foreach (string trash in TopTrashes(fileMount))
        {
            var trashed = MoveInto(trash, full, original: Path.GetRelativePath(fileMount, full));
            if (trashed.Moved)
            {
                return trashed;
            }
        }

        return Trashed.Not($"neither {Path.Combine(fileMount, ".Trash", _uid.ToString(CultureInfo.InvariantCulture))} nor {Path.Combine(fileMount, $".Trash-{_uid}")} could be used.");
    }

    /// <summary>
    /// The trash directories of a file system's top directory, in the order they are to be tried:
    /// the administrator's .Trash/$uid, when .Trash is there, is a directory, is not a symbolic link
    /// and has its sticky bit set; and then .Trash-$uid.
    /// </summary>
    private IEnumerable<string> TopTrashes(string topdir)
    {
        string uid = _uid.ToString(CultureInfo.InvariantCulture);
        var shared = new DirectoryInfo(Path.Combine(topdir, ".Trash"));
        if (shared.Exists && shared.LinkTarget is null && (shared.UnixFileMode & UnixFileMode.StickyBit) != 0)
        {
            yield return Path.Combine(shared.FullName, uid);
        }

        yield return Path.Combine(topdir, $".Trash-{uid}");
    }

    /// <summary>
    /// Trashes the file into a trash directory, made (for the user alone) if it is not there: the
    /// info file first, under the first name not taken, then the file, by that name. Where the
    /// file cannot be moved, its info file is taken away again.
    /// </summary>
    private Trashed MoveInto(string trash, string full, string original)
    {
        string files = Path.Combine(trash, "files");
        string info = Path.Combine(trash, "info");
        try
        {
            const UnixFileMode UserOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(trash, UserOnly);
            Directory.CreateDirectory(files, UserOnly);
            Directory.CreateDirectory(info, UserOnly);

            string record = $"[Trash Info]\nPath={EscapePath(original)}\nDeletionDate={_now().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}\n";
            foreach (string name in Names(Path.GetFileName(full)))
            {
                string infoFile = Path.Combine(info, name + ".trashinfo");
                string trashed = Path.Combine(files, name);
                if (File.Exists(trashed) || Directory.Exists(trashed))
                {
                    continue; // a file trashed without its info file: its name is not to be reused
                }

                try
                {
                    // CreateNew is open with O_EXCL: of two trashing the same name, only one makes it
                    using var stream = new FileStream(infoFile, FileMode.CreateNew, FileAccess.Write);
                    stream.Write(Encoding.UTF8.GetBytes(record));
                }
                catch (IOException) when (File.Exists(infoFile))
                {
                    continue;
                }

                try
                {
                    File.Move(full, trashed);
                    return Trashed.To(trashed);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    File.Delete(infoFile);
                    return Trashed.Not(ex.Message);
                }
            }

            return Trashed.Not($"no name was free in {trash}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Trashed.Not(ex.Message);
        }
    }

    /// <summary>The names to try for a trashed file: its own, and then troll.2.cbr, troll.3.cbr, and so on.</summary>
    internal static IEnumerable<string> Names(string fileName)
    {
        yield return fileName;
        string stem = Path.GetFileNameWithoutExtension(fileName), extension = Path.GetExtension(fileName);
        for (int n = 2; n <= 10_000; n++)
        {
            yield return $"{stem}.{n}{extension}";
        }
    }

    /// <summary>A path as a trash info file holds it: its bytes, with all but the unreserved and / escaped, as in URLs.</summary>
    internal static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    /// <summary>The path, or the nearest folder above it that is there: the home trash may not have been made yet.</summary>
    private static string NearestExisting(string path)
    {
        string? at = path;
        while (at is not null && !Directory.Exists(at))
        {
            at = Path.GetDirectoryName(at);
        }

        return at ?? "/";
    }
}
