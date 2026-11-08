using ComiCraft.Comics;
using ComiCraft.Viewer.Platform;
using ComiCraft.Viewer.Sdl;

namespace ComiCraft.Viewer.Cli;

/// <summary>comicraft convert: makes a CBZ or a PDF of a comic, and then settles its original.</summary>
internal static class ConvertCommand
{
    public const string Usage = """
        Usage: comicraft convert <comic> [--format cbz | pdf] [--to <file>] [--delete-original | --keep-original]

        Makes a CBZ of the comic, or, with --format pdf, a PDF, beside it by the same name, or at
        the file --to names; a --to that ends in .pdf makes a PDF without --format. An existing
        file is never written over.

        A CBZ has everything that is in the comic. A PDF has its pages, a page of the PDF to each,
        and its ComicInfo.xml, which goes in as an attachment and gives the PDF its title and
        author. Nothing is lost: JPEG and PNG pages go in as they are, and WebP, GIF and BMP
        pages as their pixels.

        Afterwards, you are asked whether to move the original to the Trash; the answer is no
        unless it is yes. --delete-original and --keep-original answer before it is asked, as a
        script must. The original only goes once what was made has been read back and found to
        have its pages. An original the Trash will not take is only deleted permanently when you
        say so; a script keeps it.
        """;

    private enum Format { Cbz, Pdf }

    public static int Run(string[] args, Terminal terminal, ITrash trash, Func<ReadOnlyMemory<byte>, Pixels>? decode = null)
    {
        var options = args.ToList();
        if (options.Remove("--help") || options.Remove("-h"))
        {
            terminal.Out.WriteLine(Usage);
            return Exit.Done;
        }

        var original = Originals.Choice(options);
        string? target = Take(options, "--to", "the name of the file to make");
        var format = FormatOf(Take(options, "--format", "cbz or pdf"), target);
        string path = Comic.Path(options);
        string name = System.IO.Path.GetFileName(path);

        if (format == Format.Pdf)
        {
            var pdf = ComicPdf.ConvertToPdf(path, target, decode ?? PageDecoder.Decode);
            terminal.Out.WriteLine($"Converted {name} to {System.IO.Path.GetFileName(pdf.Path)} ({pdf.PageCount} pages).");
            Originals.Settle(path, pdf.Path, original, terminal, trash, () => ComicPdf.HasItsPages(pdf));
            return Exit.Done;
        }

        string converted = ComicArchive.ConvertToCbz(path, target);
        int pages = ComicBook.Open(converted).PageCount;
        terminal.Out.WriteLine($"Converted {name} to {System.IO.Path.GetFileName(converted)} ({pages} pages).");
        Originals.Settle(path, converted, original, terminal, trash);
        return Exit.Done;
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
