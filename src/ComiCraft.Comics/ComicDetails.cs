using System.Globalization;

namespace ComiCraft.Comics;

/// <summary>One thing known about a comic, as a reader would see it: "Writer", "Brian Witten".</summary>
public readonly record struct Detail(string Label, string Value);

/// <summary>
/// What there is to say about a comic, in the order a reader would want it: who published it and
/// when, who made it, who is in it, what it is about, and then the comic itself. Only what is
/// known is listed; a comic without a ComicInfo.xml still has its pages and its file.
/// </summary>
public static class ComicDetails
{
    public static IReadOnlyList<Detail> Of(ComicBook comic)
    {
        var details = new List<Detail>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                details.Add(new Detail(label, value));
            }
        }

        // "Writer: Brian Witten", "Writers: Brian Witten, Larry Hama"
        void AddAll(string label, IReadOnlyList<string> values) =>
            Add(values.Count == 1 ? label : label + "s", string.Join(", ", values));

        if (comic.Info is { } info)
        {
            Add("Publisher", info.Publisher);
            Add("Imprint", info.Imprint);
            Add("Published", Date(info.Year, info.Month, info.Day));
            Add("Volume", info.Volume?.ToString(CultureInfo.InvariantCulture));
            if (info.Count is { } count)
            {
                Add("Issue", info.Number is null ? $"{count} issues" : $"{info.Number} of {count}");
            }

            Add("Story arc", info.StoryArc);
            AddAll("Writer", info.Writers);
            AddAll("Penciller", info.Pencillers);
            AddAll("Inker", info.Inkers);
            AddAll("Colorist", info.Colorists);
            AddAll("Letterer", info.Letterers);
            AddAll("Cover artist", info.CoverArtists);
            AddAll("Editor", info.Editors);
            AddAll("Character", info.Characters);
            AddAll("Team", info.Teams);
            AddAll("Location", info.Locations);
            AddAll("Genre", info.Genres);
            Add("Summary", info.Summary);
            Add("Web", info.Web);
        }

        Add("Pages", comic.PageCount.ToString(CultureInfo.InvariantCulture));
        Add("File", comic.Name);
        return details;
    }

    /// <summary>"7 July 1995", "July 1995" or "1995", as much as is known; null without a year.</summary>
    internal static string? Date(int? year, int? month, int? day)
    {
        if (year is null)
        {
            return null;
        }

        if (month is not (>= 1 and <= 12))
        {
            return year.Value.ToString(CultureInfo.InvariantCulture);
        }

        string monthYear = $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month.Value)} {year}";
        return day is >= 1 and <= 31 ? $"{day} {monthYear}" : monthYear;
    }
}
