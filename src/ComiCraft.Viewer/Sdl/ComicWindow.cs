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

    public ComicWindow(ComicBook comic, bool fullscreen)
    {
        _comic = comic;
        _turner = new PageTurner(comic.PageCount);

        SdlException.ThrowIfFalse(SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS), "SDL_Init");

        var flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        if (fullscreen)
        {
            flags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        }

        _window = SdlException.ThrowIfNull(SDL_CreateWindow(comic.Title, 800, 1200, flags), "SDL_CreateWindow");
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
                    // a click on the left half goes back a page, on the right half forward
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
            case SDL_Keycode.SDLK_ESCAPE:
            case SDL_Keycode.SDLK_Q:
                return false;

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

        SDL_SetWindowTitle(_window, $"{_comic.Title}  ({_turner.Current + 1} of {_comic.PageCount})");

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

        SDL_RenderPresent(_renderer);
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
