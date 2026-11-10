using System.Text.RegularExpressions;
using ComiCraft.Comics;
using ComiCraft.Viewer.Lookup;
using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Cli;

/// <summary>
/// comicraft tag --lookup: fills in a comic's ComicInfo.xml, or every comic's in a folder, from the
/// Metron comic book database, and, with --rename, names each by what it then says.
/// <para>
/// Which issue a comic is comes from its ComicInfo.xml, where that has the series and number, or
/// else from its file name. The comics of one series are looked up together: the series is found
/// once, and chosen by the reader from what Metron has, and then each comic is found in it by its
/// number. Everything is looked up, and every change shown, before any comic is touched; so a
/// lookup that fails, or a reader who says no, leaves every comic as it was.
/// </para>
/// </summary>
internal static partial class LookupCommand
{
    public const string Usage = """
        Usage: comicraft tag <comic | folder> --lookup [--rename[=<pattern>]] [--series-id <id>]
                             [--dry-run] [--convert] [--delete-original | --keep-original]

        Looks the comic up on Metron (https://metron.cloud), or every comic in the folder, and
        writes what Metron says into its ComicInfo.xml: series, number, cover date, title,
        summary, credits, characters and more. Fields Metron has nothing for are left as they are.

        Which issue a comic is comes from its ComicInfo.xml, or else from its file name, as in
        "Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr". You are shown the series Metron
        has by that name, and choose one; --series-id gives Metron's id for it instead, and run
        from a script, a series is only taken when there is just one that fits.

        --rename names each comic by what it says once looked up, by the pattern
        {year}{month} - {series} {number:00}, as in "198405 - Secret Wars 01.cbz"; or by your own,
        as --rename="{series} {number:000} ({year})". The fields a pattern can have are {series},
        {number}, {title}, {year}, {month}, {day}, {volume} and {publisher}; :00 pads a number.

        Every change is shown before anything is done, and you are asked to go ahead; --dry-run
        shows them and stops. A CBR is converted to a CBZ to be changed, as tag always does, and
        what becomes of the original is asked once.

        Metron needs a free account: give your user name and password in METRON_USERNAME and
        METRON_PASSWORD (or an API token in METRON_TOKEN).
        """;

    /// <summary>A comic to look up, and the issue it is said to be.</summary>
    private sealed record Wanted(string Path, ComicInfoDocument? Document, string Series, string Number, int? Year);

    /// <summary>What is to be done to a comic: its new ComicInfo.xml and what changed in it, and where it is to end up.</summary>
    private sealed record Plan(string Path, ComicInfoDocument Document, List<string> Changed, string Target, bool Converts, string Heading);

