using System.Globalization;

namespace ComiCraft.Comics;

/// <summary>
/// A field of ComicInfo.xml that can be edited: its element, and what a value of it must be.
/// The fields are those of the ComicInfo schema, version 2.1 (the draft that adds Translator,
/// Tags, StoryArcNumber and GTIN to 2.0), in the order the schema gives them, which is the order
/// they are written in. Pages is in the schema, but describes each page, so is not edited here.
/// </summary>
public sealed class ComicInfoField
{
    private enum Kind { Text, List, Whole, Rating, Choice }

    private readonly Kind _kind;
    private readonly int _min;
    private readonly int _max;
    private readonly string[] _choices;

    private ComicInfoField(string name, Kind kind, int min = 0, int max = int.MaxValue, string[]? choices = null)
    {
        Name = name;
        _kind = kind;
        _min = min;
        _max = max;
        _choices = choices ?? [];
    }

    /// <summary>The element's name, as it is written in ComicInfo.xml.</summary>
    public string Name { get; }

    /// <summary>The values the field can have, when it is one of a set; empty otherwise.</summary>
    public IReadOnlyList<string> Choices => _choices;

    /// <summary>Whether the field holds a list, written with commas between its items.</summary>
    public bool IsList => _kind == Kind.List;

    private static readonly string[] YesNo = ["Unknown", "No", "Yes"];

    private static readonly string[] Manga = ["Unknown", "No", "Yes", "YesAndRightToLeft"];

    private static readonly string[] AgeRatings =
    [
        "Unknown", "Adults Only 18+", "Early Childhood", "Everyone", "Everyone 10+", "G", "Kids to Adults",
        "M", "MA15+", "Mature 17+", "PG", "R18+", "Rating Pending", "Teen", "X18+",
    ];

    /// <summary>Every field, in the schema's order.</summary>
    public static IReadOnlyList<ComicInfoField> All { get; } =
    [
        new("Title", Kind.Text),
        new("Series", Kind.Text),
        new("Number", Kind.Text),
        new("Count", Kind.Whole),
        new("Volume", Kind.Whole),
        new("AlternateSeries", Kind.Text),
        new("AlternateNumber", Kind.Text),
        new("AlternateCount", Kind.Whole),
        new("Summary", Kind.Text),
        new("Notes", Kind.Text),
        new("Year", Kind.Whole, 0, 9999),
        new("Month", Kind.Whole, 1, 12),
        new("Day", Kind.Whole, 1, 31),
        new("Writer", Kind.List),
        new("Penciller", Kind.List),
        new("Inker", Kind.List),
        new("Colorist", Kind.List),
        new("Letterer", Kind.List),
        new("CoverArtist", Kind.List),
        new("Editor", Kind.List),
        new("Translator", Kind.List),
        new("Publisher", Kind.Text),
        new("Imprint", Kind.Text),
        new("Genre", Kind.List),
        new("Tags", Kind.List),
        new("Web", Kind.Text),
        new("PageCount", Kind.Whole),
        new("LanguageISO", Kind.Text),
        new("Format", Kind.Text),
        new("BlackAndWhite", Kind.Choice, choices: YesNo),
        new("Manga", Kind.Choice, choices: Manga),
        new("Characters", Kind.List),
        new("Teams", Kind.List),
        new("Locations", Kind.List),
        new("ScanInformation", Kind.Text),
        new("StoryArc", Kind.Text),
        new("StoryArcNumber", Kind.Text),
        new("SeriesGroup", Kind.Text),
        new("AgeRating", Kind.Choice, choices: AgeRatings),
        new("Pages", Kind.Text), // its place in the order; it cannot be set (see Find)
        new("CommunityRating", Kind.Rating),
        new("MainCharacterOrTeam", Kind.Text),
        new("Review", Kind.Text),
        new("GTIN", Kind.Text),
    ];

    /// <summary>Where an element comes in the schema's order; -1 for an element the schema does not have.</summary>
    internal static int OrderOf(string elementName)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i].Name == elementName)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The field of that name, whatever its case; an error naming the fields when there is none.</summary>
    public static ComicInfoField Find(string name)
    {
        var field = All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            throw new ComicEditException($"ComicInfo.xml has no field '{name}'. Its fields are: {string.Join(", ", All.Where(f => f.Name != "Pages").Select(f => f.Name))}.");
        }

        if (field.Name == "Pages")
        {
            throw new ComicEditException("Pages describes each page of the comic, and is not edited as a field.");
        }

        return field;
    }

    /// <summary>
    /// The value as it is to be written: trimmed; a list with one comma and space between its
    /// items; a number in plain digits; a choice spelled as the schema spells it. Null or blank is
    /// null, which removes the field. A value the field cannot hold is an error saying what it can.
    /// </summary>
    public string? Normalize(string? value)
    {
        string? trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        switch (_kind)
        {
            case Kind.List:
                var items = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return items.Length == 0 ? null : string.Join(", ", items);

            case Kind.Whole:
                if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int whole) || whole < _min || whole > _max)
                {
                    string range = _max == int.MaxValue ? $"of {_min} or more" : $"from {_min} to {_max}";
                    throw new ComicEditException($"{Name} must be a whole number {range}, not '{trimmed}'.");
                }

                return whole.ToString(CultureInfo.InvariantCulture);

            case Kind.Rating:
                if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rating) || rating < 0 || rating > 5)
                {
                    throw new ComicEditException($"{Name} must be a number from 0 to 5, such as 4.5, not '{trimmed}'.");
                }

                return rating.ToString(CultureInfo.InvariantCulture);

            case Kind.Choice:
                return _choices.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ComicEditException($"{Name} must be one of {string.Join(", ", _choices)}; not '{trimmed}'.");

            default:
                return trimmed;
        }
    }
}
