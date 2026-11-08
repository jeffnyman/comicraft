using ComiCraft.Comics;

namespace ComiCraft.Viewer;

/// <summary>What an entry on the shelf is: the folder above, a folder in this one, or a comic.</summary>
internal enum ShelfKind { Up, Folder, Comic }

/// <summary>An entry on the shelf: where it is, and the name it is shown by.</summary>
internal sealed record ShelfEntry(string Path, string Name, ShelfKind Kind);

/// <summary>
/// The comics in a folder, to choose one to read, as a list beside the page: the folder above,
/// then the folders in it, then its comics (CBZ, CBR, CB7 and PDF), each in the natural order a
/// reader expects, so that issue 2 comes before issue 10. Hidden files and folders are left out.
/// One entry is chosen, by the keys or the mouse; choosing a folder goes into it, and choosing a
/// comic is the window's to open. The list scrolls to keep the chosen entry in sight.
/// </summary>
internal sealed class Shelf
{
    /// <summary>The extensions of the comics the shelf shows.</summary>
    public static readonly string[] ComicExtensions = [".cbz", ".cbr", ".cb7", ".pdf"];

    private static readonly EnumerationOptions Listing = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    private List<ShelfEntry> _entries = [];

    /// <summary>The shelf of the folder, with the entry at <paramref name="select"/> chosen, when it is there.</summary>
    public Shelf(string folder, string? select = null) => Open(folder, select);

    /// <summary>The folder shown, as a full path.</summary>
    public string Folder { get; private set; } = "";

    /// <summary>What is in the folder, the folder above first.</summary>
    public IReadOnlyList<ShelfEntry> Entries => _entries;

    /// <summary>The entry chosen, by its place in <see cref="Entries"/>; -1 when there are none.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>The first entry in sight.</summary>
    public int Top { get; private set; }

    /// <summary>Why the folder's contents cannot be shown, when they cannot.</summary>
    public string? Problem { get; private set; }

    /// <summary>The folder's name, as a heading: the folder's own, or the path of a drive's top.</summary>
    public string Heading => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Folder)) is { Length: > 0 } name ? name : Folder;

    /// <summary>Whether the path is a comic the shelf would show, by its extension.</summary>
    public static bool IsComic(string path) => ComicExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Shows the folder, with the entry at <paramref name="select"/> chosen; failing that, the first
    /// in it, past the folder above.
    /// </summary>
    public void Open(string folder, string? select = null)
    {
        Folder = System.IO.Path.GetFullPath(folder);
        Problem = null;
        _entries = [];
        if (System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(Folder)) is { } above)
        {
            _entries.Add(new ShelfEntry(above, "..", ShelfKind.Up));
        }

        try
        {
            var folders = Directory.EnumerateDirectories(Folder, "*", Listing)
                .Where(Shown)
                .Select(path => new ShelfEntry(path, System.IO.Path.GetFileName(path), ShelfKind.Folder));
            var comics = Directory.EnumerateFiles(Folder, "*", Listing)
                .Where(path => Shown(path) && IsComic(path))
                .Select(path => new ShelfEntry(path, System.IO.Path.GetFileName(path), ShelfKind.Comic));
            _entries.AddRange(folders.OrderBy(e => e.Name, NaturalOrder.Instance));
            _entries.AddRange(comics.OrderBy(e => e.Name, NaturalOrder.Instance));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem = $"{Heading} cannot be looked in: {ex.Message}";
        }

        Top = 0;
        int chosen = select is null ? -1 : IndexOf(select);
        Selected = chosen >= 0 ? chosen : Math.Min(_entries.Count - 1, _entries is [{ Kind: ShelfKind.Up }, ..] ? 1 : 0);
    }

    /// <summary>Looks in the folder again, as it may have changed, keeping the chosen entry chosen when it is still there.</summary>
    public void Refresh()
    {
        string? chosen = Selected >= 0 ? _entries[Selected].Path : null;
        int top = Top;
        Open(Folder, chosen);
        Top = Math.Clamp(top, 0, Math.Max(0, _entries.Count - 1));
    }

    /// <summary>Chooses the entry so many places on, or back when it is negative, stopping at either end.</summary>
    public void Move(int by)
    {
        if (_entries.Count > 0)
        {
            Selected = Math.Clamp(Selected + by, 0, _entries.Count - 1);
        }
    }

    public void First() => Move(-_entries.Count);

    public void Last() => Move(_entries.Count);

    /// <summary>Chooses the entry at the place in the list, when there is one there.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return false;
        }

        Selected = index;
        return true;
    }

    /// <summary>Scrolls the list so that the chosen entry is among the <paramref name="rows"/> in sight.</summary>
    public void KeepInSight(int rows)
    {
        rows = Math.Max(1, rows);
        if (Selected < Top)
        {
            Top = Math.Max(0, Selected);
        }
        else if (Selected >= Top + rows)
        {
            Top = Selected - rows + 1;
        }

        Top = Math.Clamp(Top, 0, Math.Max(0, _entries.Count - rows));
    }

    /// <summary>Scrolls the list by so many rows, as the wheel does, without changing what is chosen.</summary>
    public void Scroll(int by, int rows) => Top = Math.Clamp(Top + by, 0, Math.Max(0, _entries.Count - Math.Max(1, rows)));

    /// <summary>
    /// Acts on the chosen entry: a folder, or the folder above, is gone into, and null returned;
    /// a comic is returned, for the window to open.
    /// </summary>
    public ShelfEntry? Choose()
    {
        if (Selected < 0)
        {
            return null;
        }

        var entry = _entries[Selected];
        switch (entry.Kind)
        {
            case ShelfKind.Up:
                Up();
                return null;
            case ShelfKind.Folder:
                Open(entry.Path);
                return null;
            default:
                return entry;
        }
    }

    /// <summary>Goes to the folder above, with the folder it came from chosen; returns false at a drive's top.</summary>
    public bool Up()
    {
        if (_entries is not [{ Kind: ShelfKind.Up } up, ..])
        {
            return false;
        }

        Open(up.Path, Folder);
        return true;
    }

    /// <summary>
    /// The text, shortened with an ellipsis at its end to fit the width, as <paramref name="measure"/>
    /// measures it; the whole of it when it fits, and nothing when not even the ellipsis does.
    /// </summary>
    public static string Shorten(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width)
        {
            return text;
        }

        // the longest start of the text that fits with the ellipsis after it
        int low = 0, high = text.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (measure(text[..middle].TrimEnd() + "…") <= width)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        string shortened = text[..low].TrimEnd() + "…";
        return measure(shortened) <= width ? shortened : "";
    }

    private int IndexOf(string path)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        return _entries.FindIndex(e => e.Kind != ShelfKind.Up && string.Equals(e.Path, full, comparison));
    }

    // a dot at the start hides a name on Linux and macOS as the attribute does on Windows
    private static bool Shown(string path) => !System.IO.Path.GetFileName(path).StartsWith('.');
}