    public static int Run(List<string> options, Terminal terminal, ITrash trash, Func<Metron> metron, ReadingPlaces places)
    {
        bool dryRun = options.Remove("--dry-run");
        bool convert = options.Remove("--convert");
        var original = Originals.Choice(options);
        var rename = TakeRename(options);
        int? seriesId = TakeSeriesId(options);
        if (options.FirstOrDefault(o => o.StartsWith("--", StringComparison.Ordinal)) is { } field)
        {
            throw new UsageException($"{field} is not a lookup option; with --lookup, the fields come from Metron. Change them afterwards with 'comicraft tag <comic> {field} <value>'.");
        }

        string path = Comic.Path(options, orFolder: true);
        var comics = Directory.Exists(path) ? ComicsIn(path) : [path];
        if (comics.Count == 0)
        {
            terminal.Out.WriteLine($"There are no comics in {System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path))} to look up.");
            return Exit.Done;
        }

        int failed = 0;
        var wanted = new List<Wanted>();
        foreach (string comic in comics)
        {
            if (Identify(comic, terminal) is { } w)
            {
                wanted.Add(w);
            }
            else
            {
                failed++;
            }
        }

        var groups = wanted.GroupBy(w => SeriesKey(w.Series)).ToList();
        if (seriesId is not null && groups.Count > 1)
        {
            throw new UsageException($"--series-id is for one series, and these comics are of {groups.Count}: {string.Join(", ", groups.Select(g => g.First().Series))}.");
        }

        var plans = new List<Plan>();
        try
        {
            var database = groups.Count > 0 ? metron() : null;
            foreach (var group in groups)
            {
                var found = Lookup(group.ToList(), seriesId, database!, rename, terminal, plans);
                failed += group.Count() - found;
            }
        }
        catch (MetronException ex)
        {
            terminal.Error(ex.Message);
            terminal.Out.WriteLine("Nothing was changed.");
            return Exit.CannotRun;
        }

        var todo = plans.Where(p => p.Changed.Count > 0 || p.Target != p.Path).ToList();
        int already = plans.Count - todo.Count;
        if (todo.Count == 0)
        {
            terminal.Out.WriteLine(plans.Count == 0 ? "Nothing was found to change." : "Every comic already says what Metron does; nothing was changed.");
            return failed > 0 ? Exit.CannotRun : Exit.Done;
        }

        Show(todo, already, terminal);
        if (dryRun)
        {
            terminal.Out.WriteLine("Nothing was changed (--dry-run).");
            return failed > 0 ? Exit.CannotRun : Exit.Done;
        }

        if (terminal.CanAsk && !terminal.Ask(todo.Count == 1 ? "Make these changes?" : $"Make these changes to {todo.Count} comics?"))
        {
            terminal.Out.WriteLine("Nothing was changed.");
            return Exit.CannotRun;
        }

        // what cannot be written is converted to a CBZ first, as tag always does, when the reader says so
        var converting = todo.Where(p => p.Converts).ToList();
        if (converting.Count > 0 && !convert)
        {
            string these = converting.Count == 1 ? $"{System.IO.Path.GetFileName(converting[0].Path)} is a CBR, which cannot be changed" : $"{converting.Count} of them are CBRs, which cannot be changed";
            if (!terminal.CanAsk)
            {
                terminal.Error($"{these}; give --convert to convert {(converting.Count == 1 ? "it" : "them")} to CBZs and change those.");
                todo.RemoveAll(p => p.Converts);
                failed += converting.Count;
            }
            else if (!terminal.Ask($"{these}. Convert {(converting.Count == 1 ? "it to a CBZ" : "them to CBZs")} and change {(converting.Count == 1 ? "that" : "those")}?"))
            {
                terminal.Out.WriteLine(converting.Count == 1 ? "It is left as it is." : "They are left as they are.");
                todo.RemoveAll(p => p.Converts);
            }
        }

        if (todo.Any(p => p.Converts) && original == Original.Ask)
        {
            original = terminal.CanAsk && terminal.Ask(trash.IsAvailable
                ? "Move each original CBR to the Trash once it is converted and checked?"
                : "Delete each original CBR permanently once it is converted and checked?")
                ? Original.Delete
                : Original.Keep;
        }

        int done = 0;
        foreach (var plan in todo)
        {
            try
            {
                Apply(plan, original, terminal, trash, places);
                done++;
            }
            catch (Exception ex) when (ex is ComicEditException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                terminal.Error($"{System.IO.Path.GetFileName(plan.Path)} could not be changed: {ex.Message}");
                failed++;
            }
        }

        if (comics.Count > 1)
        {
            var summary = new List<string> { $"{done} changed" };
            if (already > 0)
            {
                summary.Add($"{already} already right");
            }

            if (failed > 0)
            {
                summary.Add($"{failed} not");
            }

            terminal.Out.WriteLine($"Done: {string.Join(", ", summary)}.");
        }

        return failed > 0 ? Exit.CannotRun : Exit.Done;
    }

