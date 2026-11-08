namespace ComiCraft.Viewer.Cli;

/// <summary>What comicraft returns: done, not found, a mistake on the command line, or what was asked could not be done.</summary>
internal static class Exit
{
    public const int Done = 0, NotFound = 1, BadCommandLine = 2, CannotRun = 3;
}
