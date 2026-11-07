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

    [Fact]
    public void The_heading_comes_first_then_a_blank_row_then_the_details()
    {
        var rows = Layout("Operation: Knightstrike #3", Details, 80, 50);

        Assert.Equal(
            [
                new Row(RowKind.Heading, "", "Operation: Knightstrike #3"),
                new Row(RowKind.Blank, "", ""),
                new Row(RowKind.Detail, "Publisher", "Image"),
                new Row(RowKind.Detail, "Characters", "Bloodstrike, Chapel, Spawn (Simmons), Battlestone"),
            ],
            rows);
    }

    [Fact]
    public void A_long_value_wraps_under_itself()
    {
        // "Characters" and two spaces leave 18 of the 30 columns for the value
        var rows = Layout("X", Details, 30, 50);

        Assert.Equal(12, ValueColumn(Details, 30));
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
        Assert.Equal(8, ValueColumn(Details, 12));
    }

    [Fact]
    public void What_does_not_fit_is_cut_and_said_to_be()
    {
        var rows = Layout("X", Details, 30, 4);

        Assert.Equal(4, rows.Count);
        Assert.Equal(new Row(RowKind.Detail, "", "..."), rows[^1]);
    }

    [Theory]
    [InlineData("Operation: Knightstrike #3 – Enter… Bloodstrike", "Operation: Knightstrike #3 - Enter... Bloodstrike")]
    [InlineData("José García", "Jose Garcia")]
    [InlineData("“Quoted” and ‘single’", "\"Quoted\" and 'single'")]
    [InlineData("Straße", "Strasse")]
    [InlineData("line\r\nnext\ttab", "line\nnext tab")]
    [InlineData("龍", "?")]
    public void Text_is_folded_to_what_the_font_draws(string text, string folded)
    {
        Assert.Equal(folded, Fold(text));
    }
}
