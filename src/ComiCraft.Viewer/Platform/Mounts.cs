using System.Text;

namespace ComiCraft.Viewer.Platform;

/// <summary>
/// Where file systems are mounted, from Linux's /proc/self/mountinfo: what the Trash needs to know
/// whether a file is on the home's file system, or on another with a trash of its own.
/// </summary>
internal static class Mounts
{
    /// <summary>
    /// The mount points in a mountinfo table. Each line is one mount; its fifth field is where it
    /// is mounted, with a space, a tab, a line break and a backslash written in octal (\040).
    /// </summary>
    internal static IReadOnlyList<string> Parse(string mountinfo) =>
        mountinfo.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length > 4)
            .Select(fields => Unescape(fields[4]))
            .ToList();

    /// <summary>The top directory of the file system the path is on: the longest mount point it is under.</summary>
    internal static string? TopOf(string path, IReadOnlyList<string> mounts)
    {
        string? top = null;
        foreach (string mount in mounts)
        {
            bool under = mount == "/" || path == mount || path.StartsWith(mount + "/", StringComparison.Ordinal);
            if (under && (top is null || mount.Length > top.Length))
            {
                top = mount;
            }
        }

        return top;
    }

    /// <summary>A mount point with its octal escapes (\040 and the like) turned back into the bytes they stand for.</summary>
    private static string Unescape(string field)
    {
        if (!field.Contains('\\'))
        {
            return field;
        }

        var bytes = new List<byte>();
        for (int i = 0; i < field.Length; i++)
        {
            bool escaped = field[i] == '\\' && i + 3 < field.Length
                && field[(i + 1)..(i + 4)].All(c => c is >= '0' and <= '7');
            if (escaped)
            {
                bytes.Add(Convert.ToByte(field[(i + 1)..(i + 4)], 8));
                i += 3;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(field[i].ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
