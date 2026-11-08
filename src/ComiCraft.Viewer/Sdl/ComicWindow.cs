using System.Runtime.InteropServices;
using ComiCraft.Comics;
using SDL;
using static SDL.SDL3;
using static SDL.SDL3_image;

namespace ComiCraft.Viewer.Sdl;

/// <summary>
/// A window that shows a comic one page at a time, on a dark background. The page keeps its shape
/// and is fitted to the window whole, or to the window's width, or zoomed, and scrolled across and
/// down (see <see cref="Reading"/>). Beside it, the comics in its folder (see <see cref="Shelf"/>)
/// come and go, to open another in its place. The window waits for input rather than redrawing in
/// a loop: nothing on screen changes until the reader does something.
/// </summary>
internal sealed unsafe class ComicWindow : IDisposable
{
    // the comic being read and where it is; none, when the window was opened on a folder and no
    // comic has been chosen yet
    private ComicBook? _comic;
    private string? _path;
    private Reading _reading = new(new PageTurner(0), new PageView());

    // where the reader was in each comic, by its path, to open it there again; none, when not kept
    private readonly ReadingPlaces? _places;
    private bool _placesFailed;

    // the comics in a folder, to choose another; and what the shelf has to say, as a comic is
    // opened or when one cannot be
    private readonly Shelf _shelf;
    private bool _showShelf;
    private string? _shelfNote;
    private int _hover = -1;
    private float _wheel;

    // Ctrl+C in the terminal, the terminal closing, or being told to stop: each closes the window
    // as its close button does, so where the reader was is remembered, instead of ending at once
    private readonly List<PosixSignalRegistration> _signals = [];
    private SDL_Window* _window;
    private SDL_Renderer* _renderer;
    private SDL_Texture* _page;

    // why the page is not shown, when it is not: a page of a PDF that is not a picture, or one that is broken
    private string? _notShown;
    private TextRenderer? _text;
    private IReadOnlyList<Detail> _details = [];
    private bool _showInfo;

    // the left button, while it is down: where it went down, in the window's points, and whether
    // it has moved far enough since to be a drag rather than a click
    private bool _pressed, _dragging;
    private float _pressX, _pressY;
    private SDL_Cursor* _moveCursor;

    /// <summary>
    /// A window on the comic at <paramref name="path"/>, with the comics in its folder a key away;
    /// or, with no comic, on the folder, with its comics showing to choose one.
    /// </summary>
    public ComicWindow(ComicBook? comic, string? path, string folder, bool fullscreen, ReadingPlaces? places)
    {
        _places = places;
        _shelf = new Shelf(folder, path);

        SdlException.ThrowIfFalse(SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS), "SDL_Init");

        var flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        if (fullscreen)
        {
            flags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        }

