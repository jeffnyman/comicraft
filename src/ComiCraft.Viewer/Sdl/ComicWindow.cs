using ComiCraft.Comics;
using SDL;
using static SDL.SDL3;
using static SDL.SDL3_image;

namespace ComiCraft.Viewer.Sdl;

/// <summary>
/// A window that shows a comic one page at a time. The page is scaled to fit the window, keeping
/// its shape, and centred on a dark background. The window waits for input rather than redrawing
/// in a loop: nothing on screen changes until the reader does something.
/// </summary>
internal sealed unsafe class ComicWindow : IDisposable
{
    private readonly ComicBook _comic;
    private readonly PageTurner _turner;
    private SDL_Window* _window;
    private SDL_Renderer* _renderer;
    private SDL_Texture* _page;
    private TextRenderer? _text;
    private readonly IReadOnlyList<Detail> _details;
    private bool _showInfo;

    public ComicWindow(ComicBook comic, bool fullscreen)
    {
        _comic = comic;
        _turner = new PageTurner(comic.PageCount);
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
                    if (!OnKey(e.key.key))
                    {
                        return;
                    }

                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN when e.button.button == 1:
                {
                    // a click puts the details away; otherwise, a click on the left half goes back
                    // a page, on the right half forward
                    if (_showInfo)
                    {
                        ToggleInfo();
                        break;
                    }

                    int width;
                    SDL_GetWindowSize(_window, &width, null);
                    Turn(e.button.x < width / 2f ? _turner.Previous() : _turner.Next());
                    break;
                }
            }
        }
    }

    /// <summary>Acts on a key; returns false when the reader asked to quit.</summary>
    private bool OnKey(SDL_Keycode key)
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

            case SDL_Keycode.SDLK_RIGHT:
            case SDL_Keycode.SDLK_DOWN:
            case SDL_Keycode.SDLK_PAGEDOWN:
            case SDL_Keycode.SDLK_SPACE:
                Turn(_turner.Next());
                break;

            case SDL_Keycode.SDLK_LEFT:
            case SDL_Keycode.SDLK_UP:
            case SDL_Keycode.SDLK_PAGEUP:
            case SDL_Keycode.SDLK_BACKSPACE:
                Turn(_turner.Previous());
                break;

            case SDL_Keycode.SDLK_HOME:
                Turn(_turner.First());
                break;

            case SDL_Keycode.SDLK_END:
                Turn(_turner.Last());
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

    private void Turn(bool changed)
    {
        if (changed)
        {
            LoadPage();
            Draw();
        }
    }

    private void LoadPage()
    {
        if (_page is not null)
        {
            SDL_DestroyTexture(_page);
            _page = null;
        }

        SDL_SetWindowTitle(_window, $"{_comic.Heading}  ({_turner.Current + 1} of {_comic.PageCount})");

        // the page stays pinned while SDL_image decodes it; the stream is closed when it is done
        using var pin = _comic.Page(_turner.Current).Pin();
        var io = SdlException.ThrowIfNull(SDL_IOFromConstMem((IntPtr)pin.Pointer, (nuint)_comic.Page(_turner.Current).Length), "SDL_IOFromConstMem");
        _page = IMG_LoadTexture_IO(_renderer, io, true);
        if (_page is null)
        {
            Console.Error.WriteLine($"comicraft: page {_turner.Current + 1} ({_comic.PageNames[_turner.Current]}) could not be shown: {SDL_GetError()}");
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
        var fit = Fit(pageWidth, pageHeight, bounds.w * 0.9f, bounds.h * 0.9f);
        SDL_SetWindowSize(_window, (int)fit.w, (int)fit.h);
        SDL_SetWindowPosition(_window, (int)SDL_WINDOWPOS_CENTERED, (int)SDL_WINDOWPOS_CENTERED);
    }

    private void Draw()
    {
        SDL_SetRenderDrawColor(_renderer, 24, 24, 24, 255);
        SDL_RenderClear(_renderer);

        if (_page is not null)
        {
            int outputWidth, outputHeight;
            float pageWidth, pageHeight;
            SDL_GetRenderOutputSize(_renderer, &outputWidth, &outputHeight);
            SDL_GetTextureSize(_page, &pageWidth, &pageHeight);
            var target = Fit(pageWidth, pageHeight, outputWidth, outputHeight);
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

    /// <summary>The largest rectangle of the page's shape that fits in the area, centred in it.</summary>
    internal static SDL_FRect Fit(float pageWidth, float pageHeight, float areaWidth, float areaHeight)
    {
        float scale = Math.Min(areaWidth / pageWidth, areaHeight / pageHeight);
        float width = pageWidth * scale, height = pageHeight * scale;
        return new SDL_FRect { x = (areaWidth - width) / 2, y = (areaHeight - height) / 2, w = width, h = height };
    }

    public void Dispose()
    {
        if (_page is not null)
        {
            SDL_DestroyTexture(_page);
            _page = null;
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
