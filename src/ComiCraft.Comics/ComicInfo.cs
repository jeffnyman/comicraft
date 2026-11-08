using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace ComiCraft.Comics;

/// <summary>
/// What a comic's ComicInfo.xml says about it: the metadata format begun by ComicRack and kept
/// now by the Anansi Project. Every field is optional, and scrapers fill in some and not others,
/// so each is null (or an empty list) when it is missing or its value cannot be read.
/// </summary>
public sealed record ComicInfo
{
    public const string FileName = "ComicInfo.xml";

    public string? Title { get; init; }

    public string? Series { get; init; }

    /// <summary>The issue number, as text: issues can be numbered "1.5", "½" or "Annual 1".</summary>
    public string? Number { get; init; }

    /// <summary>The volume; ComicVine and the taggers that use it give the year the series began.</summary>
    public int? Volume { get; init; }

    /// <summary>How many issues the series has.</summary>
    public int? Count { get; init; }

    public string? StoryArc { get; init; }

    public int? Year { get; init; }

    public int? Month { get; init; }

    public int? Day { get; init; }

    public IReadOnlyList<string> Writers { get; init; } = [];

    public IReadOnlyList<string> Pencillers { get; init; } = [];

    public IReadOnlyList<string> Inkers { get; init; } = [];

    public IReadOnlyList<string> Colorists { get; init; } = [];

    public IReadOnlyList<string> Letterers { get; init; } = [];

    public IReadOnlyList<string> CoverArtists { get; init; } = [];

    public IReadOnlyList<string> Editors { get; init; } = [];

    public string? Publisher { get; init; }

    public string? Imprint { get; init; }

    public string? Web { get; init; }

    public string? Summary { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Characters { get; init; } = [];

    public IReadOnlyList<string> Teams { get; init; } = [];

    public IReadOnlyList<string> Locations { get; init; } = [];

    /// <summary>How many pages the comic has, by the tagger's count.</summary>
    public int? PageCount { get; init; }

    /// <summary>
    /// A one-line name for the comic, such as "Operation: Knightstrike #3 – Enter... Bloodstrike
    /// (July 1995)": as much of the series, number, title and date as is known. Null when there
    /// is neither a series nor a title to name it by.
    /// </summary>
    public string? Heading
    {
        get
        {
            string? name = Series is null
                ? Title
                : (Number is null ? Series : $"{Series} #{Number}") + (Title is null ? "" : $" – {Title}");
            if (name is null)
            {
                return null;
            }

            string? date = (Month, Year) switch
            {
                ( >= 1 and <= 12, { } year) => $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month.Value)} {year}",
                (_, { } year) => year.ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
            return date is null ? name : $"{name} ({date})";
        }
    }

    /// <summary>Reads a ComicInfo.xml; null when it is not well-formed XML or not a ComicInfo document.</summary>
    public static ComicInfo? Parse(Stream stream)
    {
        XElement root;
        try
        {
            using var reader = XmlReader.Create(stream, ReaderSettings);
            root = XElement.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        return root.Name.LocalName == RootName ? FromElement(root) : null;
    }

    /// <summary>
    /// How ComicInfo.xml is read: a DOCTYPE is ignored, never fetched, since the file comes from
    /// whoever made the archive; and the spacing between elements is let go, so that a document
    /// written out again is laid out afresh, its new elements on lines of their own like the rest.
    /// </summary>
    internal static XmlReaderSettings ReaderSettings => new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreWhitespace = true };

    /// <summary>The name of a ComicInfo document's root element.</summary>
    internal const string RootName = "ComicInfo";

    /// <summary>What a ComicInfo element says; its unreadable values are missing, as in <see cref="Parse"/>.</summary>
    internal static ComicInfo FromElement(XElement root)
    {
        string? Text(string name)
        {
            string? value = root.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        int? Number(string name) =>
            int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

        IReadOnlyList<string> List(string name) =>
            Text(name)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

        return new ComicInfo
        {
            Title = Text("Title"),
            Series = Text("Series"),
            Number = Text("Number"),
            Volume = Number("Volume"),
            Count = Number("Count"),
            StoryArc = Text("StoryArc"),
            Year = Number("Year"),
            Month = Number("Month"),
            Day = Number("Day"),
            Writers = List("Writer"),
            Pencillers = List("Penciller"),
            Inkers = List("Inker"),
            Colorists = List("Colorist"),
            Letterers = List("Letterer"),
            CoverArtists = List("CoverArtist"),
            Editors = List("Editor"),
            Publisher = Text("Publisher"),
            Imprint = Text("Imprint"),
            Web = Text("Web"),
            Summary = Text("Summary"),
            Notes = Text("Notes"),
            Genres = List("Genre"),
            Characters = List("Characters"),
            Teams = List("Teams"),
            Locations = List("Locations"),
            PageCount = Number("PageCount"),
        };
    }

    /// <summary>
    /// Whether an archive entry is the comic's ComicInfo.xml. It belongs at the root of the
    /// archive, but the case of its name varies, so that is not held against it.
    /// </summary>
    internal static bool IsComicInfo(string entryName) =>
        string.Equals(entryName.Replace('\\', '/'), FileName, StringComparison.OrdinalIgnoreCase);
}