        _window = SdlException.ThrowIfNull(SDL_CreateWindow("ComiCraft", 800, 1200, flags), "SDL_CreateWindow");
        _renderer = SdlException.ThrowIfNull(SDL_CreateRenderer(_window, (byte*)null), "SDL_CreateRenderer");
        _text = new TextRenderer(_renderer, TextSize);
        _moveCursor = SDL_CreateSystemCursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE);
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
        {
            try
            {
                _signals.Add(PosixSignalRegistration.Create(signal, Quit));
            }
            catch (PlatformNotSupportedException)
            {
                // a system without the signal ends as it otherwise would; page turns are remembered all the same
            }
        }

        if (comic is not null && path is not null)
        {
            Start(comic, path);
        }
        else
        {
            _showShelf = true;
            ShowTitle();
        }

        SizeToFirstPage();
        SDL_ShowWindow(_window);
    }

    /// <summary>Starts reading the comic, where the reader was in it, if they have read it before.</summary>
    private void Start(ComicBook comic, string path)
    {
        _comic = comic;
        _path = path;
        _reading = new Reading(new PageTurner(comic.PageCount), new PageView());
        if (Recall() is { } place)
        {
            _reading.Turner.GoTo(place.Page);
            _reading.View.Restore(place.Fit, place.Zoom, place.Down, place.Across);
        }

        _details = ComicDetails.Of(comic);
        LoadPage();
    }

    public void Run()
    {
        try
        {
            Read();
        }
        finally
        {
            Remember();
        }
    }

    private static void Quit(PosixSignalContext context)
    {
        context.Cancel = true;
        var quit = new SDL_Event { type = (uint)SDL_EventType.SDL_EVENT_QUIT };
        SDL_PushEvent(&quit);
    }

    private Place? Recall()
    {
        try
        {
            return _path is null ? null : _places?.Recall(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Remembers where the reader is, to open the comic there again; a place that cannot be kept is said once, and the reading goes on.</summary>
    private void Remember()
    {
        if (_places is null || _placesFailed || _path is null)
        {
            return;
        }

        var view = _reading.View;
        try
        {
            _places.Remember(_path, new Place(_reading.Turner.Current, view.Fit, view.Zoom, view.Top, view.Left) { Read = DateTimeOffset.Now });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _placesFailed = true;
            Console.Error.WriteLine($"comicraft: where you are in the comic cannot be remembered: {ex.Message}");
        }
    }

    private void Read()
    {
        Draw();

        SDL_Event e;
        while (SDL_WaitEvent(&e))
        {
            switch ((SDL_EventType)e.type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                    return;

                case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                case SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
                    Draw();
                    break;

                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    if (!OnKey(e.key.key, e.key.repeat))
                    {
                        return;
                    }

                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL when _showShelf && OnShelf(e.wheel.mouse_x):
                {
                    // the wheel scrolls the shelf under the pointer, three rows a turn, and a
                    // trackpad's smaller movements add up to rows
                    _wheel -= e.wheel.y * 3;
                    int rows = (int)_wheel;
                    _wheel -= rows;
                    if (rows != 0)
                    {
                        _shelf.Scroll(rows, ShelfRows());
                        UpdateHover(e.wheel.mouse_x, e.wheel.mouse_y);
                        Draw();
                    }

                    break;
                }

                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL when _comic is not null:
                {
                    // with Ctrl, the wheel zooms at the pointer; otherwise it (or two fingers on a
                    // trackpad) scrolls the page, across as well as down, and never turns it; its
                    // direction is the system's, natural scrolling included
                    float density = Density();
                    if ((SDL_GetModState() & SDL_Keymod.SDL_KMOD_CTRL) != 0)
                    {
                        ZoomAt(Math.Pow(ZoomStep, e.wheel.y), e.wheel.mouse_x * density, e.wheel.mouse_y * density);
                        break;
                    }

                    var frame = CurrentFrame();
                    Show(_reading.Pan(frame, e.wheel.x * frame.AreaWidth * WheelStep, -e.wheel.y * frame.AreaHeight * WheelStep));
                    break;
                }

                case SDL_EventType.SDL_EVENT_PINCH_UPDATE when _comic is not null:
                {
                    // a pinch on a trackpad zooms at the pointer, by as much as the fingers spread
                    float x, y, density = Density();
                    SDL_GetMouseState(&x, &y);
                    ZoomAt(e.pinch.scale, x * density, y * density);
                    break;
                }

                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN when e.button.button == 1:
                    (_pressed, _dragging, _pressX, _pressY) = (true, false, e.button.x, e.button.y);
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_MOTION when _showShelf:
                    if (UpdateHover(e.motion.x, e.motion.y))
                    {
                        Draw();
                    }

                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_MOTION when _pressed && !_showInfo && _comic is not null:
                {
                    // past a few points from where it went down, the button is dragging the page
                    if (!_dragging && MathF.Abs(e.motion.x - _pressX) + MathF.Abs(e.motion.y - _pressY) >= DragDistance)
                    {
                        _dragging = true;
                        SDL_SetCursor(_moveCursor);
                    }

                    if (_dragging)
                    {
                        float density = Density();
                        Show(_reading.Pan(CurrentFrame(), -e.motion.xrel * density, -e.motion.yrel * density));
                    }

                    break;
                }

                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == 1 && _pressed:
                {
                    bool clicked = !_dragging;
                    if (_dragging)
                    {
                        SDL_SetCursor(SDL_GetDefaultCursor());
                    }

                    (_pressed, _dragging) = (false, false);
                    if (clicked)
                    {
                        Click(e.button.x, e.button.y);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// A click, which is a press and a release with no drag between. On the shelf, it opens the
    /// entry clicked, and beside it puts the shelf away. Otherwise, it puts the details away; or,
    /// on the left half of the window it goes back and on the right half forward, as Page Up and
    /// Page Down do.
    /// </summary>
    private void Click(float x, float y)
    {
        if (_showShelf)
        {
            if (!OnShelf(x))
            {
                HideShelf();
            }
            else if (ShelfRowAt(y) is { } row && _shelf.Select(row))
            {
                ChooseOnShelf();
            }

            return;
        }

        if (_showInfo)
        {
            ToggleInfo();
            return;
        }

        int width;
        SDL_GetWindowSize(_window, &width, null);
        Show(x < width / 2f ? _reading.ScreenBack(CurrentFrame()) : _reading.ScreenForward(CurrentFrame()));
    }

    /// <summary>Zooms by the factor, keeping the page under the point (in the renderer's pixels) where it is.</summary>
    private void ZoomAt(double factor, float x, float y)
    {
        if (_reading.View.ZoomBy(CurrentFrame(), factor, x, y))
        {
            ShowTitle();
            Draw();
        }
    }

    /// <summary>Zooms at the middle of the window, as the keys do.</summary>
    private void ZoomAtMiddle(double factor)
    {
        var frame = CurrentFrame();
        ZoomAt(factor, frame.AreaWidth / 2, frame.AreaHeight / 2);
    }

    /// <summary>The renderer's pixels to each of the window's points: 2 on most high density displays.</summary>
    private float Density()
    {
        float density = SDL_GetWindowPixelDensity(_window);
        return density > 0 ? density : 1;
    }

    /// <summary>Acts on a key, which may be repeating from being held; returns false when the reader asked to quit.</summary>
    private bool OnKey(SDL_Keycode key, bool repeat)
    {
        if (_showShelf)
        {
            return OnShelfKey(key);
        }

        switch (key)
        {
            case SDL_Keycode.SDLK_L:
            case SDL_Keycode.SDLK_TAB:
                ShowShelf();
                break;

            // Escape puts the details away first, and quits when they are not showing
            case SDL_Keycode.SDLK_ESCAPE when _showInfo:
                ToggleInfo();
                break;

            case SDL_Keycode.SDLK_ESCAPE:
            case SDL_Keycode.SDLK_Q:
                return false;

            case SDL_Keycode.SDLK_I:
                ToggleInfo();
                break;

            case SDL_Keycode.SDLK_W:
                // fitted to the width, W fits the page whole; fitted whole or zoomed, to the width
                _reading.View.Fit = _reading.View.Fit == Fit.Width ? Fit.Page : Fit.Width;
                ShowTitle();
                Draw();
                break;

            // + is = with Shift on most keyboards, so = zooms in too, Shift or not
            case SDL_Keycode.SDLK_EQUALS:
            case SDL_Keycode.SDLK_PLUS:
            case SDL_Keycode.SDLK_KP_PLUS:
                ZoomAtMiddle(ZoomStep);
                break;

            case SDL_Keycode.SDLK_MINUS:
            case SDL_Keycode.SDLK_KP_MINUS:
                ZoomAtMiddle(1 / ZoomStep);
                break;

            case SDL_Keycode.SDLK_0:
            case SDL_Keycode.SDLK_KP_0:
                _reading.View.Fit = Fit.Page;
                ShowTitle();
                Draw();
                break;

            case SDL_Keycode.SDLK_SPACE:
            case SDL_Keycode.SDLK_PAGEDOWN:
                Show(_reading.ScreenForward(CurrentFrame(), repeat));
                break;

            case SDL_Keycode.SDLK_PAGEUP:
                Show(_reading.ScreenBack(CurrentFrame(), repeat));
                break;

            case SDL_Keycode.SDLK_DOWN:
                Show(_reading.StepForward(CurrentFrame(), repeat));
                break;

            case SDL_Keycode.SDLK_UP:
                Show(_reading.StepBack(CurrentFrame(), repeat));
                break;

            case SDL_Keycode.SDLK_RIGHT:
                Show(_reading.StepRight(CurrentFrame(), repeat));
                break;

            case SDL_Keycode.SDLK_LEFT:
                Show(_reading.StepLeft(CurrentFrame(), repeat));
                break;

            case SDL_Keycode.SDLK_BACKSPACE:
                Show(_reading.PreviousPage());
                break;

            case SDL_Keycode.SDLK_HOME:
                Show(_reading.FirstPage());
                break;

            case SDL_Keycode.SDLK_END:
                Show(_reading.LastPage());
                break;

            case SDL_Keycode.SDLK_F:
            case SDL_Keycode.SDLK_F11:
                ToggleFullscreen();
                break;
        }

        return true;
    }

    private void ToggleFullscreen()
    {
        bool isFullscreen = (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;
        SDL_SetWindowFullscreen(_window, !isFullscreen);
    }

    /// <summary>
    /// A key while the shelf is showing: the arrows, Page Up and Down, and Home and End choose an
    /// entry; Enter, or Right, opens it; Left, or Backspace, goes to the folder above. L, Tab and
    /// Escape put the shelf away, and with no comic open, Escape quits, as it does elsewhere.
    /// </summary>
    private bool OnShelfKey(SDL_Keycode key)
    {
        int rows = ShelfRows();
        switch (key)
        {
            case SDL_Keycode.SDLK_ESCAPE when _comic is null:
            case SDL_Keycode.SDLK_Q:
                return false;

            case SDL_Keycode.SDLK_ESCAPE:
            case SDL_Keycode.SDLK_L:
            case SDL_Keycode.SDLK_TAB:
                HideShelf();
                return true;

            case SDL_Keycode.SDLK_RETURN:
            case SDL_Keycode.SDLK_KP_ENTER:
            case SDL_Keycode.SDLK_RIGHT:
                ChooseOnShelf();
                return true;

            case SDL_Keycode.SDLK_LEFT:
            case SDL_Keycode.SDLK_BACKSPACE:
                if (_shelf.Up())
                {
                    ShelfMoved(follow: true);
                }

                return true;

            case SDL_Keycode.SDLK_F:
            case SDL_Keycode.SDLK_F11:
                ToggleFullscreen();
                return true;

            case SDL_Keycode.SDLK_DOWN:
                _shelf.Move(1);
                break;
            case SDL_Keycode.SDLK_UP:
                _shelf.Move(-1);
                break;
            case SDL_Keycode.SDLK_PAGEDOWN:
                _shelf.Move(Math.Max(1, rows - 1));
                break;
            case SDL_Keycode.SDLK_PAGEUP:
                _shelf.Move(-Math.Max(1, rows - 1));
                break;
            case SDL_Keycode.SDLK_HOME:
                _shelf.First();
                break;
            case SDL_Keycode.SDLK_END:
                _shelf.Last();
                break;
            default:
                return true;
        }

        ShelfMoved(follow: true);
        return true;
    }

    /// <summary>Shows the comics in the folder, looked in again, as it may have changed, with the details put away.</summary>
    private void ShowShelf()
    {
        _shelf.Refresh();
        (_showShelf, _showInfo, _shelfNote, _hover) = (true, false, null, -1);
        ShelfMoved(follow: true);
    }

    /// <summary>Puts the shelf away, to read the comic open; with none open, the shelf stays.</summary>
    private void HideShelf()
    {
        if (_comic is null)
        {
            return;
        }

        (_showShelf, _shelfNote) = (false, null);
        Draw();
    }

    /// <summary>Redraws the shelf once the chosen entry, or the folder, has changed; following the choice, it scrolls to keep it in sight.</summary>
    private void ShelfMoved(bool follow)
    {
        if (follow)
        {
            _shelf.KeepInSight(ShelfRows());
        }

        _hover = -1;
        Draw();
    }

    /// <summary>Goes into the chosen folder, or opens the chosen comic, in place of the one being read.</summary>
    private void ChooseOnShelf()
    {
        _shelfNote = null;
        if (_shelf.Choose() is not { } chosen)
        {
            ShelfMoved(follow: true);
            return;
        }

        if (_path is not null && Path.GetFullPath(_path) == chosen.Path)
        {
            HideShelf();
            return;
        }

        // a large comic takes a moment to open, so the shelf says what it is doing
        _shelfNote = $"Opening {chosen.Name}…";
        Draw();

        ComicBook comic;
        try
        {
            comic = ComicBook.Open(chosen.Path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _shelfNote = $"{chosen.Name} could not be read as a comic: {ex.Message}";
            Draw();
            return;
        }

        if (comic.PageCount == 0)
        {
            _shelfNote = $"{chosen.Name} has no pages.";
            Draw();
            return;
        }

        Remember();
        Start(comic, chosen.Path);
        (_showShelf, _showInfo, _shelfNote) = (false, false, null);
        Draw();
    }

    private void ToggleInfo()
    {
        _showInfo = !_showInfo;
        Draw();
    }

    /// <summary>A turn of the mouse wheel scrolls this share of the window's height (or width, across).</summary>
    private const float WheelStep = 0.05f;

    /// <summary>A press of + or −, or a turn of the wheel with Ctrl, zooms by this much.</summary>
    private const double ZoomStep = 1.25;

    /// <summary>How far, in points, the button moves while down before it is a drag and not a click.</summary>
    private const float DragDistance = 4;

    /// <summary>Shows what the reader's action did: another page, or another part of this one.</summary>
    private void Show(Moved moved)
    {
        if (moved == Moved.Turned)
        {
            LoadPage();
            Remember();
        }

        if (moved != Moved.Nothing)
        {
            Draw();
        }
    }

    /// <summary>The current page's size and the window's, in the renderer's pixels.</summary>
    private Frame CurrentFrame()
    {
        int outputWidth, outputHeight;
        float pageWidth = 0, pageHeight = 0;
        SDL_GetRenderOutputSize(_renderer, &outputWidth, &outputHeight);
        if (_page is not null)
        {
            SDL_GetTextureSize(_page, &pageWidth, &pageHeight);
        }

        return new Frame(pageWidth, pageHeight, outputWidth, outputHeight);
    }

    private void ShowTitle()
    {
        if (_comic is null)
        {
            SDL_SetWindowTitle(_window, $"{_shelf.Heading} - ComiCraft");
            return;
        }

        string fit = _reading.View.Fit switch
        {
            Fit.Width => ", fit to width",
            Fit.Zoom => $", {Math.Round(_reading.View.Zoom * 100)}%",
            _ => "",
        };
        SDL_SetWindowTitle(_window, $"{_comic.Heading}  ({_reading.Turner.Current + 1} of {_comic.PageCount}{fit})");
    }

    private void LoadPage()
    {
        if (_page is not null)
        {
            SDL_DestroyTexture(_page);
            _page = null;
        }

        ShowTitle();

        _notShown = null;
        if (_comic is null)
        {
            return;
        }

        int current = _reading.Turner.Current;
        if (_comic.Problem(current) is { } problem)
        {
            _notShown = $"Page {current + 1} is not shown: {problem}.";
            return;
        }

        // the page stays pinned while SDL_image decodes it; the stream is closed when it is done
        var image = _comic.Page(current);
        using var pin = image.Pin();
        var io = SdlException.ThrowIfNull(SDL_IOFromConstMem((IntPtr)pin.Pointer, (nuint)image.Length), "SDL_IOFromConstMem");
        _page = IMG_LoadTexture_IO(_renderer, io, true);
        if (_page is null)
        {
            string error = SDL_GetError() ?? "";
            Console.Error.WriteLine($"comicraft: page {current + 1} ({_comic.PageNames[current]}) could not be shown: {error}");
            _notShown = $"Page {current + 1} could not be shown: {error}";
        }
    }

    /// <summary>
    /// Makes the window the shape of the first page, as tall as the display comfortably allows; with
    /// no page, the shape of a comic's, ready for one.
    /// </summary>
    private void SizeToFirstPage()
    {
        var bounds = new SDL_Rect();
        if (!SDL_GetDisplayUsableBounds(SDL_GetDisplayForWindow(_window), &bounds))
        {
            return;
        }

        float pageWidth = 2, pageHeight = 3;
        if (_page is not null)
        {
            SDL_GetTextureSize(_page, &pageWidth, &pageHeight);
        }

        var fit = new PageView().Place(new Frame(pageWidth, pageHeight, bounds.w * 0.9f, bounds.h * 0.9f));
        SDL_SetWindowSize(_window, (int)fit.Width, (int)fit.Height);
        SDL_SetWindowPosition(_window, (int)SDL_WINDOWPOS_CENTERED, (int)SDL_WINDOWPOS_CENTERED);
    }

    private void Draw()
    {
        SDL_SetRenderDrawColor(_renderer, 24, 24, 24, 255);
        SDL_RenderClear(_renderer);

        if (_page is not null)
        {
            var place = _reading.View.Place(CurrentFrame());
            var target = new SDL_FRect { x = place.X, y = place.Y, w = place.Width, h = place.Height };
            SDL_RenderTexture(_renderer, _page, null, &target);
        }
        else if (_notShown is not null)
        {
            DrawNotShown(_notShown);
        }

        if (_showInfo)
        {
            DrawInfo();
        }

        if (_showShelf)
        {
            DrawShelf();
        }

        SDL_RenderPresent(_renderer);
    }

    /// <summary>Why the page is not shown, in the middle of the window where it would be.</summary>
    private void DrawNotShown(string why)
    {
        if (_text is null)
        {
            return;
        }

        int outputWidth, outputHeight;
        SDL_GetRenderOutputSize(_renderer, &outputWidth, &outputHeight);
        float density = SDL_GetWindowDisplayScale(_window);
        density = density > 0 ? density : 1;
        _text.SetSize(TextSize * density);

        float width = Math.Min(outputWidth - 2 * 24 * density, 30 * TextSize * density);
        if (width < 1)
        {
            return;
        }

        var lines = TextWrap.Wrap(why, width, text => _text.Measure(text));
        float y = (outputHeight - lines.Count * _text.LineHeight) / 2f;
        var color = new SDL_Color { r = 170, g = 170, b = 170, a = 255 };
        foreach (string line in lines)
        {
            _text.Draw(line, (outputWidth - _text.Measure(line)) / 2, y, color);
            y += _text.LineHeight;
        }
    }

    /// <summary>The size of text, in points; on a high density display, it is that many times as many pixels.</summary>
    private const float TextSize = 15;

    /// <summary>
    /// The comic's details on a dark panel over the page. The text is its usual size where the
    /// window has room for it, and smaller where the window is too narrow for a readable line.
    /// </summary>
    private void DrawInfo()
    {
        // widths in ems, that is, in multiples of the text's size: the panel is never wider than
        // a comfortable line, and the text is made smaller to keep a line from being too short
        const float MaxWidth = 34, MinWidth = 26;

        if (_text is null || _comic is null)
        {
            return;
        }

        int outputWidth, outputHeight;
        SDL_GetRenderOutputSize(_renderer, &outputWidth, &outputHeight);
        float density = SDL_GetWindowDisplayScale(_window);
        density = density > 0 ? density : 1;

        float margin = Math.Max(16 * density, Math.Min(outputWidth, outputHeight) / 20f);
        float room = outputWidth - 2 * margin;
        float size = Math.Max(8 * density, Math.Min(TextSize * density, room / MinWidth));
        _text.SetSize(size);

        float padding = size;
        float width = Math.Min(room - 2 * padding, MaxWidth * size);
        int lineHeight = _text.LineHeight;
        int maxRows = (int)((outputHeight - 2 * margin - 2 * padding) / lineHeight);
        if (width < 1 || maxRows < 1)
        {
            return;
        }

        Func<string, float> measure = text => _text.Measure(text);
        var rows = InfoOverlay.Layout(_comic.Heading, _details, width, maxRows, measure, text => _text.Measure(text, heading: true));
        float valueColumn = InfoOverlay.ValueColumn(_details, width, measure);

        var panel = new SDL_FRect { w = width + 2 * padding, h = rows.Count * lineHeight + 2 * padding };
        panel.x = (outputWidth - panel.w) / 2;
        panel.y = (outputHeight - panel.h) / 2;
        SDL_SetRenderDrawBlendMode(_renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);
        SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 220);
        SDL_RenderFillRect(_renderer, &panel);

        var headingColor = new SDL_Color { r = 255, g = 210, b = 120, a = 255 };
        var labelColor = new SDL_Color { r = 150, g = 150, b = 150, a = 255 };
        var valueColor = new SDL_Color { r = 235, g = 235, b = 235, a = 255 };
        float left = panel.x + padding;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            float y = panel.y + padding + i * lineHeight;
            if (row.Kind == InfoOverlay.RowKind.Heading)
            {
                _text.Draw(row.Text, left, y, headingColor, heading: true);
            }
            else if (row.Kind == InfoOverlay.RowKind.Detail)
            {
                _text.Draw(row.Label, left, y, labelColor);
                _text.Draw(row.Text, left + valueColumn, y, valueColor);
            }
        }
    }

    /// <summary>
    /// Where the shelf is, in the renderer's pixels: a panel down the left of the window, as wide
    /// as a good share of it, within a comfortable width for a name; its heading, and the rows
    /// below it, as many as there is room for, with room at the foot for a note.
    /// </summary>
    private readonly record struct ShelfPlace(float Width, float Padding, float Size, float ListTop, int Rows, int RowHeight, int LineHeight);

    private ShelfPlace PlaceShelf()
    {
        int outputWidth, outputHeight;
        SDL_GetRenderOutputSize(_renderer, &outputWidth, &outputHeight);
        float density = SDL_GetWindowDisplayScale(_window);
        density = density > 0 ? density : 1;

        float size = TextSize * density;
        _text?.SetSize(size);
        int lineHeight = Math.Max(1, _text?.LineHeight ?? (int)(size * 1.3f));
        float padding = size;
        float width = Math.Min(outputWidth, Math.Clamp(outputWidth * 0.4f, 20 * size, 30 * size));
        float listTop = padding + lineHeight * 1.5f;

        // a row is a little taller than its line, to be easy to tell apart and to click; the foot
        // keeps room for a note of three lines: what is being opened, or why it could not be
        int rowHeight = (int)MathF.Ceiling(lineHeight * 1.35f);
        int rows = (int)((outputHeight - listTop - 2 * padding - 3 * lineHeight) / rowHeight);
        return new ShelfPlace(width, padding, size, listTop, Math.Max(1, rows), rowHeight, lineHeight);
    }

    private int ShelfRows() => PlaceShelf().Rows;

    /// <summary>Whether the point, in the window's points, is on the shelf.</summary>
    private bool OnShelf(float x) => x * Density() < PlaceShelf().Width;

    /// <summary>The entry at the height, in the window's points, by its place on the shelf; null between them.</summary>
    private int? ShelfRowAt(float y)
    {
        var place = PlaceShelf();
        float at = y * Density() - place.ListTop;
        if (at < 0)
        {
            return null;
        }

        int row = (int)(at / place.RowHeight);
        return row < place.Rows && _shelf.Top + row < _shelf.Entries.Count ? _shelf.Top + row : null;
    }

    /// <summary>Marks the entry under the pointer; returns whether that changed.</summary>
    private bool UpdateHover(float x, float y)
    {
        int hover = OnShelf(x) && ShelfRowAt(y) is { } row ? row : -1;
        if (hover == _hover)
        {
            return false;
        }

        _hover = hover;
        return true;
    }

    /// <summary>
    /// The comics in the folder, down the left of the window: the folder's name, then its entries,
    /// the chosen one marked, and the comic being read in the colour of a heading.
    /// </summary>
    private void DrawShelf()
    {
        if (_text is null)
        {
            return;
        }

        int outputWidth, outputHeight;
        SDL_GetRenderOutputSize(_renderer, &outputWidth, &outputHeight);
        var place = PlaceShelf();
        _shelf.KeepInSight(place.Rows);

        // with no comic open yet, the rest of the window says what the shelf is for
        if (_comic is null && outputWidth - place.Width > 4 * place.Size)
        {
            const string Hint = "Choose a comic to read.";
            var hintColor = new SDL_Color { r = 110, g = 110, b = 110, a = 255 };
            _text.Draw(Hint, place.Width + (outputWidth - place.Width - _text.Measure(Hint)) / 2, (outputHeight - place.LineHeight) / 2f, hintColor);
        }

        SDL_SetRenderDrawBlendMode(_renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);
        var panel = new SDL_FRect { w = place.Width, h = outputHeight };
        SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 230);
        SDL_RenderFillRect(_renderer, &panel);

        var headingColor = new SDL_Color { r = 255, g = 210, b = 120, a = 255 };
        var folderColor = new SDL_Color { r = 150, g = 190, b = 255, a = 255 };
        var nameColor = new SDL_Color { r = 235, g = 235, b = 235, a = 255 };
        var quietColor = new SDL_Color { r = 150, g = 150, b = 150, a = 255 };
        float left = place.Padding, room = place.Width - 2 * place.Padding;
        if (room < 1)
        {
            return;
        }

        _text.Draw(Shelf.Shorten(_shelf.Heading, room, t => _text.Measure(t, heading: true)), left, place.Padding, headingColor, heading: true);

        string? current = _path is null ? null : Path.GetFullPath(_path);
        var entries = _shelf.Entries;
        for (int row = 0; row < place.Rows && _shelf.Top + row < entries.Count; row++)
        {
            int index = _shelf.Top + row;
            var entry = entries[index];
            float y = place.ListTop + row * place.RowHeight;
            if (index == _shelf.Selected || index == _hover)
            {
                byte shade = index == _shelf.Selected ? (byte)70 : (byte)40;
                var band = new SDL_FRect { x = 0, y = y, w = place.Width, h = place.RowHeight };
                SDL_SetRenderDrawColor(_renderer, shade, shade, shade, 255);
                SDL_RenderFillRect(_renderer, &band);
            }

            var (name, color) = entry.Kind switch
            {
                ShelfKind.Up => (".. (up a folder)", quietColor),
                ShelfKind.Folder => (entry.Name + Path.DirectorySeparatorChar, folderColor),
                _ => (entry.Name, entry.Path == current ? headingColor : nameColor),
            };
            _text.Draw(Shelf.Shorten(name, room, t => _text.Measure(t)), left, y + (place.RowHeight - place.LineHeight) / 2f, color);
        }

        string? note = _shelfNote ?? _shelf.Problem ??
            (entries.All(e => e.Kind == ShelfKind.Up) ? "There are no comics or folders here." : null);
        if (note is not null)
        {
            var lines = TextWrap.Wrap(note, room, t => _text.Measure(t));
            float y = outputHeight - place.Padding - Math.Min(3, lines.Count) * place.LineHeight;
            foreach (string line in lines.Take(3))
            {
                _text.Draw(line, left, y, quietColor);
                y += place.LineHeight;
            }
        }
    }

    public void Dispose()
    {
        _signals.ForEach(s => s.Dispose());
        _signals.Clear();

        if (_page is not null)
        {
            SDL_DestroyTexture(_page);
            _page = null;
        }

        if (_moveCursor is not null)
        {
            SDL_DestroyCursor(_moveCursor);
            _moveCursor = null;
        }

        // the text is drawn by the renderer, and so goes before it
        _text?.Dispose();
        _text = null;

        if (_renderer is not null)
        {
            SDL_DestroyRenderer(_renderer);
            _renderer = null;
        }

        if (_window is not null)
        {
            SDL_DestroyWindow(_window);
            _window = null;
        }

        SDL_Quit();
    }
}
