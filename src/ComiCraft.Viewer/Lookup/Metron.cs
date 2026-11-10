using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ComiCraft.Viewer.Lookup;

/// <summary>Something Metron could not be asked, or would not answer; what to do about it, where there is something.</summary>
internal sealed class MetronException(string message) : Exception(message);

/// <summary>A thing Metron names: a publisher, a role, a character.</summary>
internal sealed record MetronNamed(int Id, string Name);

/// <summary>A series, as Metron lists it when searched, or gives it by its id.</summary>
internal sealed record MetronSeries
{
    public int Id { get; init; }

    /// <summary>The series' name with its year, as a search lists it: "Marvel Super Heroes Secret Wars (1984)".</summary>
    public string? Series { get; init; }

    /// <summary>The series' name alone, as it is given by its id.</summary>
    public string? Name { get; init; }

    public int YearBegan { get; init; }

    public int? YearEnd { get; init; }

    public int? IssueCount { get; init; }

    public int? Volume { get; init; }

    public MetronNamed? Publisher { get; init; }

    /// <summary>The series as it is shown to choose it: its name and year.</summary>
    public string Display => Series ?? $"{Name} ({YearBegan})";
}

/// <summary>An issue as a series' issues are listed: enough to find it by its number.</summary>
internal sealed record MetronIssueEntry(int Id, string Number, string? CoverDate);

/// <summary>A credit on an issue: who, and what they did.</summary>
internal sealed record MetronCredit(string Creator, List<MetronNamed>? Role);

/// <summary>The series an issue is in, as the issue gives it.</summary>
internal sealed record MetronIssueSeries(int Id, string Name, int? Volume, int YearBegan, List<MetronNamed>? Genres, string? Language);

/// <summary>Everything Metron says about an issue that a ComicInfo.xml has a place for.</summary>
internal sealed record MetronIssue
{
    public int Id { get; init; }

    public MetronNamed? Publisher { get; init; }

    public MetronNamed? Imprint { get; init; }

    public required MetronIssueSeries Series { get; init; }

    public required string Number { get; init; }

    /// <summary>The collection's title, for a trade paperback or the like.</summary>
    public string? Title { get; init; }

    /// <summary>The titles of the stories in the issue.</summary>
    public List<string>? Name { get; init; }

    /// <summary>The date on the cover, as YYYY-MM-DD, the day being the first of the month where the cover gives only a month.</summary>
    public string? CoverDate { get; init; }

    public MetronNamed? Rating { get; init; }

    public string? Isbn { get; init; }

    public string? Upc { get; init; }

    public string? Desc { get; init; }

    public List<MetronNamed>? Arcs { get; init; }

    public List<MetronCredit>? Credits { get; init; }

    public List<MetronNamed>? Characters { get; init; }

    public List<MetronNamed>? Teams { get; init; }

    public string? ResourceUrl { get; init; }
}

/// <summary>
/// The Metron comic book database (https://metron.cloud), asked through its API, which needs an
/// account: a token in METRON_TOKEN, or a user name and password in METRON_USERNAME and
/// METRON_PASSWORD. Metron limits how often it can be asked; when it says to wait, the wait is
/// made, and it is asked again.
/// </summary>
internal sealed class Metron
{
    public const string Site = "https://metron.cloud";

    /// <summary>How many times a request Metron says to wait on is tried again before giving up.</summary>
    private const int Retries = 3;

    /// <summary>The longest wait made when Metron says to wait; a longer one is given up on.</summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _http;
    private readonly Action<TimeSpan> _wait;
    private readonly TextWriter _say;

    private sealed record Page<T>(List<T>? Results, string? Next);

    /// <summary>Metron, asked through the client, which has its base address and its credentials; waiting as <paramref name="wait"/> does, and saying so to <paramref name="say"/>.</summary>
    public Metron(HttpClient http, TextWriter say, Action<TimeSpan>? wait = null)
    {
        _http = http;
        _say = say;
        _wait = wait ?? Thread.Sleep;
    }

    /// <summary>The environment variables that hold the credentials.</summary>
    public const string TokenVariable = "METRON_TOKEN", UserVariable = "METRON_USERNAME", PasswordVariable = "METRON_PASSWORD";

