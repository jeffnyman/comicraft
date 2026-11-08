namespace ComiCraft.Viewer.Cli;

/// <summary>A mistake on the command line, said in one line.</summary>
internal sealed class UsageException(string message) : Exception(message);
