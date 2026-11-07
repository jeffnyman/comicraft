using ComiCraft.Comics;
using static ComiCraft.Viewer.InfoOverlay;

namespace ComiCraft.Viewer.Tests;

public class InfoOverlayTests
{
    private static readonly Detail[] Details =
    [
        new("Publisher", "Image"),
        new("Characters", "Bloodstrike, Chapel, Spawn (Simmons), Battlestone"),
    ];

    /// <summary>Text measured in characters, and the heading's twice as wide, as a larger face would be.</summary>
    private static float Measure(string text) => text.Length;

    private static float MeasureHeading(string text) => text.Length * 2;

    private static IReadOnlyList<Row> Layout(string heading, IReadOnlyList<Detail> details, float width, int maxRows) =>
        InfoOverlay.Layout(heading, details, width, maxRows, Measure, MeasureHeading);

    [Fact]
    public void The_heading_comes_first_then_a_blank_row_then_the_details()
    {
        var rows = Layout("Knightstrike #3", Details, 80, 50);

        Assert.Equal(
            [
                new Row(RowKind.Heading, "", "Knightstrike #3"),
                new Row(RowKind.Blank, "", ""),
                new Row(RowKind.Detail, "Publisher", "Image"),
                new Row(RowKind.Detail, "Characters", "Bloodstrike, Chapel, Spawn (Simmons), Battlestone"),
            ],
            rows);
    }

    [Fact]
    public void The_heading_is_measured_in_its_own_face()
    {
        // 30 wide fits 15 of the heading's characters
        var rows = Layout("Operation: Knightstrike #3", Details, 30, 50);

        Assert.Equal(
            [new Row(RowKind.Heading, "", "Operation:"), new Row(RowKind.Heading, "", "Knightstrike #3")],
            rows.Take(2));
    }

    [Fact]
    public void A_long_value_wraps_under_itself()
    {
        // "Characters" and the gap leave 18 of the 30 for the value
        var rows = Layout("X", Details, 30, 50);

        Assert.Equal(12, ValueColumn(Details, 30, Measure));
        Assert.Equal(
            [
                new Row(RowKind.Detail, "Characters", "Bloodstrike,"),
                new Row(RowKind.Detail, "", "Chapel, Spawn"),
                new Row(RowKind.Detail, "", "(Simmons),"),
                new Row(RowKind.Detail, "", "Battlestone"),
            ],
            rows.Skip(3));
    }

    [Fact]
    public void A_narrow_panel_keeps_a_third_for_the_values()
    {
        Assert.Equal(8, ValueColumn(Details, 12, Measure));
    }

    [Fact]
    public void What_does_not_fit_is_cut_and_said_to_be()
    {
        var rows = Layout("X", Details, 30, 4);

        Assert.Equal(4, rows.Count);
        Assert.Equal(new Row(RowKind.Detail, "", "…"), rows[^1]);
    }

    [Fact]
    public void Text_is_kept_as_it_is()
    {
        var rows = Layout("Knightstrike #3 – Enter… Bloodstrike", [new("Writer", "José García")], 200, 50);

        Assert.Equal("Knightstrike #3 – Enter… Bloodstrike", rows[0].Text);
        Assert.Equal("José García", rows[^1].Text);
    }
}
