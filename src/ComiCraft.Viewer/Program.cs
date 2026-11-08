using ComiCraft.Comics;
using ComiCraft.Viewer.Cli;
using ComiCraft.Viewer.Platform;
using ComiCraft.Viewer.Sdl;

namespace ComiCraft.Viewer;

internal static class Program
{
    private static int Main(string[] args)
    {
        var terminal = Terminal.Console;
        try
        {
            return args.FirstOrDefault() switch
            {
                "tag" => TagCommand.Run(args[1..], terminal, Trash.ForThisSystem()),
                "convert" => ConvertCommand.Run(args[1..], terminal, Trash.ForThisSystem()),
                _ => View(args),
            };
        }
        catch (UsageException ex)
        {
            terminal.Error(ex.Message);
            Console.Error.WriteLine($"Run 'comicraft {(args.FirstOrDefault() is "tag" or "convert" ? args[0] + " " : "")}--help' for more.");
            return Exit.BadCommandLine;
        }
        catch (FileNotFoundException ex)
        {
            terminal.Error(ex.Message);
            return Exit.NotFound;
        }
        catch (Exception ex) when (ex is ComicEditException or IOException or UnauthorizedAccessException)
        {
            terminal.Error(ex.Message);
            return Exit.CannotRun;
        }
    }

    /// <summary>comicraft &lt;comic&gt;: the comic, in a window.</summary>
    private static int View(string[] args)
    {
        var options = args.ToList();
        if (options.Count == 0 || options.Remove("--help") || options.Remove("-h"))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? Exit.BadCommandLine : Exit.Done;
        }

        bool fullscreen = options.Remove("--fullscreen");
        string path = Comic.Path(options);

        ComicBook comic;
        try
        {
            comic = ComicBook.Open(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"comicraft: '{path}' could not be read as a comic: {ex.Message}");
            return Exit.CannotRun;
        }

        if (comic.PageCount == 0)
        {
            Console.Error.WriteLine($"comicraft: '{path}' has no pages.");
            return Exit.CannotRun;
        }

        try
        {
            using var window = new ComicWindow(comic, fullscreen);
            window.Run();
        }
        catch (SdlException ex)
        {
            Console.Error.WriteLine($"comicraft: {ex.Message}");
            return Exit.CannotRun;
        }

        return Exit.Done;
    }

    private const string Usage = """
        Usage: comicraft [--fullscreen] <comic.cbz | comic.cbr>
               comicraft tag <comic> [--<field> <value>...]         its ComicInfo.xml
               comicraft convert <comic | folder> [--format pdf]    a CBZ or PDF of it

        'comicraft tag --help' and 'comicraft convert --help' say more about each.

        Keys:
          Space, Page Down      a screen across or down, then the next page
          Page Up               a screen back across or up, then the page before
          Down, Up              down or up a little, then on or back a page
          Right, Left           a little across a wide page, else turn the page
          Backspace             previous page
          Home, End             first page, last page
          W                     fit the page whole, or to the width
          +, -  (or =)          zoom in, zoom out
          0                     the whole page again
          I                     comic details on and off
          F, F11                full screen on and off
          Escape                put details away, or quit
          Q                     quit

        The mouse wheel scrolls a page that is larger than the window, across and
        down; with Ctrl, or as a pinch on a trackpad, it zooms at the pointer. A
        drag moves the page. A click on the right half of the window goes forward,
        as Page Down does, and on the left half back; with the details showing, a
        click puts them away.

        A page larger than the window is read across, then down. Where a page fits
        the window, every key that goes on or back turns a page.
        """;
}
