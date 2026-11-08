namespace ComiCraft.Viewer.Cli;

/// <summary>comicraft forget: forgets where the reader was in a comic, or in every comic, so it opens at its first page again.</summary>
internal static class ForgetCommand
{
    public static string Usage(ReadingPlaces places) => $"""
        Usage: comicraft forget <comic>
               comicraft forget --all

        A comic opens where you left it: at the page you were on, fitted, zoomed and scrolled as
        it was. forget forgets where you were in the comic, which then opens at its first page,
        as it did the first time; the comic need not be there any more. --all forgets every comic.

        Where you were is kept in {places.File},
        for the last {ReadingPlaces.MostComics} comics read. Nothing else is kept there.
        """;

    public static int Run(string[] args, Terminal terminal, ReadingPlaces places)
    {
        var options = args.ToList();
        if (options.Remove("--help") || options.Remove("-h"))
        {
            terminal.Out.WriteLine(Usage(places));
            return Exit.Done;
        }

        bool all = options.Remove("--all");
        if (options.FirstOrDefault(a => a.StartsWith('-')) is { } unknown)
        {
            throw new UsageException($"unknown option '{unknown}'.");
        }

        if (all)
        {
            if (options.Count > 0)
            {
                throw new UsageException("--all forgets every comic; give it no comic.");
            }

            int count = places.ForgetAll();
            terminal.Out.WriteLine(count switch
            {
                0 => "No comic was remembered.",
                1 => "Forgot where you were in 1 comic.",
                _ => $"Forgot where you were in {count} comics.",
            });
            return Exit.Done;
        }

        // a comic that has been moved or deleted can still be forgotten, so it need not be there
        string comic = options.Count switch
        {
            0 => throw new UsageException("no comic given; give one, or --all to forget every comic."),
            > 1 => throw new UsageException("give one comic at a time, or --all to forget every comic."),
            _ => options[0],
        };

        string name = Path.GetFileName(comic);
        terminal.Out.WriteLine(places.Forget(comic)
            ? $"Forgot where you were in {name}; it opens at its first page."
            : $"{name} was not remembered; it opens at its first page.");
        return Exit.Done;
    }
}
