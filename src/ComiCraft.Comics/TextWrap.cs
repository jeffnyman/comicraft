namespace ComiCraft.Comics;

/// <summary>Breaks text into lines no wider than a number of characters, between words where it can.</summary>
public static class TextWrap
{
    /// <summary>
    /// The lines of the text at the given width. Line breaks in the text are kept; spaces at the
    /// ends of lines are dropped; a word too long for a line is broken across lines.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        var lines = new List<string>();
        foreach (string paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // the word goes on this line, after a space, when it fits
                if (line.Length > 0 && line.Length + 1 + word.Length <= width)
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
                while (rest.Length > width)
                {
                    lines.Add(rest[..width]);
                    rest = rest[width..];
                }

                line.Append(rest);
            }

            lines.Add(line.ToString());
        }

        return lines;
    }
}
