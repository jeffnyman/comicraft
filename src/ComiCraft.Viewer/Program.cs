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
                "forget" => ForgetCommand.Run(args[1..], terminal, ReadingPlaces.ForThisUser()),
                _ => View(args),
            };
        }
        catch (UsageException ex)
        {
            terminal.Error(ex.Message);
            Console.Error.WriteLine($"Run 'comicraft {(args.FirstOrDefault() is "tag" or "convert" or "forget" ? args[0] + " " : "")}--help' for more.");
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

    /// <summary>comicraft &lt;comic | folder&gt;: the comic, in a window, with its folder's comics a key away; or a folder's comics, to choose one.</summary>
    private static int View(string[] args)
    {
        var options = args.ToList();
        if (options.Count == 0 || options.Remove("--help") || options.Remove("-h"))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? Exit.BadCommandLine : Exit.Done;
        }

        bool fullscreen = options.Remove("--fullscreen");
        string path = Comic.Path(options, orFolder: true);
        if (Directory.Exists(path))
        {
            return Show(null, null, path, fullscreen);
        }

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

        return Show(comic, path, Path.GetDirectoryName(Path.GetFullPath(path))!, fullscreen);
    }

    private static int Show(ComicBook? comic, string? path, string folder, bool fullscreen)
    {
        try
        {
            using var window = new ComicWindow(comic, path, folder, fullscreen, ReadingPlaces.ForThisUser());
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
        Usage: comicraft [--fullscreen] <comic.cbz | comic.cbr | comic.pdf | folder>
               comicraft tag <comic> [--<field> <value>...]         its ComicInfo.xml
               comicraft convert <comic | folder> [--format pdf]    a CBZ or PDF of it
               comicraft forget <comic> | --all                     where you were in it

        A comic opens where you left it, on its page, fitted and zoomed as it was.
        Given a folder, ComiCraft shows the comics in it, to choose one to read.
        'comicraft tag --help', 'convert --help' and 'forget --help' say more.

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
          L, Tab                the comics in the folder on and off
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

        In the comics in the folder, Up and Down (and Page Up, Page Down, Home and
        End) choose one; Enter or Right opens it, or goes into a folder, and Left or
        Backspace goes up a folder. A click opens what it is on. L, Tab or Escape
        puts them away, and the comic being read is shown in gold.
        """;
}
