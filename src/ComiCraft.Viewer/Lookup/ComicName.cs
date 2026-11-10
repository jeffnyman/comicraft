using System.Globalization;
using System.Text.RegularExpressions;

namespace ComiCraft.Viewer.Lookup;

/// <summary>
/// What a comic's file name says it is: its series, its issue number, the year in it, and how many
/// issues the series has, when the name says so. Scanned comics are mostly named alike, as in
/// <c>Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr</c>: the series, the number, then
/// notes in brackets, of which a year is one.
/// </summary>
internal sealed partial record ComicName(string Series, string Number, int? Year, int? Count)
{
    /// <summary>What the file's name says, or null when no issue number can be found in it.</summary>
    public static ComicName? Parse(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');

        int? year = null, count = null;
        foreach (Match note in Note().Matches(name))
        {
            string text = note.Groups[1].Value.Trim();
            if (year is null && YearOnly().Match(text) is { Success: true } y)
            {
                year = int.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if (count is null && OfCount().Match(text) is { Success: true } c)
            {
                count = int.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        // what is left once the notes are gone is the series and then the number: the last number
        // standing alone, with a # before it or not ("Troll 001", "Troll #1", "Troll 1.5")
        string rest = Spaces().Replace(Note().Replace(name, " "), " ").Trim();
        var number = LastNumber().Match(rest);
        if (!number.Success)
        {
            return null;
        }

        string series = rest[..number.Index].TrimEnd(' ', '-', '#', ',').Trim();
        if (series.Length == 0)
        {
            return null;
        }

        return new ComicName(series, number.Groups[1].Value, year, count);
    }

    /// <summary>
    /// An issue number as it is compared: without the zeros it is padded with ("001" is "1"), and in
    /// any case. A number that is not a plain whole number ("1/2", "Annual 1") is kept as it is.
    /// </summary>
    public static string Key(string number)
    {
        string trimmed = number.Trim();
        string digits = trimmed.TrimStart('0');
        bool whole = trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit);
        return whole ? (digits.Length == 0 ? "0" : digits) : trimmed.ToLowerInvariant();
    }

    // a note in round or square brackets: (1984), (of 12), [Digital]
    [GeneratedRegex(@"[\(\[]([^\)\]]*)[\)\]]")]
    private static partial Regex Note();

    [GeneratedRegex(@"^(1[89]\d\d|20\d\d)$")]
    private static partial Regex YearOnly();

    [GeneratedRegex(@"^of\s+(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex OfCount();

    [GeneratedRegex(@"(?:^|\s)#?(\d+(?:\.\d+)?[a-zA-Z]?)(?=\s|$)(?!.*(?:^|\s)#?\d)")]
    private static partial Regex LastNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
