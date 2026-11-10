using System.Text;
using System.Text.RegularExpressions;

namespace ComiCraft.Viewer.Lookup;

/// <summary>
/// The name a comic is given from what is known about it, by a pattern such as
/// <c>{year}{month} - {series} {number:00}</c>. The fields are those of its ComicInfo.xml:
/// {series}, {number}, {title}, {year}, {month} (two digits, so 198405 sorts as it should),
/// {day}, {volume}, {publisher}. A number can be padded with zeros, {number:000}. The name is
/// made fit for a file on every system: what Windows does not allow in a name is replaced.
/// </summary>
internal sealed partial class NamePattern
{
    public const string Default = "{year}{month} - {series} {number:00}";

    private static readonly string[] Fields = ["series", "number", "title", "year", "month", "day", "volume", "publisher"];

    private readonly string _pattern;

    private NamePattern(string pattern) => _pattern = pattern;

    public override string ToString() => _pattern;

    /// <summary>The pattern, checked: every field in it must be one there is, and padding is only for numbers.</summary>
    public static NamePattern Parse(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new FormatException("the name pattern is empty.");
        }

        foreach (Match field in Field().Matches(pattern))
        {
            string name = field.Groups[1].Value.ToLowerInvariant();
            if (!Fields.Contains(name))
            {
                throw new FormatException($"the name pattern has {{{field.Groups[1].Value}}}, which is not a field; it can have {string.Join(", ", Fields.Select(f => $"{{{f}}}"))}.");
            }

            if (field.Groups[2].Success && name is not ("number" or "volume" or "day" or "month"))
            {
                throw new FormatException($"only numbers can be padded with zeros, not {{{field.Groups[1].Value}}}.");
            }
        }

        if (Unmatched().IsMatch(Field().Replace(pattern, "")))
        {
            throw new FormatException("the name pattern has a { or } that is not part of a field.");
        }

        return new NamePattern(pattern);
    }

    /// <summary>
    /// The name, without its extension, from the values of the comic's fields (by their ComicInfo
    /// names); null when a field the pattern has is missing, as a comic with no month has no {month}.
    /// </summary>
    public string? Format(Func<string, string?> value)
    {
        bool missing = false;
        string name = Field().Replace(_pattern, field =>
        {
            string key = field.Groups[1].Value.ToLowerInvariant();
            string? text = value(char.ToUpperInvariant(key[0]) + key[1..]);
            if (string.IsNullOrWhiteSpace(text))
            {
                missing = true;
                return "";
            }

            int width = field.Groups[2].Success ? field.Groups[2].Length : key is "month" or "day" ? 2 : 0;
            return Pad(text.Trim(), width);
        });

        return missing ? null : Clean(name);
    }

    /// <summary>A number padded with zeros to the width: 1 is 01, 1.5 is 01.5; what is not a number is left as it is.</summary>
    internal static string Pad(string number, int width)
    {
        int digits = 0;
        while (digits < number.Length && char.IsAsciiDigit(number[digits]))
        {
            digits++;
        }

        if (digits == 0 || width <= digits)
        {
            return number;
        }

        return new string('0', width - digits) + number;
    }

    /// <summary>
    /// A name made fit for a file anywhere: a colon, as in a title, becomes a dash, as it would be
    /// written; a slash becomes a dash; the other characters Windows forbids, and control
    /// characters, go; and the spaces and dots Windows will not end a name with are trimmed.
    /// </summary>
    internal static string Clean(string name)
    {
        var clean = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            switch (c)
            {
                case ':':
                    clean.Append(" -");
                    break;
                case '/' or '\\' or '|':
                    clean.Append('-');
                    break;
                case '"':
                    clean.Append('\'');
                    break;
                case '<' or '>' or '?' or '*':
                    break;
                default:
                    if (!char.IsControl(c))
                    {
                        clean.Append(c);
                    }

                    break;
            }
        }

        return Spaces().Replace(clean.ToString(), " ").Trim().TrimEnd('.', ' ');
    }

    [GeneratedRegex(@"\{([A-Za-z]+)(?::(0+))?\}")]
    private static partial Regex Field();

    [GeneratedRegex(@"[\{\}]")]
    private static partial Regex Unmatched();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
