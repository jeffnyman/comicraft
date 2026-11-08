using ComiCraft.Comics;
using SDL;
using static SDL.SDL3;
using static SDL.SDL3_image;

namespace ComiCraft.Viewer.Sdl;

/// <summary>
/// A window that shows a comic one page at a time, on a dark background. The page keeps its shape
/// and is fitted to the window whole, or to the window's width, or zoomed, and scrolled across and
/// down (see <see cref="Reading"/>). The window waits for input rather than redrawing in a loop:
/// nothing on screen changes until the reader does something.
/// </summary>
internal sealed unsafe class ComicWindow : IDisposable
{
    private readonly ComicBook _comic;
    private readonly Reading _reading;
    private SDL_Window* _window;
    private SDL_Renderer* _renderer;
    private SDL_Texture* _page;
    private TextRenderer? _text;
    private readonly IReadOnlyList<Detail> _details;
    private bool _showInfo;

    // the left button, while it is down: where it went down, in the window's points, and whether
    // it has moved far enough since to be a drag rather than a click
    private bool _pressed, _dragging;
    private float _pressX, _pressY;
    private SDL_Cursor* _moveCursor;

    public ComicWindow(ComicBook comic, bool fullscreen)
    {
        _comic = comic;
        _reading = new Reading(new PageTurner(comic.PageCount), new PageView());
        _details = ComicDetails.Of(comic);

        SdlException.ThrowIfFalse(SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS), "SDL_Init");

        var flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        if (fullscreen)
        {
            flags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        }

        _window = SdlException.ThrowIfNull(SDL_CreateWindow(comic.Heading, 800, 1200, flags), "SDL_CreateWindow");
        _renderer = SdlException.ThrowIfNull(SDL_CreateRenderer(_window, (byte*)null), "SDL_CreateRenderer");
        _text = new TextRenderer(_renderer, TextSize);
        _moveCursor = SDL_CreateSystemCursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE);

        LoadPage();
        SizeToFirstPage();
        SDL_ShowWindow(_window);
    }

    public void Run()
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

                case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
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

                case SDL_EventType.SDL_EVENT_PINCH_UPDATE:
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

                case SDL_EventType.SDL_EVENT_MOUSE_MOTION when _pressed && !_showInfo:
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
                        Click(e.button.x);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// A click, which is a press and a release with no drag between: it puts the details away;
    /// otherwise, on the left half of the window it goes back and on the right half forward, as
    /// Page Up and Page Down do.
    /// </summary>
    private void Click(float x)
    {
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
        switch (key)
        {
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
                bool isFullscreen = (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;
                SDL_SetWindowFullscreen(_window, !isFullscreen);
                break;
        }

        return true;
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

        // the page stays pinned while SDL_image decodes it; the stream is closed when it is done
        int current = _reading.Turner.Current;
        var image = _comic.Page(current);
        using var pin = image.Pin();
        var io = SdlException.ThrowIfNull(SDL_IOFromConstMem((IntPtr)pin.Pointer, (nuint)image.Length), "SDL_IOFromConstMem");
        _page = IMG_LoadTexture_IO(_renderer, io, true);
        if (_page is null)
        {
            Console.Error.WriteLine($"comicraft: page {current + 1} ({_comic.PageNames[current]}) could not be shown: {SDL_GetError()}");
        }
    }

    /// <summary>Makes the window the shape of the first page, as tall as the display comfortably allows.</summary>
    private void SizeToFirstPage()
    {
        if (_page is null)
        {
            return;
        }

        var bounds = new SDL_Rect();
        if (!SDL_GetDisplayUsableBounds(SDL_GetDisplayForWindow(_window), &bounds))
        {
            return;
        }

        float pageWidth, pageHeight;
        SDL_GetTextureSize(_page, &pageWidth, &pageHeight);
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

        if (_showInfo)
        {
            DrawInfo();
        }

        SDL_RenderPresent(_renderer);
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

        if (_text is null)
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

    public void Dispose()
    {
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
