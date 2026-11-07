# ComiCraft

**ComiCraft** is a C# viewer for digital comics: CBZ (zip) and CBR (RAR) files. It shows a comic
a page at a time, either whole or fitted to the window's width and scrolled, and shows what the
comic's `ComicInfo.xml` says about it, when it has one.

A comic's kind is found from its contents, not its extension, so a CBR that is really a zip (or
the other way round) opens all the same. Its pages are the images in it (JPEG, PNG, GIF, WebP,
BMP) in the natural order of their names, so `page2.jpg` comes before `page10.jpg`.

## Reading

`comicraft <comic>` opens the comic in a window the shape of its first page.
`comicraft --help` lists the keys.

| Option | What it does |
|---|---|
| `--fullscreen` | Opens on the whole display. |

### Fitting the page

A comic page is taller than it is wide, and most screens are the other way round, so there are
two ways to show it. **W** switches between them; the title bar says when the page is fitted to
the width.

| Fit | What it does |
|---|---|
| Page | The whole page is seen at once, as large as fits. This is where a comic opens. |
| Width | The page fills the width of the window, and is scrolled down through. Panels and lettering are as large as the window allows. |

### Keys

| Key | What it does |
|---|---|
| Space, Page Down | Goes down a screen. At the bottom of a page, goes on to the top of the next. |
| Page Up | Goes up a screen. At the top of a page, goes back to the bottom of the one before. |
| Down, Up | Goes down or up a little. At the end of a page, goes on or back a page, as above. |
| Right | Goes to the next page, at its top. |
| Left, Backspace | Goes to the page before, at its top. |
| Home, End | Goes to the first page, or the last. |
| W | Fits the page whole, or to the width. |
| I | Shows the comic's details, or puts them away. |
| F, F11 | Switches between a window and the full screen. |
| Escape | Puts the details away when they are showing; quits otherwise. |
| Q | Quits. |

Where the whole page is seen, there is nothing to scroll, so every key that goes on or back
simply turns a page.

Space, Page Down, Page Up, Down and Up, held down, scroll to the end of the page and stop there,
and go on to the next page only when pressed again, so they never run on through pages unread.
Where the whole page is seen, they turn page after page when held, as Right and Left always do.

### Mouse

| Action | What it does |
|---|---|
| Wheel, or two fingers on a trackpad | Scrolls a page that is taller than the window. It never turns the page. |
| Click on the right half of the window | Goes forward, as Page Down does. |
| Click on the left half of the window | Goes back, as Page Up does. |
| Click with the details showing | Puts them away. |

### The comic's details

**I** shows a panel over the page with what is known about the comic: its publisher and date, who
wrote and drew it, who is in it, how many pages it has and the file it came from. The heading in
the title bar comes from the same place, such as *Operation: Knightstrike #3 – Enter... Bloodstrike
(July 1995)*.

All of this comes from the comic's `ComicInfo.xml`, the metadata file begun by ComicRack and kept
now by the [Anansi Project](https://anansi-project.github.io/docs/comicinfo/intro). Comics without
one, or with one that cannot be read, still open, and show their file name and their pages.

## Layout

| Path | What it is |
|---|---|
| `src/ComiCraft.Comics` | The comic library: opening archives, finding and ordering the pages, reading `ComicInfo.xml`, and the details and text wrapping the viewer shows. |
| `src/ComiCraft.Viewer` | The `comicraft` viewer, on SDL: the window, fitting and scrolling pages, keys and mouse, and the details panel. |
| `tests/ComiCraft.Comics.Tests` | Tests for the library, on small comics made in memory. |
| `tests/ComiCraft.Viewer.Tests` | Tests for the viewer's layout and its moving through a comic, without a window. |

Beyond the .NET SDK, the dependencies are [SharpCompress](https://github.com/adamhathcock/sharpcompress)
for the archives, and SDL3 (`ppy.SDL3-CS`), SDL3_image (`ppy.SDL3_image-CS`) and SDL3_ttf
(`ppy.SDL3_ttf-CS`) for the viewer's window, its pages and its text.

The viewer's text is set in [Inter](https://rsms.me/inter/), by Rasmus Andersson, built into the
viewer. Inter is licensed under the SIL Open Font License 1.1, which is in
`src/ComiCraft.Viewer/Fonts/OFL.txt` and is copied beside the viewer when it is built.

## Building and running

Needs the .NET 10 SDK.

```
dotnet build ComiCraft.slnx
dotnet test ComiCraft.slnx
dotnet run --project src/ComiCraft.Viewer -- <comic> [--fullscreen]
dotnet run --project src/ComiCraft.Viewer -- --help
```

Comics never go in the repository: `*.cbz` and `*.cbr` are ignored.
