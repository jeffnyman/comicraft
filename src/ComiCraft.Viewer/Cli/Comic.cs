namespace ComiCraft.Viewer.Cli;

/// <summary>The comic a command is given.</summary>
internal static class Comic
{
    /// <summary>
    /// The one comic left among the arguments, once the command has taken its options: an error
    /// for anything else that looks like an option, for none, or for more than one.
    /// </summary>
    public static string Path(List<string> arguments)
    {
        if (arguments.FirstOrDefault(a => a.StartsWith('-')) is { } unknown)
        {
            throw new UsageException($"unknown option '{unknown}'.");
        }

        return arguments.Count switch
        {
            0 => throw new UsageException("no comic given."),
            > 1 => throw new UsageException("give one comic at a time."),
            _ => File.Exists(arguments[0]) ? arguments[0] : throw new FileNotFoundException($"'{arguments[0]}' was not found."),
        };
    }
}