    /// <summary>
    /// The credentials for Metron from the environment, as an Authorization header: a token, or a
    /// user name and password; null, with why, when there are none.
    /// </summary>
    public static AuthenticationHeaderValue? Credentials(Func<string, string?> environment, out string? problem)
    {
        problem = null;
        if (environment(TokenVariable) is { Length: > 0 } token)
        {
            return new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        string? user = environment(UserVariable), password = environment(PasswordVariable);
        if (user is { Length: > 0 } && password is { Length: > 0 })
        {
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        }

        problem = $"""
            Looking comics up needs a Metron account, which is free: sign up at {Site}.
            Then give ComiCraft your user name and password in {UserVariable} and {PasswordVariable}
            (or an API token in {TokenVariable}), as environment variables.
            """;
        return null;
    }

    /// <summary>A client for Metron's API, with the credentials.</summary>
    public static HttpClient Client(AuthenticationHeaderValue credentials)
    {
        var http = new HttpClient { BaseAddress = new Uri($"{Site}/api/"), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = credentials;
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ComiCraft", typeof(Metron).Assembly.GetName().Version?.ToString(3) ?? "0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    /// <summary>The series whose names (or other names) have every one of the words, begun in the year when one is given.</summary>
    public IReadOnlyList<MetronSeries> SearchSeries(string words, int? yearBegan)
    {
        string query = $"series/?q={Uri.EscapeDataString(words)}";
        if (yearBegan is { } year)
        {
            query += $"&year_began={year.ToString(CultureInfo.InvariantCulture)}";
        }

        return All<MetronSeries>(query, most: 3);
    }

    /// <summary>The series with that id.</summary>
    public MetronSeries Series(int id) => Get<MetronSeries>($"series/{id.ToString(CultureInfo.InvariantCulture)}/");

    /// <summary>Every issue in the series.</summary>
    public IReadOnlyList<MetronIssueEntry> Issues(int seriesId) =>
        All<MetronIssueEntry>($"issue/?series_id={seriesId.ToString(CultureInfo.InvariantCulture)}", most: 50);

    /// <summary>The issue with that id, with all Metron says of it.</summary>
    public MetronIssue Issue(int id) => Get<MetronIssue>($"issue/{id.ToString(CultureInfo.InvariantCulture)}/");

    /// <summary>The results of a list, page by page, to at most so many pages.</summary>
    private List<T> All<T>(string query, int most)
    {
        var all = new List<T>();
        string? next = query;
        for (int page = 0; next is not null && page < most; page++)
        {
            var got = Get<Page<T>>(next);
            all.AddRange(got.Results ?? []);
            next = got.Next;
        }

        return all;
    }

    private T Get<T>(string query)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            HttpResponseMessage response;
            try
            {
                response = _http.Send(request);
            }
            catch (HttpRequestException ex)
            {
                throw new MetronException($"Metron could not be reached: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                throw new MetronException("Metron did not answer in time.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < Retries)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
                    if (wait > LongestWait)
                    {
                        throw new MetronException($"Metron has been asked as often as it allows for now, and asks to wait {Describe(wait)}. Try again then.");
                    }

                    _say.WriteLine($"Metron asks to wait {Describe(wait)} before it is asked again; waiting.");
                    _wait(wait);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new MetronException($"Metron did not accept the credentials in {UserVariable} and {PasswordVariable} (or {TokenVariable}).");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MetronException($"Metron has nothing at {query}.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new MetronException($"Metron answered {(int)response.StatusCode} {response.ReasonPhrase} to {query}.");
                }

                try
                {
                    using var body = response.Content.ReadAsStream();
                    return JsonSerializer.Deserialize<T>(body, Json) ?? throw new JsonException("it was empty");
                }
                catch (JsonException ex)
                {
                    throw new MetronException($"Metron's answer to {query} could not be read: {ex.Message}");
                }
            }
        }
    }

    private static string Describe(TimeSpan wait) =>
        wait.TotalSeconds < 120 ? $"{Math.Ceiling(wait.TotalSeconds):0} seconds" : $"{Math.Ceiling(wait.TotalMinutes):0} minutes";
}
