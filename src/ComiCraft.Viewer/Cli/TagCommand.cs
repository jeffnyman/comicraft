using ComiCraft.Comics;
using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Cli;

/// <summary>
/// comicraft tag: shows a comic's ComicInfo.xml, or changes its fields. Every value is checked
/// before anything is done, so a mistake in one leaves the comic as it was. A CBR cannot be
/// written, so it is converted to a CBZ first, when the reader says so (--convert, or yes when
/// asked), and its original is then settled as a conversion's always is.
/// </summary>
internal static class TagCommand
{
    public static string Usage => $"""
        Usage: comicraft tag <comic>
               comicraft tag <comic> --<field> <value>... [--convert] [--delete-original | --keep-original]

        With no fields, shows the comic's ComicInfo.xml. With fields, changes them: a field given
        an empty value ("") is removed. Field names are ComicInfo's, in any case, with or without
        hyphens: --series, --cover-artist, --CoverArtist. --field=value works too.

        A CBR cannot be changed, only converted to a CBZ that can. You are asked whether to
        convert it, unless --convert is given; and then whether to move the original to the
        Trash, unless --delete-original or --keep-original is. Run from a script, nothing is
        asked: a CBR is not converted without --convert, and an original is kept without
        --delete-original. An original the Trash will not take is only deleted permanently
        when you say so; a script keeps it.

        Fields:
        {Fields}
        """;

    /// <summary>The fields, as many to a line as fit the help's width, indented under its heading.</summary>
    private static string Fields =>
        string.Join("\n", TextWrap.Wrap(string.Join(", ", ComicInfoField.All.Where(f => f.Name != "Pages").Select(f => f.Name)) + ".", 86, s => s.Length)
            .Select(line => "  " + line));

    /// <summary>A field to change, and what to: null removes it.</summary>
    private readonly record struct Change(ComicInfoField Field, string? Value);

    public static int Run(string[] args, Terminal terminal, ITrash trash)
    {
        var options = args.ToList();
        if (options.Remove("--help") || options.Remove("-h"))
        {
            terminal.Out.WriteLine(Usage);
            return Exit.Done;
        }

        bool convert = options.Remove("--convert");
        var original = Originals.Choice(options);
        var changes = TakeChanges(options);
        string path = Comic.Path(options);

        if (changes.Count == 0)
        {
            if (convert || original != Original.Ask)
            {
                throw new UsageException("--convert, --delete-original and --keep-original are for changing fields; give some to change.");
            }

            Show(path, terminal);
            return Exit.Done;
        }

        // the changes are made to the document first, read from the comic as it is, a CBR too: so
        // a ComicInfo.xml that cannot be read, or changes that change nothing, convert nothing
        var document = ComicArchive.ReadComicInfo(path) ?? ComicInfoDocument.New();
        var changed = new List<(string Field, string? Was, string? Now)>();
        foreach (var change in changes)
        {
            string? was = document.Get(change.Field.Name);
            if (document.Set(change.Field.Name, change.Value))
            {
                changed.Add((change.Field.Name, was, document.Get(change.Field.Name)));
            }
        }

        string name = System.IO.Path.GetFileName(path);
        if (changed.Count == 0)
        {
            terminal.Out.WriteLine($"{name} already says that; it is left as it is.");
            return Exit.Done;
        }

        string writable = path;
        if (ComicArchive.KindOf(path) == ArchiveKind.Rar)
        {
            string cbz = System.IO.Path.ChangeExtension(path, ".cbz");
            if (!convert)
            {
                if (!terminal.CanAsk)
                {
                    terminal.Error($"{name} is a CBR, which cannot be changed. Give --convert to convert it to a CBZ and change that.");
                    return Exit.CannotRun;
                }

                if (!terminal.Ask($"{name} is a CBR, which cannot be changed. Convert it to a CBZ, {System.IO.Path.GetFileName(cbz)}, and change that?"))
                {
                    terminal.Out.WriteLine("Nothing was changed.");
                    return Exit.CannotRun;
                }
            }

            writable = ComicArchive.ConvertToCbz(path, cbz);
            terminal.Out.WriteLine($"Converted {name} to {System.IO.Path.GetFileName(writable)}.");
        }

        ComicArchive.WriteComicInfo(writable, document);
        int width = changed.Max(c => c.Field.Length);
        foreach (var (field, was, now) in changed)
        {
            terminal.Out.WriteLine($"  {field.PadRight(width)}  {was ?? "(none)"} -> {now ?? "(removed)"}");
        }

        terminal.Out.WriteLine($"Saved {System.IO.Path.GetFileName(writable)}.");

        if (writable != path)
        {
            Originals.Settle(path, writable, original, terminal, trash);
        }

        return Exit.Done;
    }

    /// <summary>
    /// Takes every --field value (and --field=value) out of the options, each checked as it is
    /// taken, so that a mistake is found before the comic is touched.
    /// </summary>
    private static List<Change> TakeChanges(List<string> options)
    {
        var changes = new List<Change>();
        for (int i = 0; i < options.Count;)
        {
            string option = options[i];
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            string name = option[2..];
            string? value;
            int taken;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                (name, value, taken) = (name[..equals], name[(equals + 1)..], 1);
            }
            else if (i + 1 < options.Count)
            {
                (value, taken) = (options[i + 1], 2);
            }
            else
            {
                throw new UsageException($"{option} needs a value; give \"\" to remove the field.");
            }

            try
            {
                var field = ComicInfoField.Find(name.Replace("-", ""));
                changes.Add(new Change(field, field.Normalize(value)));
            }
            catch (ComicEditException ex)
            {
                throw new UsageException(ex.Message);
            }

            options.RemoveRange(i, taken);
        }

        return changes;
    }

    /// <summary>The comic's heading, then each field its ComicInfo.xml has, in the schema's order.</summary>
    private static void Show(string path, Terminal terminal)
    {
        var document = ComicArchive.ReadComicInfo(path);
        if (document is null)
        {
            terminal.Out.WriteLine($"{System.IO.Path.GetFileName(path)} has no ComicInfo.xml.");
            return;
        }

        terminal.Out.WriteLine(document.Info.Heading ?? System.IO.Path.GetFileNameWithoutExtension(path));
        var fields = ComicInfoField.All
            .Where(f => f.Name != "Pages")
            .Select(f => (f.Name, Value: document.Get(f.Name)))
            .Where(f => f.Value is not null)
            .ToList();
        if (fields.Count == 0)
        {
            terminal.Out.WriteLine("  (its ComicInfo.xml has no fields)");
            return;
        }

        int width = fields.Max(f => f.Name.Length);
        foreach (var (name, value) in fields)
        {
            // a value of more than one line, as a summary may be, is kept in line under itself
            string indent = new(' ', width + 4);
            terminal.Out.WriteLine($"  {name.PadRight(width)}  {value!.ReplaceLineEndings($"\n{indent}")}");
        }
    }
}
