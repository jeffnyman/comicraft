namespace ComiCraft.Viewer.Platform;

/// <summary>What became of a file sent to the Trash: where it went, or why it could not go.</summary>
internal readonly record struct Trashed(string? Into, string? Failure)
{
    public bool Moved => Failure is null;

    public static Trashed To(string? into) => new(into, null);

    public static Trashed Not(string why) => new(null, why);
}

/// <summary>
/// The system's Trash (the Recycle Bin, on Windows), where a file goes to be deleted in a way
/// that can be undone. A file that cannot go there is never deleted instead: the caller is told
/// why, and decides.
/// </summary>
internal interface ITrash
{
    /// <summary>Whether this system has a Trash ComiCraft can use.</summary>
    bool IsAvailable { get; }

    Trashed Move(string path);
}

internal static class Trash
{
    /// <summary>This system's Trash: macOS's, for now; elsewhere, none.</summary>
    public static ITrash ForThisSystem() => OperatingSystem.IsMacOS() ? new MacTrash() : new NoTrash();
}

/// <summary>A system ComiCraft does not yet have the Trash of.</summary>
internal sealed class NoTrash : ITrash
{
    public bool IsAvailable => false;

    public Trashed Move(string path) => Trashed.Not("ComiCraft cannot use the Trash on this system yet.");
}
