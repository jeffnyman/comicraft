namespace ComiCraft.Comics.Tests;

public class TextWrapTests
{
    /// <summary>Text measured in characters, as in a terminal.</summary>
    private static IReadOnlyList<string> Wrap(string text, int width) => TextWrap.Wrap(text, width, s => s.Length);

    [Fact]
    public void Short_text_is_one_line()
    {
        Assert.Equal(["Image"], Wrap("Image", 20));
    }

    [Fact]
    public void Lines_break_between_words()
    {
        Assert.Equal(["Bloodstrike,", "Chapel, Spawn", "(Simmons)"], Wrap("Bloodstrike, Chapel, Spawn (Simmons)", 13));
    }

    [Fact]
    public void A_word_that_fills_a_line_exactly_fits()
    {
        Assert.Equal(["abc def", "ghi"], Wrap("abc def ghi", 7));
    }

    [Fact]
    public void A_word_too_long_for_a_line_is_broken()
    {
        Assert.Equal(["a", "abcde", "fgh"], Wrap("a abcdefgh", 5));
    }

    [Fact]
    public void Line_breaks_in_the_text_are_kept()
    {
        Assert.Equal(["One.", "", "Two."], Wrap("One.\r\n\r\nTwo.", 20));
    }

    [Fact]
    public void Runs_of_spaces_and_tabs_count_as_one()
    {
        Assert.Equal(["a b c"], Wrap("  a \t b\tc  ", 20));
    }

    [Fact]
    public void Lines_fit_by_how_wide_the_text_is_not_how_long()
    {
        // as in a proportional font: a W is three times as wide as an i
        static float Measure(string s) => s.Sum(c => c == 'W' ? 3 : 1);

        Assert.Equal(["iii iii", "WW", "iii"], TextWrap.Wrap("iii iii WW iii", 7, Measure));
    }

    [Fact]
    public void A_letter_and_its_accent_stay_together()
    {
        // "e" and a combining acute accent, which measures as nothing
        static float Measure(string s) => s.Count(c => c != '́');

        Assert.Equal(["abé", "cd"], TextWrap.Wrap("abécd", 3, Measure));
    }

    [Fact]
    public void A_character_too_wide_for_a_line_still_gets_one()
    {
        Assert.Equal(["W", "W"], TextWrap.Wrap("WW", 1, s => s.Length * 3));
    }

    [Fact]
    public void A_width_must_be_given()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap("a", 0));
    }
}
