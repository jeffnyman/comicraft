using ComiCraft.Comics;
using ComiCraft.Viewer.Platform;
using ComiCraft.Viewer.Sdl;

namespace ComiCraft.Viewer.Cli;

/// <summary>
/// comicraft convert: makes a CBZ or a PDF of a comic, or of every comic in a folder, and then
/// settles the originals.
/// </summary>
internal static class ConvertCommand
{
    public const string Usage = """
        Usage: comicraft convert <comic> [--format cbz | pdf] [--to <file>] [--delete-original | --keep-original]
               comicraft convert <folder> [--format cbz | pdf] [--recursive] [--delete-original | --keep-original]

        Makes a CBZ of the comic, or, with --format pdf, a PDF, beside it by the same name, or at
        the file --to names; a --to that ends in .pdf makes a PDF without --format. An existing
        file is never written over.

        A CBZ has everything that is in the comic. A PDF has its pages, a page of the PDF to each,
        and its ComicInfo.xml, which goes in as an attachment and gives the PDF its title and
        author. Nothing is lost: JPEG and PNG pages go in as they are, and WebP, GIF and BMP
        pages as their pixels.

        Given a folder, converts every CBR in it to a CBZ, or, with --format pdf, every CBR and
        CBZ to a PDF, each beside itself; with --recursive, those in the folders in it too. A
        comic whose CBZ or PDF is already there is skipped, and one that cannot be converted is
        reported, and the rest go on. Ctrl+C stops once the comic being converted is done.

        Afterwards, you are asked whether to move the original to the Trash (for a folder, once,
        before any is converted); the answer is no unless it is yes. --delete-original and
        --keep-original answer before it is asked, as a script must. An original only goes once
        what was made has been read back and found to have its pages. An original the Trash will
        not take is only deleted permanently when you say so; a script, or a folder, keeps it.
        """;

    private enum Format { Cbz, Pdf }

    /// <summary>What a conversion made: where, how many pages it has, and how to check it has the original's.</summary>
    private readonly record struct Made(string Path, int Pages, Func<bool>? SamePages);

    public static int Run(string[] args, Terminal terminal, ITrash trash, Func<ReadOnlyMemory<byte>, Pixels>? decode = null)
    {
        var options = args.ToList();
        if (options.Remove("--help") || options.Remove("-h"))
        {
            terminal.Out.WriteLine(Usage);
            return Exit.Done;
        }

        var original = Originals.Choice(options);
        bool recursive = options.Remove("--recursive");
        string? target = Take(options, "--to", "the name of the file to make");
        var format = FormatOf(Take(options, "--format", "cbz or pdf"), target);
        string path = Comic.Path(options, orFolder: true);
        decode ??= PageDecoder.Decode;

        if (Directory.Exists(path))
        {
            if (target is not null)
            {
                throw new UsageException("--to names one file; a folder's comics are each converted beside themselves.");
            }

            return Folder(path, format, recursive, original, terminal, trash, decode);
        }

        if (recursive)
        {
            throw new UsageException("--recursive is for converting a folder.");
        }

        var made = Make(path, target, format, decode);
        terminal.Out.WriteLine($"Converted {System.IO.Path.GetFileName(path)} to {System.IO.Path.GetFileName(made.Path)} ({made.Pages} pages).");
        Originals.Settle(path, made.Path, original, terminal, trash, made.SamePages);
        return Exit.Done;
    }

    private static Made Make(string path, string? target, Format format, Func<ReadOnlyMemory<byte>, Pixels> decode)
    {
        if (format == Format.Pdf)
        {
            var pdf = ComicPdf.ConvertToPdf(path, target, decode);
            return new Made(pdf.Path, pdf.PageCount, () => ComicPdf.HasItsPages(pdf));
        }

        string converted = ComicArchive.ConvertToCbz(path, target);
        return new Made(converted, ComicBook.Open(converted).PageCount, null);
    }

