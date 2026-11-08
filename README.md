# ComiCraft

**ComiCraft** is a C# viewer for digital comics: CBZ (zip) and CBR (RAR) files, and PDFs whose
pages are pictures, as comics' are. It shows a comic
a page at a time, whole, fitted to the window's width, or zoomed, and scrolled across and down,
and shows what the comic's `ComicInfo.xml` says about it, when it has one.

A comic's kind is found from its contents, not its extension, so a CBR that is really a zip (or
the other way round) opens all the same. Its pages are the images in it (JPEG, PNG, GIF, WebP,
BMP) in the natural order of their names, so `page2.jpg` comes before `page10.jpg`.

### PDFs

A PDF's pages are shown when each is a picture, as in the PDFs ComiCraft makes and in most
scanned comics. The picture is taken out of the PDF as it is, a JPEG as the JPEG the PDF holds and
anything else as the same pixels, so it looks as it would in the comic it was made from; the PDF
is not drawn, as a reader of PDFs draws it. A page that is not only a picture, such as one with
text set on it, or one made of several pictures, says why it is not shown in its place, and a PDF
none of whose pages is a picture, such as a book of text, is not opened. Text hidden behind a
picture, which makes a scan searchable, does not count. A `ComicInfo.xml` attached to the PDF, as
ComiCraft attaches one, gives the comic's details.

## Reading

`comicraft <comic>` opens the comic in a window the shape of its first page.
`comicraft --help` lists the keys.

| Option | What it does |
|---|---|
| `--fullscreen` | Opens on the whole display. |

### Fitting the page

A comic page is taller than it is wide, and most screens are the other way round, so there are
two ways to show it, and the page can be zoomed beyond either. **W** switches between the two;
the title bar says when the page is fitted to the width, and how far it is zoomed.

| Fit | What it does |
|---|---|
| Page | The whole page is seen at once, as large as fits. This is where a comic opens. |
| Width | The page fills the width of the window, and is scrolled down through. Panels and lettering are as large as the window allows. |
| Zoomed | The page is as large as you zoom it, from the whole page (100%) to four of the screen's pixels to each of the page's, and is scrolled across and down. |

**+** and **−** zoom in and out at the middle of the window; Ctrl with the wheel, or a pinch on a
trackpad, zooms at the pointer, keeping what is under it where it is. **0** goes back to the
whole page. A zoom stays as the pages turn, as large on the next page as on this one, since what
needed it on one page will on the next.

### Reading a page larger than the window

A page is read as it is in print: across, and then down. Space goes a screen across the page; at
its right, back to its left a screen further down; at its end, on to the next page, at its top
left. Page Up goes the other way, back to the end of the page before. Where the page is no wider
than the window, there is nothing to go across, and Space simply goes down.

### Keys

| Key | What it does |
|---|---|
| Space, Page Down | Goes a screen across the page, or down it. At the end of a page, goes on to the top of the next. |
| Page Up | Goes a screen back across the page, or up it. At the start of a page, goes back to the end of the one before. |
| Down, Up | Goes down or up a little. At the end of a page, goes on or back a page, as above. |
| Right | Goes right a little across a page wider than the window; at its right, or where the page fits, goes to the next page, at its top. |
| Left | Goes left a little across a page wider than the window; at its left, or where the page fits, goes to the page before, at its top. |
| Backspace | Goes to the page before, at its top. |
| Home, End | Goes to the first page, or the last. |
| W | Fits the page whole, or to the width. |
| +, − | Zooms in, or out, at the middle of the window. (= zooms in too, so Shift is not needed.) |
| 0 | Fits the whole page again. |
| I | Shows the comic's details, or puts them away. |
| F, F11 | Switches between a window and the full screen. |
| Escape | Puts the details away when they are showing; quits otherwise. |
| Q | Quits. |

Where the whole page is seen, there is nothing to scroll, so every key that goes on or back
simply turns a page.

The keys that go on or back, held down, scroll to the end of the page and stop there, and go on to
the next page only when pressed again, so they never run on through pages unread; Space and Page
Up stop at the end of each row of a page wider than the window, too. Where the whole page is
seen, they turn page after page when held.

### Mouse

| Action | What it does |
|---|---|
| Wheel, or two fingers on a trackpad | Scrolls a page that is larger than the window, across as well as down. It never turns the page. |
| Ctrl and the wheel, or a pinch on a trackpad | Zooms in or out at the pointer. |
| Drag | Moves a page that is larger than the window. |
| Click on the right half of the window | Goes forward, as Page Down does. |
| Click on the left half of the window | Goes back, as Page Up does. |
| Click with the details showing | Puts them away. |

A click is a press and a release in the same place; once the pointer moves a few points with the
button down, it is a drag, and the release turns no page.

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

