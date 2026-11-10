using System.Globalization;

namespace ComiCraft.Viewer.Lookup;

/// <summary>
/// What Metron says of an issue, as the fields of a ComicInfo.xml. Only fields Metron has a value
/// for are given, so a lookup never takes away what a comic says that Metron does not know.
/// </summary>
internal static class IssueFields
{
    /// <summary>
    /// What a Notes field set by a lookup starts with: Metron's data is shared under CC BY-SA, which
    /// asks that where it came from is said. A comic's own notes are not written over by it.
    /// </summary>
    public const string NotesStart = "Information from Metron";

    /// <summary>Metron's roles, by the ComicInfo field each is credited in; a role may be in more than one.</summary>
    private static readonly (string Field, string[] Roles)[] Credits =
    [
        ("Writer", ["Writer", "Script", "Scripter", "Story", "Plot", "Plotter", "Co-Writer"]),
        ("Penciller", ["Penciller", "Pencils", "Artist", "Breakdowns", "Layouts"]),
        ("Inker", ["Inker", "Inks", "Artist", "Finishes", "Embellisher"]),
        ("Colorist", ["Colorist", "Colors", "Color Separations"]),
        ("Letterer", ["Letterer", "Letters"]),
        ("CoverArtist", ["Cover"]),
        ("Editor", ["Editor", "Editor In Chief", "Consulting Editor"]),
        ("Translator", ["Translator"]),
    ];

    /// <summary>Metron's ratings, by the AgeRating ComicInfo has for each; one with none is left out.</summary>
    private static readonly Dictionary<string, string> Ratings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Everyone"] = "Everyone",
        ["Teen"] = "Teen",
        ["Teen Plus"] = "Teen",
        ["Mature"] = "Mature 17+",
        ["Explicit"] = "Adults Only 18+",
    };

    /// <summary>
    /// The fields, in ComicInfo's names, with their values. The series' issue count, which an issue
    /// does not give, comes from the series, and only when the series has ended, since the count of
    /// one still running is only the count so far.
    /// </summary>
    public static List<(string Field, string Value)> Of(MetronIssue issue, MetronSeries? series)
    {
        var fields = new List<(string, string)>();
        void Add(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields.Add((field, value.Trim()));
            }
        }

        string? Join(IEnumerable<string>? items) =>
            items?.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct().ToList() is { Count: > 0 } list
                ? string.Join(", ", list)
                : null;

        Add("Title", issue.Name is { Count: > 0 } stories ? string.Join("; ", stories.Where(s => !string.IsNullOrWhiteSpace(s))) : issue.Title);
        Add("Series", issue.Series.Name);
        Add("Number", issue.Number);
        if (series is { YearEnd: not null, IssueCount: > 0 and var count })
        {
            Add("Count", count.ToString(CultureInfo.InvariantCulture));
        }

        Add("Volume", issue.Series.Volume?.ToString(CultureInfo.InvariantCulture));
        Add("Summary", issue.Desc);

        // a cover gives a month, and Metron writes it as its first day; the day is not the cover's
        if (DateOnly.TryParseExact(issue.CoverDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cover))
        {
            Add("Year", cover.Year.ToString(CultureInfo.InvariantCulture));
            Add("Month", cover.Month.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var (field, roles) in Credits)
        {
            Add(field, Join(issue.Credits?
                .Where(c => c.Role?.Any(r => roles.Contains(r.Name.Trim(), StringComparer.OrdinalIgnoreCase)) == true)
                .Select(c => c.Creator)));
        }

        Add("Publisher", issue.Publisher?.Name);
        Add("Imprint", issue.Imprint?.Name);
        Add("Genre", Join(issue.Series.Genres?.Select(g => g.Name)));
        Add("Web", issue.ResourceUrl);
        Add("LanguageISO", issue.Series.Language);
        Add("Characters", Join(issue.Characters?.Select(c => c.Name)));
        Add("Teams", Join(issue.Teams?.Select(t => t.Name)));
        Add("StoryArc", Join(issue.Arcs?.Select(a => a.Name)));
        if (issue.Rating?.Name is { } rating && Ratings.TryGetValue(rating.Trim(), out string? ageRating))
        {
            Add("AgeRating", ageRating);
        }

        Add("GTIN", string.IsNullOrWhiteSpace(issue.Isbn) ? issue.Upc : issue.Isbn);
        Add("Notes", $"{NotesStart} ({Metron.Site}), issue {issue.Id.ToString(CultureInfo.InvariantCulture)}, shared under CC BY-SA 4.0.");
        return fields;
    }
}
