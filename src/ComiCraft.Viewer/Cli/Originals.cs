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
    /// to have the same pages, by number and by name.
    /// </summary>
    public static void Settle(string original, string converted, Original choice, Terminal terminal, ITrash trash)
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
            return;
        }

        if (!ComicBook.Open(original).PageNames.SequenceEqual(ComicBook.Open(converted).PageNames))
        {
            terminal.Error($"{Path.GetFileName(converted)} does not have the same pages as {name}, so {name} is kept.");
            return;
        }

        if (!trash.IsAvailable)
        {
            File.Delete(original);
            terminal.Out.WriteLine($"Deleted {name}.");
            return;
        }

        var trashed = trash.Move(original);
        if (trashed.Moved)
        {
            terminal.Out.WriteLine($"Moved {name} to the Trash.");
            return;
        }

        terminal.Error($"{name} could not be moved to the Trash: {trashed.Failure}");
        if (terminal.CanAsk && terminal.Ask($"Delete {name} permanently instead?"))
        {
            File.Delete(original);
            terminal.Out.WriteLine($"Deleted {name}.");
            return;
        }

        terminal.Out.WriteLine($"Kept {name}.");
    }
}