A PDF's `ComicInfo.xml` is shown, but not changed: change the CBZ the PDF was made from, and make
the PDF again.

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

## Converting a comic

`comicraft convert <comic>` converts a comic to a CBZ without changing anything else, and
`comicraft convert <comic> --format pdf` converts it to a PDF. Either way, it then asks about the
original as above, and it goes only once what was made has been read back and found to have its
pages. `--to <file>` names what is made (a name ending in `.pdf` makes a PDF without `--format`),
and an existing file is never written over.

A PDF has a page for each of the comic's pages, in the same order, each the page's image and
nothing else. Nothing is lost on the way. JPEG pages, and PNG pages, go in as they are, byte for
byte, as a PDF can hold them. WebP, GIF and BMP pages, which it cannot, go in as their pixels,
compressed without loss, as do the few PNGs and JPEGs a PDF cannot hold (a PNG with transparency
keeps it). Every page is 10¼ inches high, a printed comic's height, and as wide as its shape makes
it, so a page is about 6⅝ inches wide, as in print, and a spread twice that, however finely it was
scanned.

The comic's `ComicInfo.xml` gives the PDF its title (the heading, as the title bar shows it), its
author (the writers), its subject (the summary) and its keywords (the genres), and goes in whole,
as an attachment, so nothing in it is lost. Anything else in the archive, such as a scanner's note,
has no place in a PDF and is left out. ComiCraft opens the PDF with the same pages, byte for byte,
and the same details.

Before the original goes, the PDF is read back by [PdfPig](https://github.com/UglyToad/PdfPig), a
reader of PDFs that is not ComiCraft's own: it must have as many pages, each with one image, whose
data is the data that was written.

### A folder of comics

Given a folder, `convert` converts every comic in it: every CBR to a CBZ, or, with
`--format pdf`, every CBR and CBZ to a PDF, each beside itself. `--recursive` takes in the folders
in it as well, passing over hidden ones (a Trash is one) and links to folders.

```
comicraft convert ~/Comics/Troll
comicraft convert ~/Comics --format pdf --recursive
```

What becomes of the originals is asked once, before any comic is converted, so the rest needs no
one there; each original is still checked before it goes, and one the Trash will not take is kept,
not asked about. A comic whose CBZ or PDF is already there is skipped. One that cannot be converted
is reported, and the rest go on. Each is numbered as it is done (`[3/40]`), and a line at the end
says how many were converted, skipped or could not be. Ctrl+C stops once the comic being
converted is done, and a second Ctrl+C stops at once. ComiCraft exits with 3 when a comic could not
be converted or the conversion was stopped, and with 0 otherwise.

| Option | What it does |
|---|---|
| `--convert` | Converts a CBR to change it, without asking. (`tag`) |
| `--delete-original` | Moves the original to the Trash after converting, without asking. |
| `--keep-original` | Keeps the original after converting, without asking. |
| `--format cbz \| pdf` | What a conversion makes: a CBZ, which it makes unless told, or a PDF. (`convert`) |
| `--to <file>` | Names what a conversion makes, for one comic. (`convert`) |
| `--recursive` | Converts the comics in the folders in a folder too. (`convert`) |

Run from a script, with its input redirected, ComiCraft asks nothing: a CBR is not converted
without `--convert`, an original is kept without `--delete-original`, and an original the Trash
will not take is kept.

## Layout

| Path | What it is |
|---|---|
| `src/ComiCraft.Comics` | The comic library: opening archives, finding and ordering the pages, reading and editing `ComicInfo.xml`, writing and converting archives, converting comics to PDFs and reading PDFs as comics, and the details and text wrapping the viewer shows. |
| `src/ComiCraft.Viewer` | The `comicraft` command: the viewer, on SDL (the window, fitting and scrolling pages, keys and mouse, and the details panel), the `tag` and `convert` commands (`Cli`), and moving originals to the Trash (`Platform`). |
| `tests/ComiCraft.Comics.Tests` | Tests for the library, on small comics and images made in memory and on disk, CBRs among them, with the PDFs it makes read back by PdfPig, and PDFs made by hand of pages that are pictures and pages that are not. |
| `tests/ComiCraft.Viewer.Tests` | Tests for the viewer's layout and its moving through a comic, without a window, and for the commands. |

Beyond the .NET SDK, the dependencies are [SharpCompress](https://github.com/adamhathcock/sharpcompress)
for the archives, PdfPig for reading PDFs, and SDL3 (`ppy.SDL3-CS`), SDL3_image (`ppy.SDL3_image-CS`) and SDL3_ttf
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
dotnet run --project src/ComiCraft.Viewer -- convert <comic | folder> [--format pdf] [--recursive]
dotnet run --project src/ComiCraft.Viewer -- --help
```

Comics never go in the repository: `*.cbz` and `*.cbr` are ignored.
