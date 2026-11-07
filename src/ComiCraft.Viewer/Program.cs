using ComiCraft.Comics;
using ComiCraft.Viewer.Sdl;

namespace ComiCraft.Viewer;

internal static class Program
{
    // exit codes: done, not found, a mistake on the command line, comic unreadable or no window
    private const int Done = 0, NotFound = 1, BadCommandLine = 2, CannotRun = 3;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            PrintHelp();
            return args.Length == 0 ? BadCommandLine : Done;
        }

        string? path = null;
        bool fullscreen = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--fullscreen":
                    fullscreen = true;
                    break;
                case { } option when option.StartsWith('-'):
                    Error($"unknown option '{option}'.");
                    return BadCommandLine;
                default:
                    if (path is not null)
                    {
                        Error("give one comic at a time.");
                        return BadCommandLine;
                    }

                    path = arg;
                    break;
            }
        }

        if (path is null)
        {
            Error("no comic given.");
            return BadCommandLine;
        }

        if (!File.Exists(path))
        {
            Error($"'{path}' was not found.");
            return NotFound;
        }

        ComicBook comic;
        try
        {
            comic = ComicBook.Open(path);
        }
        catch (Exception ex)
        {
            Error($"'{path}' could not be read as a comic: {ex.Message}");
            return CannotRun;
        }

        if (comic.PageCount == 0)
        {
            Error($"'{path}' has no pages.");
            return CannotRun;
        }

        try
        {
            using var window = new ComicWindow(comic, fullscreen);
            window.Run();
        }
        catch (SdlException ex)
        {
            Error(ex.Message);
            return CannotRun;
        }

        return Done;
    }

    private static void Error(string message) => Console.Error.WriteLine($"comicraft: {message}");

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Usage: comicraft [--fullscreen] <comic.cbz | comic.cbr>

            Keys:
              Space, Page Down                 down a screen, then on to the next page
              Page Up                          up a screen, then back to the page before
              Down, Up                         down or up a little, then on or back a page
              Right, Left, Backspace           next page, previous page
              Home, End                        first page, last page
              W                                fit the page whole, or to the width
              I                                comic details on and off
              F, F11                           full screen on and off
              Escape                           put details away, or quit
              Q                                quit

            The mouse wheel scrolls a page that is taller than the window. A click on
            the right half of the window goes forward, as Page Down does, and on the
            left half back; with the details showing, a click puts them away.

            Where a page fits the window, every key that goes on or back turns a page.
            """);
    }
}
