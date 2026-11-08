using ComiCraft.Comics;
using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Cli;

/// <summary>What becomes of a comic once it has been converted: asked about, kept, or deleted.</summary>
internal enum Original { Ask, Keep, Delete }

/// <summary>
/// The original of a converted comic: kept, unless the reader says to delete it, on the command
/// line or when asked. Whatever a comic is converted to, this is how its original is settled.
/// </summary>
internal static class Originals
{
    /// <summary>
    /// What the command line says to do with an original: --delete-original or --keep-original,
    /// or, with neither, ask. Takes the options it reads out of the list.
    /// </summary>
    public static Original Choice(List<string> options)
    {
        bool delete = options.Remove("--delete-original");
        bool keep = options.Remove("--keep-original");
        if (delete && keep)
        {
            throw new UsageException("give --delete-original or --keep-original, not both.");
        }

        return delete ? Original.Delete : keep ? Original.Keep : Original.Ask;
    }

    /// <summary>
    /// Deletes the original or keeps it, as the reader chose. Deleting it is moving it to the
    /// Trash, where the system has one ComiCraft can use, so that it can be undone; where it has
    /// none, the question says the original would be deleted for good. When the Trash will not
    /// take it, the original is never deleted for good unasked: the reader is asked, and a script
    /// keeps it. Either way, it only goes once the converted comic has been opened again and found
    /// to have the same pages: <paramref name="samePages"/> says whether it has, and, for a CBZ, which
    /// is the default, they are the same by number and by name. Where a folder's comics are
    /// converted, an original the Trash will not take is kept unasked, without <paramref name="askIfRefused"/>,
    /// so that the conversion goes on. Returns whether the original is gone.
    /// </summary>
    public static bool Settle(string original, string converted, Original choice, Terminal terminal, ITrash trash, Func<bool>? samePages = null, bool askIfRefused = true)
    {
        string name = Path.GetFileName(original);
        string question = trash.IsAvailable
            ? $"Move the original, {name}, to the Trash?"
            : $"Delete the original, {name}, permanently?";
        bool delete = choice switch
        {
            Original.Delete => true,
            Original.Keep => false,
            _ => terminal.CanAsk && terminal.Ask(question),
        };

        if (!delete)
        {
            string how = trash.IsAvailable ? "move it to the Trash" : "delete it";
            terminal.Out.WriteLine(choice == Original.Ask && !terminal.CanAsk
                ? $"Kept {name}; give --delete-original to {how}."
                : $"Kept {name}.");
            return false;
        }

        samePages ??= () => ComicBook.Open(original).PageNames.SequenceEqual(ComicBook.Open(converted).PageNames);
        if (!samePages())
        {
            terminal.Error($"{Path.GetFileName(converted)} does not have the same pages as {name}, so {name} is kept.");
            return false;
        }

        if (!trash.IsAvailable)
        {
            File.Delete(original);
            terminal.Out.WriteLine($"Deleted {name}.");
            return true;
        }

        var trashed = trash.Move(original);
        if (trashed.Moved)
        {
            terminal.Out.WriteLine($"Moved {name} to the Trash.");
            return true;
        }

        // a system that says it failed but took the file anyway is not to be believed about it
        if (!File.Exists(original))
        {
            terminal.Error($"{name} is gone, but the system did not say it went to the Trash: {trashed.Failure}");
            return true;
        }

        terminal.Error($"{name} could not be moved to the Trash: {trashed.Failure}");
        if (askIfRefused && terminal.CanAsk && terminal.Ask($"Delete {name} permanently instead?"))
        {
            File.Delete(original);
            terminal.Out.WriteLine($"Deleted {name}.");
            return true;
        }

        terminal.Out.WriteLine($"Kept {name}.");
        return false;
    }
}
