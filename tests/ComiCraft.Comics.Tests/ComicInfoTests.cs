using System.Text;
using ComiCraft.Comics;

namespace ComiCraft.Comics.Tests;

public class ComicInfoTests
{
    private static ComicInfo? Parse(string xml) => ComicInfo.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    // as a ComicVine scraper wrote it, for Operation: Knightstrike #3
    private const string Knightstrike = """
        <?xml version="1.0"?>
        <ComicInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Title>Enter... Bloodstrike</Title>
          <Series>Operation: Knightstrike</Series>
          <Number>3</Number>
          <Volume>1995</Volume>
          <Year>1995</Year>
          <Month>7</Month>
          <Writer>Brian Witten</Writer>
          <Inker>Jaime Mendoza</Inker>
          <Colorist>Gloria Vasquez, Extreme Color</Colorist>
          <Publisher>Image</Publisher>
          <PageCount>25</PageCount>
          <Characters>Bloodstrike, Chapel, Spawn (Simmons), Battlestone</Characters>
          <Pages>
            <Page Image="0" ImageWidth="950" ImageHeight="1443" Type="FrontCover" />
          </Pages>
        </ComicInfo>
        """;

    [Fact]
    public void Reads_the_fields_a_scraper_wrote()
    {
        var info = Parse(Knightstrike)!;

        Assert.Equal("Enter... Bloodstrike", info.Title);
        Assert.Equal("Operation: Knightstrike", info.Series);
        Assert.Equal("3", info.Number);
        Assert.Equal(1995, info.Volume);
        Assert.Equal(1995, info.Year);
        Assert.Equal(7, info.Month);
        Assert.Equal(["Brian Witten"], info.Writers);
        Assert.Equal("Image", info.Publisher);
        Assert.Equal(25, info.PageCount);
    }

    [Fact]
    public void Lists_are_split_at_commas()
    {
        var info = Parse(Knightstrike)!;

        Assert.Equal(["Gloria Vasquez", "Extreme Color"], info.Colorists);
        Assert.Equal(["Bloodstrike", "Chapel", "Spawn (Simmons)", "Battlestone"], info.Characters);
    }

    [Fact]
    public void Missing_fields_are_null_or_empty()
    {
        var info = Parse(Knightstrike)!;

        Assert.Null(info.Summary);
        Assert.Null(info.Day);
        Assert.Empty(info.Pencillers);
    }

    [Fact]
    public void An_empty_document_has_nothing_in_it()
    {
        var info = Parse("<ComicInfo />")!;

        Assert.Null(info.Series);
        Assert.Null(info.Heading);
        Assert.Empty(info.Writers);
    }

    [Fact]
    public void Blank_and_unreadable_values_are_missing()
    {
        var info = Parse("<ComicInfo><Series>  </Series><Year>nineteen</Year><Month></Month></ComicInfo>")!;

        Assert.Null(info.Series);
        Assert.Null(info.Year);
        Assert.Null(info.Month);
    }

    [Theory]
    [InlineData("<ComicInfo><Series>Unclosed</ComicInfo>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    [InlineData("<SomethingElse />")]
    public void A_broken_or_foreign_document_gives_nothing(string xml)
    {
        Assert.Null(Parse(xml));
    }

    [Fact]
    public void A_doctype_is_ignored()
    {
        var info = Parse("""<!DOCTYPE ComicInfo SYSTEM "http://example.invalid/comicinfo.dtd"><ComicInfo><Series>X</Series></ComicInfo>""");

        Assert.Equal("X", info?.Series);
    }

    [Theory]
    [InlineData("<Series>Operation: Knightstrike</Series><Number>3</Number><Title>Enter... Bloodstrike</Title><Year>1995</Year><Month>7</Month>",
        "Operation: Knightstrike #3 – Enter... Bloodstrike (July 1995)")]
    [InlineData("<Series>Operation: Knightstrike</Series><Number>1</Number><Year>1995</Year><Month>5</Month>",
        "Operation: Knightstrike #1 (May 1995)")]
    [InlineData("<Series>Troll</Series><Year>1993</Year>", "Troll (1993)")]
    [InlineData("<Series>Troll</Series><Year>1993</Year><Month>13</Month>", "Troll (1993)")]
    [InlineData("<Series>Troll</Series><Number>1</Number>", "Troll #1")]
    [InlineData("<Title>A One-Shot</Title><Month>3</Month>", "A One-Shot")]
    public void The_heading_says_as_much_as_is_known(string fields, string heading)
    {
        Assert.Equal(heading, Parse($"<ComicInfo>{fields}</ComicInfo>")!.Heading);
    }

    [Theory]
    [InlineData("ComicInfo.xml", true)]
    [InlineData("comicinfo.xml", true)]
    [InlineData("Issue 1/ComicInfo.xml", false)]
    [InlineData("ComicInfo.xml.bak", false)]
    public void Only_the_root_ComicInfo_counts(string entryName, bool isComicInfo)
    {
        Assert.Equal(isComicInfo, ComicInfo.IsComicInfo(entryName));
    }
}
