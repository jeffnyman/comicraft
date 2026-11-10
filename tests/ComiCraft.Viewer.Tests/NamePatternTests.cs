using ComiCraft.Viewer.Lookup;

namespace ComiCraft.Viewer.Tests;

/// <summary>Naming a comic by a pattern of its fields.</summary>
public class NamePatternTests
{
    private static readonly Dictionary<string, string> SecretWars = new()
    {
        ["Series"] = "Marvel Super Heroes Secret Wars",
        ["Number"] = "1",
        ["Title"] = "The War Begins!",
        ["Year"] = "1984",
        ["Month"] = "5",
        ["Publisher"] = "Marvel",
    };

    private static string? Name(string pattern, Dictionary<string, string>? fields = null) =>
        NamePattern.Parse(pattern).Format(f => (fields ?? SecretWars).GetValueOrDefault(f));

    [Fact]
    public void The_default_puts_the_cover_date_first_so_names_sort_by_it()
    {
        Assert.Equal("198405 - Marvel Super Heroes Secret Wars 01", Name(NamePattern.Default));
    }

    [Theory]
    [InlineData("{series} {number:000} ({year})", "Marvel Super Heroes Secret Wars 001 (1984)")]
    [InlineData("{series} #{number} - {title}", "Marvel Super Heroes Secret Wars #1 - The War Begins!")]
    [InlineData("{YEAR}-{Month} {publisher}", "1984-05 Marvel")]
    public void A_pattern_of_its_own(string pattern, string name)
    {
        Assert.Equal(name, Name(pattern));
    }

    [Fact]
    public void A_pattern_with_a_field_the_comic_does_not_have_names_nothing()
    {
        Assert.Null(Name("{year}{month} - {series}", new() { ["Series"] = "Troll", ["Year"] = "1993" }));
    }

    [Theory]
    [InlineData("1", 2, "01")]
    [InlineData("12", 2, "12")]
    [InlineData("123", 2, "123")]
    [InlineData("1.5", 3, "001.5")]
    [InlineData("1/2", 3, "001/2")]
    [InlineData("Annual", 3, "Annual")]
    public void Numbers_are_padded_with_zeros(string number, int width, string padded)
    {
        Assert.Equal(padded, NamePattern.Pad(number, width));
    }

    [Theory]
    [InlineData("Batman: Year One", "Batman - Year One")]
    [InlineData("What If? 1", "What If 1")]
    [InlineData("AC/DC \"Live\"", "AC-DC 'Live'")]
    [InlineData("Trailing...", "Trailing")]
    [InlineData("  Two   spaces  ", "Two spaces")]
    public void A_name_is_made_fit_for_a_file_anywhere(string name, string clean)
    {
        Assert.Equal(clean, NamePattern.Clean(name));
    }

    [Theory]
    [InlineData("{series} {issue}", "not a field")]
    [InlineData("{series:00}", "only numbers")]
    [InlineData("{series} {number", "{ or }")]
    [InlineData(" ", "empty")]
    public void A_pattern_that_cannot_be_used_says_why(string pattern, string why)
    {
        Assert.Contains(why, Assert.Throws<FormatException>(() => NamePattern.Parse(pattern)).Message);
    }
}
