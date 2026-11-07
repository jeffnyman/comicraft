namespace ComiCraft.Comics.Tests;

public class TextWrapTests
{
    [Fact]
    public void Short_text_is_one_line()
    {
        Assert.Equal(["Image"], TextWrap.Wrap("Image", 20));
    }

    [Fact]
    public void Lines_break_between_words()
    {
        Assert.Equal(["Bloodstrike,", "Chapel, Spawn", "(Simmons)"], TextWrap.Wrap("Bloodstrike, Chapel, Spawn (Simmons)", 13));
    }

    [Fact]
    public void A_word_that_fills_a_line_exactly_fits()
    {
        Assert.Equal(["abc def", "ghi"], TextWrap.Wrap("abc def ghi", 7));
    }

    [Fact]
    public void A_word_too_long_for_a_line_is_broken()
    {
        Assert.Equal(["a", "abcde", "fgh"], TextWrap.Wrap("a abcdefgh", 5));
    }

    [Fact]
    public void Line_breaks_in_the_text_are_kept()
    {
        Assert.Equal(["One.", "", "Two."], TextWrap.Wrap("One.\r\n\r\nTwo.", 20));
    }

    [Fact]
    public void Runs_of_spaces_count_as_one()
    {
        Assert.Equal(["a b"], TextWrap.Wrap("  a   b  ", 20));
    }

    [Fact]
    public void A_width_must_be_given()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextWrap.Wrap("a", 0));
    }
}