    /// <summary>
    /// Converts every comic of the kinds the format is made from, in the folder (and its folders,
    /// when recursive), one after another. What becomes of the originals is asked once, before any
    /// is converted, so that the rest needs no one there.
    /// </summary>
    private static int Folder(string folder, Format format, bool recursive, Original original, Terminal terminal, ITrash trash, Func<ReadOnlyMemory<byte>, Pixels> decode)
    {
        string[] from = format == Format.Pdf ? [".cbr", ".cbz"] : [".cbr"];
        string to = format == Format.Pdf ? ".pdf" : ".cbz";
        string kinds = format == Format.Pdf ? "CBRs or CBZs" : "CBRs";
        string made = format == Format.Pdf ? "PDFs" : "CBZs";
        string where = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder)));

        var comics = ComicsIn(folder, from, recursive);
        if (comics.Count == 0)
        {
            terminal.Out.WriteLine($"There are no {kinds} in {where}{(recursive ? " or the folders in it" : "")} to convert.");
            return Exit.Done;
        }

        terminal.Out.WriteLine($"{Count(comics.Count, "comic")} in {where} to convert to {made}.");
        bool hint = false;
        if (original == Original.Ask)
        {
            hint = !terminal.CanAsk;
            original = terminal.CanAsk && terminal.Ask(trash.IsAvailable
                ? "Move each original to the Trash once it is converted and checked?"
                : "Delete each original permanently once it is converted and checked?")
                ? Original.Delete
                : Original.Keep;
        }

        int converted = 0, skipped = 0, failed = 0, gone = 0, left = 0;
        bool stopping = false;
        ConsoleCancelEventHandler stop = (_, press) =>
        {
            if (!stopping)
            {
                // the first Ctrl+C lets the comic being converted finish; a second stops at once
                press.Cancel = true;
                stopping = true;
                terminal.Error("stopping once this comic is converted; Ctrl+C again stops at once.");
            }
        };

        Console.CancelKeyPress += stop;
        try
        {
            for (int i = 0; i < comics.Count; i++)
            {
                if (stopping)
                {
                    left = comics.Count - i;
                    break;
                }

                string comic = comics[i];
                string name = System.IO.Path.GetRelativePath(folder, comic);
                string counter = $"[{i + 1}/{comics.Count}]";
                string target = System.IO.Path.ChangeExtension(comic, to);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    terminal.Out.WriteLine($"{counter} Skipped {name}: {System.IO.Path.GetFileName(target)} is already there.");
                    skipped++;
                    continue;
                }

                Made result;
                try
                {
                    result = Make(comic, null, format, decode);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    terminal.Error($"{counter} {name} could not be converted: {ex.Message}");
                    failed++;
                    continue;
                }

                terminal.Out.WriteLine($"{counter} Converted {name} to {System.IO.Path.GetFileName(result.Path)} ({result.Pages} pages).");
                converted++;
                if (original == Original.Delete &&
                    Originals.Settle(comic, result.Path, Original.Delete, terminal, trash, result.SamePages, askIfRefused: false))
                {
                    gone++;
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= stop;
        }

        var summary = new List<string> { $"Converted {converted} of {comics.Count}" };
        if (skipped > 0)
        {
            summary.Add($"skipped {skipped}, already converted");
        }

        if (failed > 0)
        {
            summary.Add($"{failed} could not be");
        }

        if (left > 0)
        {
            summary.Add($"stopped with {left} left");
        }

        if (gone > 0)
        {
            summary.Add($"{(trash.IsAvailable ? "moved" : "deleted")} {Count(gone, "original")}{(trash.IsAvailable ? " to the Trash" : "")}");
        }

        terminal.Out.WriteLine(string.Join("; ", summary) + ".");
        if (hint && converted > 0)
        {
            terminal.Out.WriteLine($"Kept the originals; give --delete-original to {(trash.IsAvailable ? "move them to the Trash" : "delete them")}.");
        }

        return failed > 0 || left > 0 ? Exit.CannotRun : Exit.Done;
    }

    private static string Count(int count, string thing) => count == 1 ? $"1 {thing}" : $"{count} {thing}s";

    /// <summary>
    /// The comics in the folder with one of the extensions, and, when recursive, in the folders in
    /// it, in the natural order of their paths. Hidden files and folders are passed over (a Trash
    /// is one: .Trash-1000, $RECYCLE.BIN), and so are links to folders, which may lead in a circle.
    /// </summary>
    private static List<string> ComicsIn(string folder, string[] extensions, bool recursive)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true,
        };

        var found = new List<string>();
        void Look(string at)
        {
            found.AddRange(Directory.EnumerateFiles(at, "*", options).Where(file =>
                !System.IO.Path.GetFileName(file).StartsWith('.') &&
                extensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)));
            if (recursive)
            {
                foreach (string inner in Directory.EnumerateDirectories(at, "*", options))
                {
                    if (!System.IO.Path.GetFileName(inner).StartsWith('.') && new DirectoryInfo(inner).LinkTarget is null)
                    {
                        Look(inner);
                    }
                }
            }
        }

        Look(folder);
        found.Sort((a, b) => NaturalOrder.Instance.Compare(System.IO.Path.GetRelativePath(folder, a), System.IO.Path.GetRelativePath(folder, b)));
        return found;
    }

    /// <summary>
    /// The format asked for: as --format says, or, without it, as the --to file's extension does,
    /// and a CBZ when neither does. The two must not say different things.
    /// </summary>
    private static Format FormatOf(string? asked, string? target)
    {
        Format? named = System.IO.Path.GetExtension(target)?.ToLowerInvariant() switch
        {
            ".pdf" => Format.Pdf,
            ".cbz" => Format.Cbz,
            _ => null,
        };

        Format? given = asked?.ToLowerInvariant() switch
        {
            null => null,
            "cbz" => Format.Cbz,
            "pdf" => Format.Pdf,
            _ => throw new UsageException($"--format is cbz or pdf, not '{asked}'."),
        };

        if (given is { } g && named is { } n && g != n)
        {
            throw new UsageException($"--format {asked} makes a {g.ToString().ToUpperInvariant()}, but --to names a {n.ToString().ToUpperInvariant()}.");
        }

        return given ?? named ?? Format.Cbz;
    }

    /// <summary>The value of an option given as --option value or --option=value, taken out of the options; null when it is not given.</summary>
    private static string? Take(List<string> options, string option, string what)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].StartsWith(option + "=", StringComparison.Ordinal))
            {
                string value = options[i][(option.Length + 1)..];
                options.RemoveAt(i);
                return value;
            }

            if (options[i] == option)
            {
                if (i + 1 >= options.Count)
                {
                    throw new UsageException($"{option} needs {what}.");
                }

                string value = options[i + 1];
                options.RemoveRange(i, 2);
                return value;
            }
        }

        return null;
    }
}
