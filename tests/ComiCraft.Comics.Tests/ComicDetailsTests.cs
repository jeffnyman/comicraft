namespace ComiCraft.Comics.Tests;

public class ComicDetailsTests
{
    [Fact]
    public void Details_come_in_reading_order()
    {
        var comic = TestComics.WithInfo("""
            <ComicInfo>
              <Series>Operation: Knightstrike</Series>
              <Number>3</Number>
              <Year>1995</Year>
              <Month>7</Month>
              <Writer>Brian Witten</Writer>
              <Colorist>Gloria Vasquez, Extreme Color</Colorist>
              <Publisher>Image</Publisher>
              <Characters>Bloodstrike, Chapel</Characters>
            </ComicInfo>
            """, "1.jpg", "2.jpg");

        Assert.Equal(
            [
                new Detail("Publisher", "Image"),
                new Detail("Published", "July 1995"),
                new Detail("Writer", "Brian Witten"),
                new Detail("Colorists", "Gloria Vasquez, Extreme Color"),
                new Detail("Characters", "Bloodstrike, Chapel"),
                new Detail("Pages", "2"),
                new Detail("File", "Test Comic"),
            ],
            ComicDetails.Of(comic));
    }

    [Fact]
    public void Without_a_ComicInfo_there_are_the_pages_and_the_file()
    {
        using var cbz = TestComics.Cbz("1.jpg");
        var comic = ComicBook.Open(cbz, "Troll 001 (1993)");

        Assert.Equal([new Detail("Pages", "1"), new Detail("File", "Troll 001 (1993)")], ComicDetails.Of(comic));
    }

    [Theory]
    [InlineData("<Number>3</Number><Count>4</Count>", "3 of 4")]
    [InlineData("<Count>4</Count>", "4 issues")]
    public void The_issue_is_shown_when_the_series_length_is_known(string fields, string issue)
    {
        var comic = TestComics.WithInfo($"<ComicInfo>{fields}</ComicInfo>");

        Assert.Contains(new Detail("Issue", issue), ComicDetails.Of(comic));
    }

    [Fact]
    public void A_number_alone_is_left_to_the_heading()
    {
        var comic = TestComics.WithInfo("<ComicInfo><Series>X</Series><Number>3</Number></ComicInfo>");

        Assert.DoesNotContain(ComicDetails.Of(comic), d => d.Label == "Issue");
    }

    [Theory]
    [InlineData(1995, 7, 4, "4 July 1995")]
    [InlineData(1995, 7, null, "July 1995")]
    [InlineData(1995, null, 4, "1995")]
    [InlineData(1995, 13, null, "1995")]
    [InlineData(null, 7, 4, null)]
    public void Dates_say_as_much_as_is_known(int? year, int? month, int? day, string? date)
    {
        Assert.Equal(date, ComicDetails.Date(year, month, day));
    }
}
