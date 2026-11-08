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

## Changing a comic's details

`comicraft tag <comic>` shows the fields of the comic's `ComicInfo.xml`. Given fields, it
changes them, and says what it changed:

```
comicraft tag "Troll 001.cbz" --series Troll --number 1 --year 1993 --publisher Image
comicraft tag "Troll 001.cbz" --cover-artist "Rob Liefeld"
comicraft tag "Troll 001.cbz" --notes ""
```

A field's name is its name in ComicInfo, in any case, with or without hyphens (`--coverartist`,
`--cover-artist`, `--CoverArtist`), and `--field=value` works as well as `--field value`. An empty
value (`""`) removes the field. `comicraft tag --help` lists every field.

Each value is checked before anything is done, so a mistake (a month of 13, a field that does not
exist) leaves the comic as it was. A change that changes nothing does not write the file. Only the
fields given change: everything else in `ComicInfo.xml` is kept, including what ComiCraft does not
read itself, such as the page list or a tagger's own elements. The comic's pages are copied as they
are, and the comic is replaced in one step, so it is never left half written. A comic that is
read-only is left alone.

### CBRs

A CBR is a RAR archive, and RAR can be read by free software but not written. To change a CBR,
ComiCraft converts it to a CBZ beside it, with everything that was in it, and changes that. It asks
before converting, unless `--convert` is given, and then asks whether to move the original to the
Trash. The answer is no unless it is yes, and the original only goes once the CBZ has been opened
again and found to have the same pages.

On macOS the original goes to the Trash of the drive it is on, as it would from Finder. On Linux
it goes to the Trash as the [freedesktop.org Trash Specification](https://specifications.freedesktop.org/trash/latest/)
lays it out, which GNOME, KDE and tools such as `trash-cli` all read: the home trash
(`~/.local/share/Trash`) for a file on the same file system as the home, and that file system's
own (`.Trash-<uid>` at its top) for one on another, as a Windows drive under `/mnt` is in WSL. On
Windows it goes to the Recycle Bin of the drive it is on, as it would from Explorer. Windows deletes
a file permanently, and quietly, where it cannot recycle it (a drive with no Recycle Bin, one set
not to keep deleted files, a file too large for the Bin), so ComiCraft asks Windows first whether it
means to recycle the file, and stops it when it does not.

On every system, an original the Trash will not take is only deleted permanently when you say so.

`comicraft convert <comic>` converts a comic to a CBZ without changing anything else, and asks the
same about the original. `--to <file.cbz>` names the CBZ, and an existing file is never written over.

| Option | What it does |
|---|---|
| `--convert` | Converts a CBR to change it, without asking. (`tag`) |
| `--delete-original` | Moves the original to the Trash after converting, without asking. |
| `--keep-original` | Keeps the original after converting, without asking. |
| `--to <file.cbz>` | Names the CBZ a conversion makes. (`convert`) |

Run from a script, with its input redirected, ComiCraft asks nothing: a CBR is not converted
without `--convert`, an original is kept without `--delete-original`, and an original the Trash
will not take is kept.

## Layout

| Path | What it is |
|---|---|
| `src/ComiCraft.Comics` | The comic library: opening archives, finding and ordering the pages, reading and editing `ComicInfo.xml`, writing and converting archives, and the details and text wrapping the viewer shows. |
| `src/ComiCraft.Viewer` | The `comicraft` command: the viewer, on SDL (the window, fitting and scrolling pages, keys and mouse, and the details panel), and the `tag` and `convert` commands (`Cli`). |
| `tests/ComiCraft.Comics.Tests` | Tests for the library, on small comics made in memory and on disk, CBRs among them. |
| `tests/ComiCraft.Viewer.Tests` | Tests for the viewer's layout and its moving through a comic, without a window, and for the commands. |

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
dotnet run --project src/ComiCraft.Viewer -- tag <comic> [--<field> <value>...]
dotnet run --project src/ComiCraft.Viewer -- convert <comic>
dotnet run --project src/ComiCraft.Viewer -- --help
```

Comics never go in the repository: `*.cbz` and `*.cbr` are ignored.
