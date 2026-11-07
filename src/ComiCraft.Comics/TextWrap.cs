using System.Globalization;
using System.Text;

namespace ComiCraft.Comics;

/// <summary>
/// Breaks text into lines that fit a width, between words where it can. How wide a piece of text
/// is comes from whoever asks: in pixels of a font for a window, or in characters for a terminal.
/// </summary>
public static class TextWrap
{
    private static readonly char[] Spaces = [' ', '\t'];

    /// <summary>
    /// The lines of the text no wider than <paramref name="width"/>, as <paramref name="measure"/>
    /// measures them. Line breaks in the text are kept; spaces at the ends of lines are dropped; a
    /// word too wide for a line is broken across lines, between characters (never inside one).
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, float width, Func<string, float> measure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        var lines = new List<string>();
        foreach (string paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (string word in paragraph.Split(Spaces, StringSplitOptions.RemoveEmptyEntries))
            {
                // the word goes on this line, after a space, when it fits
                if (line.Length > 0 && measure($"{line} {word}") <= width)
                {
                    line.Append(' ').Append(word);
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                string rest = word;
                while (measure(rest) > width)
                {
                    int fits = FittingLength(rest, width, measure);
                    if (fits == rest.Length)
                    {
                        break; // one character, wider than a line: it has the line to itself
                    }

                    lines.Add(rest[..fits]);
                    rest = rest[fits..];
                }

                line.Append(rest);
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    /// <summary>
    /// How much of the start of the text fits the width, whole characters as a reader sees them
    /// (a letter and its accent, an emoji), and always at least one, so a line is never empty.
    /// </summary>
    private static int FittingLength(string text, float width, Func<string, float> measure)
    {
        int fits = StringInfo.GetNextTextElementLength(text);
        while (fits < text.Length)
        {
            int next = fits + StringInfo.GetNextTextElementLength(text, fits);
            if (measure(text[..next]) > width)
            {
                break;
            }

            fits = next;
        }

        return fits;
    }
}
