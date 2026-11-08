using ComiCraft.Comics;

namespace ComiCraft.Viewer.Cli;

/// <summary>comicraft convert: makes a CBZ of a comic, and then settles its original.</summary>
internal static class ConvertCommand
{
    public const string Usage = """
        Usage: comicraft convert <comic> [--to <file.cbz>] [--delete-original | --keep-original]

        Makes a CBZ of the comic, with everything that is in it, beside it by the same name,
        or at the file --to names. An existing file is never written over.

        Afterwards, you are asked whether to delete the original; the answer is no unless it is
        yes. --delete-original and --keep-original answer before it is asked, as a script must.
        """;

    public static int Run(string[] args, Terminal terminal)
    {
        var options = args.ToList();
        if (options.Remove("--help") || options.Remove("-h"))
        {
            terminal.Out.WriteLine(Usage);
            return Exit.Done;
        }

        var original = Originals.Choice(options);
        string? target = null;
        int to = options.IndexOf("--to");
        if (to >= 0)
        {
            if (to + 1 >= options.Count)
            {
                throw new UsageException("--to needs the name of the CBZ to make.");
            }

            target = options[to + 1];
            options.RemoveRange(to, 2);
        }

        string path = Comic.Path(options);
        string converted = ComicArchive.ConvertToCbz(path, target);
        int pages = ComicBook.Open(converted).PageCount;
        terminal.Out.WriteLine($"Converted {System.IO.Path.GetFileName(path)} to {System.IO.Path.GetFileName(converted)} ({pages} pages).");
        Originals.Settle(path, converted, original, terminal);
        return Exit.Done;
    }
}
