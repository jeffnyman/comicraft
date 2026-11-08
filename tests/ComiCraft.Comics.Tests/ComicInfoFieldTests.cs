namespace ComiCraft.Comics.Tests;

public class ComicInfoFieldTests
{
    [Theory]
    [InlineData("Series", "  Operation: Knightstrike  ", "Operation: Knightstrike")]
    [InlineData("Inker", "Rob Liefeld,Jaime Mendoza , ", "Rob Liefeld, Jaime Mendoza")]
    [InlineData("Year", " 1995 ", "1995")]
    [InlineData("Month", "07", "7")]
    [InlineData("Manga", "yesandrighttoleft", "YesAndRightToLeft")]
    [InlineData("AgeRating", "teen", "Teen")]
    [InlineData("AgeRating", "mature 17+", "Mature 17+")]
    [InlineData("CommunityRating", "4.50", "4.50")]
    [InlineData("Number", "1.5", "1.5")]
    public void Values_are_written_as_the_schema_spells_them(string field, string value, string written)
    {
        Assert.Equal(written, ComicInfoField.Find(field).Normalize(value));
    }

    [Theory]
    [InlineData("Series", "")]
    [InlineData("Series", "   ")]
    [InlineData("Writer", " , ,")]
    [InlineData("Year", null)]
    public void Nothing_is_no_value(string field, string? value)
    {
        Assert.Null(ComicInfoField.Find(field).Normalize(value));
    }

    [Theory]
    [InlineData("Month", "13", "Month must be a whole number from 1 to 12, not '13'.")]
    [InlineData("Year", "nineteen", "Year must be a whole number from 0 to 9999, not 'nineteen'.")]
    [InlineData("Count", "-1", "Count must be a whole number of 0 or more, not '-1'.")]
    [InlineData("CommunityRating", "5.5", "CommunityRating must be a number from 0 to 5, such as 4.5, not '5.5'.")]
    [InlineData("BlackAndWhite", "maybe", "BlackAndWhite must be one of Unknown, No, Yes; not 'maybe'.")]
    public void A_value_the_field_cannot_hold_says_what_it_can(string field, string value, string message)
    {
        var ex = Assert.Throws<ComicEditException>(() => ComicInfoField.Find(field).Normalize(value));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Fields_are_found_whatever_their_case()
    {
        Assert.Equal("CoverArtist", ComicInfoField.Find("coverartist").Name);
    }

    [Fact]
    public void A_field_the_schema_does_not_have_is_an_error_that_lists_them()
    {
        var ex = Assert.Throws<ComicEditException>(() => ComicInfoField.Find("Artist"));
        Assert.StartsWith("ComicInfo.xml has no field 'Artist'. Its fields are: Title, Series, Number,", ex.Message);
        Assert.DoesNotContain("Pages", ex.Message);
    }

    [Fact]
    public void Pages_is_not_a_field_to_edit()
    {
        Assert.Throws<ComicEditException>(() => ComicInfoField.Find("Pages"));
    }

    [Fact]
    public void Lists_and_choices_are_known()
    {
        Assert.True(ComicInfoField.Find("Characters").IsList);
        Assert.False(ComicInfoField.Find("Summary").IsList);
        Assert.Equal(["Unknown", "No", "Yes", "YesAndRightToLeft"], ComicInfoField.Find("Manga").Choices);
    }
}
