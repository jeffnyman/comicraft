using ComiCraft.Comics;

namespace ComiCraft.Viewer;

/// <summary>
/// The comic's details, laid out as rows of text for a panel drawn over the page: the heading
/// across the top, then each detail as a label and its value, the value wrapped under itself.
/// Text is measured by the fonts it will be drawn in, given as functions, so the layout does
/// not depend on how it is drawn.
/// </summary>
internal static class InfoOverlay
{
    internal enum RowKind { Heading, Blank, Detail }

    /// <summary>A row of the panel; a detail's label is empty on the rows its value wraps onto.</summary>
    internal readonly record struct Row(RowKind Kind, string Label, string Text);

    private const string More = "…";

    /// <summary>The space between the longest label and the values.</summary>
    private const string Gap = "MM";

    /// <summary>
    /// The rows for a panel <paramref name="width"/> wide and <paramref name="maxRows"/> rows
    /// high: the heading measured by <paramref name="measureHeading"/>, the details by
    /// <paramref name="measure"/>. When there are more than fit, the last row that fits says so.
    /// </summary>
    internal static IReadOnlyList<Row> Layout(
        string heading,
        IReadOnlyList<Detail> details,
        float width,
        int maxRows,
        Func<string, float> measure,
        Func<string, float> measureHeading)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);

        var rows = new List<Row>();
        foreach (string line in TextWrap.Wrap(heading, width, measureHeading))
        {
            rows.Add(new Row(RowKind.Heading, "", line));
        }

        rows.Add(new Row(RowKind.Blank, "", ""));

        float valueWidth = Math.Max(1, width - ValueColumn(details, width, measure));
        foreach (var detail in details)
        {
            var lines = TextWrap.Wrap(detail.Value, valueWidth, measure);
            for (int i = 0; i < lines.Count; i++)
            {
                rows.Add(new Row(RowKind.Detail, i == 0 ? detail.Label : "", lines[i]));
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
    /// Where a detail's value starts, from the left of the panel: after the longest label, but
    /// never so far that a narrow panel leaves the values less than a third of it.
    /// </summary>
    internal static float ValueColumn(IReadOnlyList<Detail> details, float width, Func<string, float> measure) =>
        Math.Min(details.Count == 0 ? 0 : details.Max(d => measure(d.Label)) + measure(Gap), width * 2 / 3);
}
