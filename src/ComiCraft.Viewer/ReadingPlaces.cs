using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComiCraft.Viewer;

/// <summary>
/// Where a reader was in a comic: its page, counted from zero, and how that page was fitted,
/// zoomed and scrolled (as <see cref="PageView"/> keeps them); and when.
/// </summary>
internal sealed record Place(int Page, Fit Fit, double Zoom, double Down, double Across)
{
    public DateTimeOffset Read { get; init; }
}

/// <summary>
/// Where the reader was in each comic they have read, so that a comic opens where they left it.
/// It is kept in a file of the reader's own (see <see cref="ForThisUser"/>), by each comic's full
/// path, for the <see cref="MostComics"/> read last. The file is read again before each change, and
/// replaced in one step, so two windows open at once each keep their own comic's place. It is only
/// a convenience: a file that cannot be read is as good as none, and is started again.
/// </summary>
internal sealed class ReadingPlaces(string file)
{
    /// <summary>The most comics remembered; past it, those read longest ago are forgotten.</summary>
    public const int MostComics = 1000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    // paths are told apart as the system's own files are: by case on Linux, not on Windows or macOS
    private static readonly StringComparer Paths = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>The file the places are kept in.</summary>
    public string File { get; } = file;

    /// <summary>
    /// This user's places, kept where each system keeps an application's own files: in
    /// %APPDATA%\ComiCraft on Windows, ~/Library/Application Support/ComiCraft on macOS, and,
    /// elsewhere, in $XDG_STATE_HOME/comicraft (~/.local/state/comicraft), as the XDG Base
    /// Directory Specification says state that should outlast a run is kept.
    /// </summary>
    public static ReadingPlaces ForThisUser() => new(Path.Combine(Folder(), "reading.json"));

    private static string Folder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComiCraft");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "ComiCraft");
        }

        // the specification says a relative path in the variable is to be ignored
        string? state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        return Path.Combine(state is { Length: > 0 } && Path.IsPathRooted(state) ? state : Path.Combine(home, ".local", "state"), "comicraft");
    }

    /// <summary>Where the reader was in the comic; null when it is not remembered.</summary>
    public Place? Recall(string comic) => Load().GetValueOrDefault(Key(comic));

    /// <summary>Remembers where the reader is in the comic, forgetting the comics read longest ago when there are too many.</summary>
    public void Remember(string comic, Place place)
    {
        var places = Load();
        places[Key(comic)] = place;
        foreach (var old in places.OrderByDescending(p => p.Value.Read).Skip(MostComics).ToList())
        {
            places.Remove(old.Key);
        }

        Save(places);
    }

    /// <summary>Forgets where the reader was in the comic; returns whether it was remembered.</summary>
    public bool Forget(string comic)
    {
        var places = Load();
        if (!places.Remove(Key(comic)))
        {
            return false;
        }

        Save(places);
        return true;
    }

    /// <summary>
    /// Remembers the place in a comic that has moved under its new path, as when it is renamed,
    /// so that it still opens where it was left; returns whether there was a place to move.
    /// </summary>
    public bool Move(string from, string to)
    {
        var places = Load();
        if (!places.Remove(Key(from), out var place))
        {
            return false;
        }

        places[Key(to)] = place;
        Save(places);
        return true;
    }

    /// <summary>Forgets every comic, removing the file; returns how many were remembered.</summary>
    public int ForgetAll()
    {
        int count = Load().Count;
        if (System.IO.File.Exists(File))
        {
            System.IO.File.Delete(File);
        }

        return count;
    }

    private static string Key(string comic) => Path.GetFullPath(comic);

    /// <summary>What the file holds: a format, so a later one can be told from it, and the places.</summary>
    private sealed record Saved(int Format, Dictionary<string, Place> Comics);

    private Dictionary<string, Place> Load()
    {
        try
        {
            using var stream = System.IO.File.OpenRead(File);
            if (JsonSerializer.Deserialize<Saved>(stream, Json) is { Format: 1, Comics: { } comics })
            {
                return new Dictionary<string, Place>(comics.Where(c => c.Value is not null), Paths);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // none yet, or one that cannot be read: either way, nothing is remembered
        }

        return new Dictionary<string, Place>(Paths);
    }

    private void Save(Dictionary<string, Place> places)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(File))!;
        Directory.CreateDirectory(folder);
        string copy = Path.Combine(folder, $".{Path.GetFileName(File)}.{Guid.NewGuid():N}");
        try
        {
            using (var stream = System.IO.File.Create(copy))
            {
                JsonSerializer.Serialize(stream, new Saved(1, places), Json);
            }

            System.IO.File.Move(copy, File, overwrite: true);
        }
        finally
        {
            System.IO.File.Delete(copy);
        }
    }
}
