using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using ComiCraft.Viewer.Lookup;

namespace ComiCraft.Viewer.Tests;

/// <summary>
/// Metron, as its API answers, from series and issues of the test's own: series searched by
/// every word of q in their names, and by the year they began; a series' issues listed a page
/// of two at a time; an issue given by its id. It can be told to say "wait" or "who are you".
/// </summary>
internal sealed class FakeMetron : HttpMessageHandler
{
    public const string Base = "https://metron.test/api/";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public sealed record Series(int Id, string Name, int YearBegan, int? YearEnd, int IssueCount, string Publisher = "Marvel");

    public sealed record Issue(int Id, int SeriesId, string Number, string CoverDate, string Title, params (string Creator, string Role)[] Credits);

    public List<Series> AllSeries { get; } = [];

    public List<Issue> Issues { get; } = [];

    /// <summary>What was asked, path and query, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>The Authorization header each request came with.</summary>
    public List<string?> Credentials { get; } = [];

    /// <summary>How many requests to answer with 429, and the wait to ask for.</summary>
    public int TooMany { get; set; }

    public TimeSpan? RetryAfter { get; set; } = TimeSpan.FromSeconds(5);

    public bool Unauthorized { get; set; }

    public List<TimeSpan> Waited { get; } = [];

    /// <summary>The Metron that asks this one, with credentials, and waiting by noting the wait.</summary>
    public Metron Client(TextWriter say)
    {
        var http = new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri(Base) };
        http.DefaultRequestHeaders.Authorization = new("Basic", "dGVzdDp0ZXN0");
        return new Metron(http, say, Waited.Add);
    }

    /// <summary>Secret Wars, as Metron has it, with another of the same name to choose between; and its twelve issues.</summary>
    public static FakeMetron WithSecretWars()
    {
        var metron = new FakeMetron();
        metron.AllSeries.Add(new Series(1001, "Marvel Super Heroes Secret Wars", 1984, 1985, 12));
        metron.AllSeries.Add(new Series(2002, "Secret Wars", 2015, 2016, 9));
        metron.AllSeries.Add(new Series(3003, "Secret Wars II", 1985, 1986, 9));
        string[] titles = ["The War Begins!", "Prisoners of War", "Tempest Fugit", "Situation: Hopeless!"];
        for (int n = 1; n <= 12; n++)
        {
            int month = (n + 4 - 1) % 12 + 1, year = n + 4 > 12 ? 1985 : 1984;
            metron.Issues.Add(new Issue(5000 + n, 1001, n.ToString(), $"{year}-{month:00}-01", n <= titles.Length ? titles[n - 1] : $"Chapter {n}",
                ("Jim Shooter", "Writer"), ("Mike Zeck", "Penciller"), ("John Beatty", "Inker"), ("Mike Zeck", "Cover"), ("Tom DeFalco", "Editor")));
        }

        return metron;
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        string path = uri.AbsolutePath["/api/".Length..];
        Asked.Add(path + uri.Query);
        Credentials.Add(request.Headers.Authorization?.ToString());

        if (Unauthorized)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (TooMany > 0)
        {
            TooMany--;
            var wait = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (RetryAfter is { } after)
            {
                wait.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(after);
            }

            return wait;
        }

        var query = HttpUtility.ParseQueryString(uri.Query);
        string[] parts = path.Trim('/').Split('/');
        return parts switch
        {
            ["series"] => Ok(new { next = (string?)null, results = SearchSeries(query["q"] ?? "", query["year_began"]).Select(ListItem) }),
            ["series", var id] when AllSeries.FirstOrDefault(s => s.Id.ToString() == id) is { } s => Ok(Detail(s)),
            ["issue"] => IssuePage(int.Parse(query["series_id"]!), int.Parse(query["page"] ?? "1")),
            ["issue", var id] when Issues.FirstOrDefault(i => i.Id.ToString() == id) is { } i => Ok(Detail(i)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    private IEnumerable<Series> SearchSeries(string q, string? year) =>
        AllSeries.Where(s =>
            q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => s.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) &&
            (year is null || s.YearBegan.ToString() == year));

    private static object ListItem(Series s) => new
    {
        id = s.Id,
        series = $"{s.Name} ({s.YearBegan})",
        year_began = s.YearBegan,
        year_end = s.YearEnd,
        volume = 1,
        issue_count = s.IssueCount,
        publisher = new { id = 1, name = s.Publisher },
        series_type = new { id = 1, name = "Limited Series" },
    };

    private static object Detail(Series s) => new
    {
        id = s.Id,
        name = s.Name,
        sort_name = s.Name,
        year_began = s.YearBegan,
        year_end = s.YearEnd,
        volume = 1,
        issue_count = s.IssueCount,
        publisher = new { id = 1, name = s.Publisher },
    };

    private HttpResponseMessage IssuePage(int seriesId, int page)
    {
        var all = Issues.Where(i => i.SeriesId == seriesId).ToList();
        var these = all.Skip((page - 1) * 2).Take(2);
        string? next = page * 2 < all.Count ? $"{Base}issue/?page={page + 1}&series_id={seriesId}" : null;
        return Ok(new
        {
            count = all.Count,
            next,
            results = these.Select(i => new { id = i.Id, number = i.Number, issue = $"#{i.Number}", cover_date = i.CoverDate }),
        });
    }

    private object Detail(Issue i)
    {
        var s = AllSeries.First(x => x.Id == i.SeriesId);
        return new
        {
            id = i.Id,
            publisher = new { id = 1, name = s.Publisher },
            imprint = (object?)null,
            series = new { id = s.Id, name = s.Name, volume = 1, year_began = s.YearBegan, genres = new[] { new { id = 1, name = "Super-Hero" } }, language = "en" },
            number = i.Number,
            title = "",
            name = new[] { i.Title },
            cover_date = i.CoverDate,
            rating = new { id = 1, name = "Everyone" },
            isbn = "",
            upc = "",
            desc = $"Issue {i.Number} of the war.",
            arcs = Array.Empty<object>(),
            credits = i.Credits.GroupBy(c => c.Creator).Select(g => new { id = 1, creator = g.Key, role = g.Select(c => new { id = 1, name = c.Role }) }),
            characters = new[] { new { id = 1, name = "Spider-Man" }, new { id = 2, name = "Doctor Doom" } },
            teams = new[] { new { id = 1, name = "Avengers" } },
            resource_url = $"https://metron.test/issue/{i.Id}/",
        };
    }

    private static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json") };
}
