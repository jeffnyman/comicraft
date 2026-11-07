using System.Globalization;
using System.Text;
using ComiCraft.Comics;

namespace ComiCraft.Viewer;

/// <summary>
/// The comic's details, laid out as rows of text for a panel drawn over the page: the heading
/// across the top, then each detail as a label and its value, the value wrapped under itself.
/// The text is for SDL's built-in debug font, which draws only plain ASCII, so it is folded to
/// that first.
/// </summary>
internal static class InfoOverlay
{
    internal enum RowKind { Heading, Blank, Detail }

    /// <summary>A row of the panel; a detail's label is empty on the rows its value wraps onto.</summary>
    internal readonly record struct Row(RowKind Kind, string Label, string Text);

    private const string More = "...";

    /// <summary>
    /// The rows for a panel the given number of characters wide and rows high. When there are
    /// more than fit, the last row that fits says so.
    /// </summary>
    internal static IReadOnlyList<Row> Layout(string heading, IReadOnlyList<Detail> details, int columns, int maxRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);

        var rows = new List<Row>();
        foreach (string line in TextWrap.Wrap(Fold(heading), columns))
        {
            rows.Add(new Row(RowKind.Heading, "", line));
        }

        rows.Add(new Row(RowKind.Blank, "", ""));

        int valueWidth = Math.Max(1, columns - ValueColumn(details, columns));
        foreach (var detail in details)
        {
            string label = Fold(detail.Label);
            var lines = TextWrap.Wrap(Fold(detail.Value), valueWidth);
            for (int i = 0; i < lines.Count; i++)
            {
                rows.Add(new Row(RowKind.Detail, i == 0 ? label : "", lines[i]));
            }
        }

        if (rows.Count > maxRows)
        {
            rows.RemoveRange(maxRows - 1, rows.Count - maxRows + 1);
            rows.Add(new Row(RowKind.Detail, "", More));
        }

        return rows;
    }

    /// <summary>
    /// Where a detail's value starts, in characters from the left of the panel: after the longest
    /// label, but never so far that a narrow panel leaves the values less than a third of it.
    /// </summary>
    internal static int ValueColumn(IReadOnlyList<Detail> details, int columns) =>
        Math.Min(details.Count == 0 ? 0 : details.Max(d => d.Label.Length) + 2, columns * 2 / 3);

    /// <summary>
    /// The text in plain printable ASCII, which is all the debug font draws: typographic dashes,
    /// quotes and ellipses become their plain forms, accents are dropped, and whatever is left
    /// over becomes "?". Line breaks are kept.
    /// </summary>
    internal static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            switch (c)
            {
                case '\n':
                case >= ' ' and <= '~':
                    folded.Append(c);
                    break;
                case '\r':
                    break;
                case '\t' or ' ':
                    folded.Append(' ');
                    break;
                case '‐' or '‑' or '‒' or '–' or '—' or '―' or '−':
                    folded.Append('-');
                    break;
                case '‘' or '’' or '‚' or '′':
                    folded.Append('\'');
                    break;
                case '“' or '”' or '„' or '″':
                    folded.Append('"');
                    break;
                case '…':
                    folded.Append("...");
                    break;
                case '•':
                    folded.Append('*');
                    break;
                case 'ß':
                    folded.Append("ss");
                    break;
                case var mark when CharUnicodeInfo.GetUnicodeCategory(mark) == UnicodeCategory.NonSpacingMark:
                    break; // the accent of a letter, now apart from it
                default:
                    folded.Append('?');
                    break;
            }
        }

        return folded.ToString();
    }
}
