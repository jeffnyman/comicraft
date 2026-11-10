using ComiCraft.Viewer.Lookup;

namespace ComiCraft.Viewer.Tests;

/// <summary>Asking Metron, as its API answers; and what it says of an issue, as ComicInfo fields.</summary>
public class MetronTests
{
    private readonly StringWriter _said = new();

    [Fact]
    public void Series_are_searched_by_their_words_and_year()
    {
        var fake = FakeMetron.WithSecretWars();

        var found = fake.Client(_said).SearchSeries("secret wars", 1984);

        Assert.Equal([1001], found.Select(s => s.Id));
        Assert.Equal("Marvel Super Heroes Secret Wars (1984)", found[0].Display);
        Assert.Equal(12, found[0].IssueCount);
        Assert.Equal("Marvel", found[0].Publisher?.Name);
        Assert.Equal("series/?q=secret%20wars&year_began=1984", fake.Asked.Single());
    }

    [Fact]
    public void A_series_issues_are_gathered_from_every_page()
    {
        var fake = FakeMetron.WithSecretWars();

        var issues = fake.Client(_said).Issues(1001);

        Assert.Equal(Enumerable.Range(1, 12).Select(n => n.ToString()), issues.Select(i => i.Number));
        Assert.Equal(6, fake.Asked.Count);
    }

    [Fact]
    public void Every_request_has_the_credentials()
    {
        var fake = FakeMetron.WithSecretWars();

        fake.Client(_said).Issues(1001);

        Assert.All(fake.Credentials, c => Assert.Equal("Basic dGVzdDp0ZXN0", c));
    }

    [Fact]
    public void When_Metron_says_to_wait_it_is_waited_for_and_asked_again()
    {
        var fake = FakeMetron.WithSecretWars();
        fake.TooMany = 2;

        var issue = fake.Client(_said).Issue(5001);

        Assert.Equal("1", issue.Number);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], fake.Waited);
        Assert.Contains("Metron asks to wait 5 seconds", _said.ToString());
    }

    [Fact]
    public void Metron_saying_to_wait_too_often_is_given_up_on()
    {
        var fake = FakeMetron.WithSecretWars();
        fake.TooMany = 10;

        var ex = Assert.Throws<MetronException>(() => fake.Client(_said).Issue(5001));

        Assert.Contains("429", ex.Message);
        Assert.Equal(3, fake.Waited.Count);
    }

    [Fact]
    public void A_wait_too_long_to_make_is_not_made()
    {
        var fake = FakeMetron.WithSecretWars();
        fake.TooMany = 1;
        fake.RetryAfter = TimeSpan.FromHours(3);

        var ex = Assert.Throws<MetronException>(() => fake.Client(_said).Issue(5001));

        Assert.Contains("asks to wait 180 minutes", ex.Message);
        Assert.Empty(fake.Waited);
    }

    [Fact]
    public void Credentials_Metron_will_not_take_say_where_they_came_from()
    {
        var fake = FakeMetron.WithSecretWars();
        fake.Unauthorized = true;

        var ex = Assert.Throws<MetronException>(() => fake.Client(_said).Issue(5001));

        Assert.Contains("METRON_USERNAME", ex.Message);
    }

    [Fact]
    public void The_credentials_are_a_token_or_a_user_name_and_password()
    {
        Assert.Equal("Bearer abc", Metron.Credentials(v => v == "METRON_TOKEN" ? "abc" : null, out _)?.ToString());
        Assert.Equal(
            "Basic dXNlcjpwYXNz",
            Metron.Credentials(v => v switch { "METRON_USERNAME" => "user", "METRON_PASSWORD" => "pass", _ => null }, out _)?.ToString());
    }

    [Fact]
    public void Without_credentials_it_says_how_to_give_them()
    {
        Assert.Null(Metron.Credentials(_ => null, out string? problem));
        Assert.Contains("metron.cloud", problem);
        Assert.Contains("METRON_PASSWORD", problem);
    }

    [Fact]
    public void An_issue_is_given_as_the_fields_of_a_comic_info()
    {
        var fake = FakeMetron.WithSecretWars();
        var metron = fake.Client(_said);
        var series = metron.SearchSeries("secret wars", 1984)[0];

        var fields = IssueFields.Of(metron.Issue(5001), series).ToDictionary(f => f.Field, f => f.Value);

        Assert.Equal("The War Begins!", fields["Title"]);
        Assert.Equal("Marvel Super Heroes Secret Wars", fields["Series"]);
        Assert.Equal("1", fields["Number"]);
        Assert.Equal("12", fields["Count"]);
        Assert.Equal("1984", fields["Year"]);
        Assert.Equal("5", fields["Month"]);
        Assert.False(fields.ContainsKey("Day"));
        Assert.Equal("Jim Shooter", fields["Writer"]);
        Assert.Equal("Mike Zeck", fields["Penciller"]);
        Assert.Equal("Mike Zeck", fields["CoverArtist"]);
        Assert.Equal("John Beatty", fields["Inker"]);
        Assert.Equal("Tom DeFalco", fields["Editor"]);
        Assert.Equal("Marvel", fields["Publisher"]);
        Assert.Equal("Spider-Man, Doctor Doom", fields["Characters"]);
        Assert.Equal("Avengers", fields["Teams"]);
        Assert.Equal("Super-Hero", fields["Genre"]);
        Assert.Equal("Everyone", fields["AgeRating"]);
        Assert.Equal("en", fields["LanguageISO"]);
        Assert.Equal("https://metron.test/issue/5001/", fields["Web"]);
        Assert.StartsWith(IssueFields.NotesStart, fields["Notes"]);
        Assert.Contains("CC BY-SA", fields["Notes"]);
        Assert.False(fields.ContainsKey("GTIN"));
        Assert.False(fields.ContainsKey("StoryArc"));
    }

    [Fact]
    public void A_series_still_running_gives_no_count()
    {
        var fake = FakeMetron.WithSecretWars();
        var metron = fake.Client(_said);
        var running = metron.SearchSeries("secret wars", 1984)[0] with { YearEnd = null };

        Assert.DoesNotContain(IssueFields.Of(metron.Issue(5001), running), f => f.Field == "Count");
    }
}
