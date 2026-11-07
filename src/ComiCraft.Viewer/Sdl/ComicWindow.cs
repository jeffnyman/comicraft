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

    /// <summary>
    /// The comic's details on a dark panel over the page, in SDL's built-in 8x8 font. The font is
    /// scaled by a whole number, larger on a high density display, and made smaller when the window
    /// is too narrow for a readable line.
    /// </summary>
    private void DrawInfo()
    {
        const int Glyph = 8, MaxColumns = 72, MinColumns = 40;

        int outputWidth, outputHeight;
        SDL_GetRenderOutputSize(_renderer, &outputWidth, &outputHeight);
        float density = SDL_GetWindowDisplayScale(_window);
        int scale = Math.Max(2, (int)MathF.Round(1.5f * (density > 0 ? density : 1)));
        int twentieth = Math.Min(outputWidth, outputHeight) / 20;
        int Margin(int s) => Math.Max(Glyph * s, twentieth);
        while (scale > 1 && (outputWidth - 2 * Margin(scale)) / (Glyph * scale) - 2 < MinColumns)
        {
            scale--;
        }

        int glyph = Glyph * scale, lineHeight = 10 * scale, padding = glyph, margin = Margin(scale);
        int columns = Math.Clamp((outputWidth - 2 * margin - 2 * padding) / glyph, 1, MaxColumns);
        int maxRows = Math.Max(1, (outputHeight - 2 * margin - 2 * padding + lineHeight - glyph) / lineHeight);
        var rows = InfoOverlay.Layout(_comic.Heading, _details, columns, maxRows);
        int valueColumn = InfoOverlay.ValueColumn(_details, columns);

        var panel = new SDL_FRect { w = columns * glyph + 2 * padding, h = rows.Count * lineHeight - (lineHeight - glyph) + 2 * padding };
        panel.x = (outputWidth - panel.w) / 2;
        panel.y = (outputHeight - panel.h) / 2;
        SDL_SetRenderDrawBlendMode(_renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);
        SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 220);
        SDL_RenderFillRect(_renderer, &panel);

        // the text is drawn at its scale, so its positions are given in that scale's units
        SDL_SetRenderScale(_renderer, scale, scale);
        float left = (panel.x + padding) / scale, top = (panel.y + padding) / scale;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            float y = top + i * lineHeight / (float)scale;
            if (row.Kind == InfoOverlay.RowKind.Heading)
            {
                SDL_SetRenderDrawColor(_renderer, 255, 210, 120, 255);
                SDL_RenderDebugText(_renderer, left, y, row.Text);
            }
            else if (row.Kind == InfoOverlay.RowKind.Detail)
            {
                SDL_SetRenderDrawColor(_renderer, 150, 150, 150, 255);
                SDL_RenderDebugText(_renderer, left, y, row.Label);
                SDL_SetRenderDrawColor(_renderer, 235, 235, 235, 255);
                SDL_RenderDebugText(_renderer, left + valueColumn * Glyph, y, row.Text);
            }
        }

        SDL_SetRenderScale(_renderer, 1, 1);
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