    /// <summary>The comics in the folder, not in the folders in it, that can be looked up, in the order a reader expects.</summary>
    private static List<string> ComicsIn(string folder)
    {
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(f => !System.IO.Path.GetFileName(f).StartsWith('.') && Shelf.IsComic(f))
            .Order(Comparer<string>.Create((a, b) => NaturalOrder.Instance.Compare(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b))))
            .ToList();
    }

    /// <summary>
    /// Which issue the comic is: as its ComicInfo.xml says, when that has the series and number,
    /// or else as its file name does. Null, having said why, for one that cannot be looked up.
    /// </summary>
    private static Wanted? Identify(string path, Terminal terminal)
    {
        string name = System.IO.Path.GetFileName(path);
        ComicInfoDocument? document;
        try
        {
            if (ComicArchive.KindOf(path) == ArchiveKind.Pdf)
            {
                terminal.Error($"{name} is a PDF, whose ComicInfo.xml cannot be changed; look up the CBZ it was made from. Skipped.");
                return null;
            }

            document = ComicArchive.ReadComicInfo(path);
        }
        catch (Exception ex) when (ex is ComicEditException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            terminal.Error($"{name} cannot be read: {ex.Message} Skipped.");
            return null;
        }

        var parsed = ComicName.Parse(path);
        string? series = document?.Get("Series") ?? parsed?.Series;
        string? number = document?.Get("Number") ?? parsed?.Number;
        if (series is null || number is null)
        {
            terminal.Error($"{name} does not say which issue it is: its ComicInfo.xml has no series and number, and none can be told from its name. Skipped.");
            return null;
        }

        int? year = int.TryParse(document?.Get("Year"), out int y) ? y : parsed?.Year;
        return new Wanted(path, document, series, number, year);
    }

    /// <summary>
    /// Finds the comics' series on Metron, and each comic in it by its number, and plans what is
    /// to be done to each. Returns how many were found.
    /// </summary>
    private static int Lookup(List<Wanted> comics, int? seriesId, Metron metron, NamePattern? rename, Terminal terminal, List<Plan> plans)
    {
        var first = comics[0];
        int? year = comics.Min(c => c.Year);
        string what = $"{first.Series}{(year is { } y ? $" ({y})" : "")}";
        MetronSeries? series = seriesId is { } id ? metron.Series(id) : Choose(what, comics, year, metron, terminal);
        if (series is null)
        {
            return 0;
        }

        terminal.Out.WriteLine($"Looking up {Count(comics.Count, "comic")} in {series.Display} on Metron.");
        var issues = new Dictionary<string, MetronIssueEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in metron.Issues(series.Id))
        {
            issues.TryAdd(ComicName.Key(issue.Number), issue);
        }

        int found = 0;
        var targets = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var comic in comics)
        {
            string name = System.IO.Path.GetFileName(comic.Path);
            if (!issues.TryGetValue(ComicName.Key(comic.Number), out var entry))
            {
                terminal.Error($"{series.Display} on Metron has no issue {comic.Number}, which {name} says it is. Skipped.");
                continue;
            }

            var issue = metron.Issue(entry.Id);
            plans.Add(PlanFor(comic, issue, series, rename, targets, terminal));
            found++;
        }

        return found;
    }

    /// <summary>
    /// The series the reader chooses of those Metron has that fit; or, from a script, the one
    /// that fits when only one does. Null, having said why, when there is none.
    /// </summary>
    private static MetronSeries? Choose(string what, List<Wanted> comics, int? year, Metron metron, Terminal terminal)
    {
        var words = Words(comics[0].Series);
        var candidates = Search(WordList(comics[0].Series), year, metron);
        if (candidates.Count == 0)
        {
            terminal.Error($"Metron has no series like {what}; give --series-id to say which it is. Skipped {Count(comics.Count, "comic")}.");
            return null;
        }

        int highest = comics.Max(c => int.TryParse(ComicName.Key(c.Number), out int n) ? n : 0);
        var ranked = candidates
            .Select(s => (Series: s, Score: Score(words, year, highest, s)))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Series.YearBegan)
            .Take(8)
            .ToList();

        if (!terminal.CanAsk)
        {
            // a script takes a series only when its name and year are the comics', and only one's are
            var exact = ranked.Where(c => Words(NameOf(c.Series)).SetEquals(words) && year is not null && c.Series.YearBegan == year).ToList();
            if (exact.Count == 1)
            {
                return exact[0].Series;
            }

            terminal.Error($"Metron has {Count(ranked.Count, "series", "series")} like {what}, and a script cannot choose; give --series-id with Metron's id for it ({string.Join("; ", ranked.Select(c => $"{c.Series.Id}: {c.Series.Display}"))}). Skipped {Count(comics.Count, "comic")}.");
            return null;
        }

        terminal.Out.WriteLine($"Metron has {(ranked.Count == 1 ? "this series" : "these series")} for {what}, {Count(comics.Count, "comic")}:");
        for (int i = 0; i < ranked.Count; i++)
        {
            var s = ranked[i].Series;
            var about = new List<string>();
            if (s.Publisher?.Name is { } publisher)
            {
                about.Add(publisher);
            }

            if (s.IssueCount is { } count)
            {
                about.Add(Count(count, "issue"));
            }

            about.Add(s.YearEnd is { } end && end != s.YearBegan ? $"{s.YearBegan}-{end}" : $"{s.YearBegan}");
            terminal.Out.WriteLine($"  {i + 1}. {s.Display}: {string.Join(", ", about)}  (id {s.Id})");
        }

        int chosen = terminal.Choose("Which is it?", ranked.Count);
        if (chosen == 0)
        {
            terminal.Out.WriteLine($"Skipped {Count(comics.Count, "comic")}.");
            return null;
        }

        return ranked[chosen - 1].Series;
    }

    /// <summary>
    /// The series on Metron like the name: those with all its words, begun in the year; or, with
    /// none, in any year; or, with none still, with fewer of its words, from the end of the name,
    /// where the name most often says what the series is ("Secret Wars", not "Marvel Super-Heroes").
    /// </summary>
    private static List<MetronSeries> Search(List<string> all, int? year, Metron metron)
    {
        for (int skip = 0; skip == 0 || all.Count - skip >= 2; skip++)
        {
            string query = string.Join(' ', all.Skip(skip));
            var found = metron.SearchSeries(query, year);
            if (found.Count == 0 && year is not null)
            {
                found = metron.SearchSeries(query, null);
            }

            if (found.Count > 0)
            {
                return [.. found];
            }

            if (all.Count < 2)
            {
                break;
            }
        }

        return [];
    }

    /// <summary>How well a series fits: the words its name shares with the comic's, its year, and having issues enough.</summary>
    private static double Score(HashSet<string> words, int? year, int highest, MetronSeries series)
    {
        var theirs = Words(NameOf(series));
        double shared = words.Count + theirs.Count == 0 ? 0 : (double)words.Intersect(theirs).Count() / words.Union(theirs).Count();
        double began = year is null ? 0 : series.YearBegan == year ? 1 : Math.Abs(series.YearBegan - year.Value) <= 1 ? 0.5 : 0;
        double enough = series.IssueCount is { } count && count >= highest ? 0.25 : 0;
        return shared * 2 + began + enough;
    }

    private static string NameOf(MetronSeries series) => series.Name ?? YearAtEnd().Replace(series.Display, "");

    /// <summary>The words of a name, as it is compared: in lower case, without punctuation, so that "Super-Heroes" is "super heroes".</summary>
    private static HashSet<string> Words(string name) => [.. WordList(name)];

    /// <summary>The words of a name in their order, as they are searched for.</summary>
    private static List<string> WordList(string name) => [.. NotWord().Split(name.ToLowerInvariant()).Where(w => w.Length > 0)];

    private static string SeriesKey(string series) => string.Join(' ', WordList(series));

    /// <summary>What is to be done to the comic, from what Metron says of the issue it is.</summary>
    private static Plan PlanFor(Wanted comic, MetronIssue issue, MetronSeries series, NamePattern? rename, HashSet<string> targets, Terminal terminal)
    {
        string name = System.IO.Path.GetFileName(comic.Path);
        var document = comic.Document ?? ComicInfoDocument.New();
        var changed = new List<string>();
        foreach (var (field, value) in IssueFields.Of(issue, series))
        {
            // the comic's own notes are its reader's, and are not written over
            if (field == "Notes" && document.Get("Notes") is { } notes && !notes.StartsWith(IssueFields.NotesStart, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                if (document.Set(field, value))
                {
                    changed.Add(field);
                }
            }
            catch (ComicEditException)
            {
                // a value ComicInfo cannot hold, such as a rating it has no name for, is left out
            }
        }

        bool converts = ComicArchive.KindOf(comic.Path) != ArchiveKind.Zip;
        string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(comic.Path))!;
        string target = converts ? System.IO.Path.ChangeExtension(comic.Path, ".cbz") : comic.Path;
        if (rename is not null)
        {
            if (rename.Format(document.Get) is { Length: > 0 } renamed)
            {
                target = System.IO.Path.Combine(folder, renamed + ".cbz");
            }
            else
            {
                terminal.Error($"{name} cannot be named by {rename}, since its ComicInfo.xml does not have every field the pattern has; it keeps its name.");
            }
        }

        if (!SamePath(target, comic.Path) && (File.Exists(target) || !targets.Add(System.IO.Path.GetFullPath(target))))
        {
            terminal.Error($"{name} would be named {System.IO.Path.GetFileName(target)}, but {(File.Exists(target) ? "that is already there" : "another comic here would be named that too")}; it keeps its name.");
            target = converts ? System.IO.Path.ChangeExtension(comic.Path, ".cbz") : comic.Path;
            if (converts && File.Exists(target))
            {
                throw new MetronException($"{name} would be converted to {System.IO.Path.GetFileName(target)}, which is already there; move it aside, or rename it, and look up again.");
            }
        }

        var info = document.Info;
        string heading = info.Heading ?? $"{issue.Series.Name} #{issue.Number}";
        return new Plan(comic.Path, document, changed, target, converts, heading);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>What is to be done to each comic: what it is found to be, what it is to be called, and what fields change.</summary>
    private static void Show(List<Plan> todo, int already, Terminal terminal)
    {
        terminal.Out.WriteLine();
        foreach (var plan in todo)
        {
            string name = System.IO.Path.GetFileName(plan.Path);
            string target = System.IO.Path.GetFileName(plan.Target);
            terminal.Out.WriteLine(name);
            terminal.Out.WriteLine($"  is {plan.Heading}");
            if (target != name)
            {
                terminal.Out.WriteLine($"  {(plan.Converts ? "converts to" : "becomes")} {target}");
            }

            if (plan.Changed.Count > 0)
            {
                terminal.Out.WriteLine($"  sets {string.Join(", ", plan.Changed)}");
            }
        }

        terminal.Out.WriteLine();
        if (already > 0)
        {
            terminal.Out.WriteLine($"{Count(already, "comic")} already {(already == 1 ? "says" : "say")} what Metron does, and {(already == 1 ? "is" : "are")} left as {(already == 1 ? "it is" : "they are")}.");
        }
    }

    /// <summary>Does what is planned: converts the comic when it must be, writes its ComicInfo.xml, and renames it; and moves where the reader was in it along with it.</summary>
    private static void Apply(Plan plan, Original original, Terminal terminal, ITrash trash, ReadingPlaces places)
    {
        string name = System.IO.Path.GetFileName(plan.Path);
        string target = System.IO.Path.GetFileName(plan.Target);
        if (plan.Converts)
        {
            string made = ComicArchive.ConvertToCbz(plan.Path, plan.Target);
            ComicArchive.WriteComicInfo(made, plan.Document);
            terminal.Out.WriteLine($"Converted {name} to {target}, and saved what Metron says in it.");
            if (Originals.Settle(plan.Path, made, original, terminal, trash, askIfRefused: false))
            {
                places.Move(plan.Path, made);
            }

            return;
        }

        if (plan.Changed.Count > 0)
        {
            ComicArchive.WriteComicInfo(plan.Path, plan.Document);
        }

        if (!SamePath(plan.Target, plan.Path))
        {
            File.Move(plan.Path, plan.Target, overwrite: false);
            places.Move(plan.Path, plan.Target);
            terminal.Out.WriteLine(plan.Changed.Count > 0 ? $"Saved {name} as {target}." : $"Renamed {name} to {target}.");
        }
        else
        {
            terminal.Out.WriteLine($"Saved {name}.");
        }
    }

    /// <summary>--rename, for the default pattern, or --rename=pattern; null for neither.</summary>
    private static NamePattern? TakeRename(List<string> options)
    {
        string? pattern = null;
        for (int i = options.Count - 1; i >= 0; i--)
        {
            if (options[i] == "--rename")
            {
                pattern ??= NamePattern.Default;
                options.RemoveAt(i);
            }
            else if (options[i].StartsWith("--rename=", StringComparison.Ordinal))
            {
                pattern = options[i]["--rename=".Length..];
                options.RemoveAt(i);
            }
        }

        try
        {
            return pattern is null ? null : NamePattern.Parse(pattern);
        }
        catch (FormatException ex)
        {
            throw new UsageException(ex.Message);
        }
    }

    private static int? TakeSeriesId(List<string> options)
    {
        int at = options.FindIndex(o => o == "--series-id" || o.StartsWith("--series-id=", StringComparison.Ordinal));
        if (at < 0)
        {
            return null;
        }

        string? value = options[at].Contains('=') ? options[at][(options[at].IndexOf('=') + 1)..] : at + 1 < options.Count ? options[at + 1] : null;
        options.RemoveRange(at, options[at].Contains('=') || value is null ? 1 : 2);
        return int.TryParse(value, out int id) && id > 0
            ? id
            : throw new UsageException("--series-id needs Metron's id for the series, a number, as it is shown when you are asked to choose.");
    }

    private static string Count(int count, string one, string? many = null) => $"{count} {(count == 1 ? one : many ?? one + "s")}";

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"\s*\(\d{4}\)\s*$")]
    private static partial Regex YearAtEnd();
}
