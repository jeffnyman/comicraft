using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;
using static SDL.SDL3_ttf;

namespace ComiCraft.Viewer.Sdl;

/// <summary>
/// Draws text with SDL_ttf in Inter, the font built into the viewer: a regular face for most
/// text and a semi-bold one for headings. Sizes are in pixels of the renderer's output, so text
/// is as sharp as the display allows.
/// </summary>
internal sealed unsafe class TextRenderer : IDisposable
{
    private TTF_TextEngine* _engine;
    private Face _regular;
    private Face _semiBold;
    private float _size;

    /// <summary>A font and the memory it is read from, which SDL_ttf reads as it needs and so must outlive it.</summary>
    private struct Face
    {
        public TTF_Font* Font;
        public void* Data;
    }

    public TextRenderer(SDL_Renderer* renderer, float size)
    {
        SdlException.ThrowIfFalse(TTF_Init(), "TTF_Init");
        _engine = SdlException.ThrowIfNull(TTF_CreateRendererTextEngine(renderer), "TTF_CreateRendererTextEngine");
        _size = size;
        _regular = Open("Inter-Regular.ttf", size);
        _semiBold = Open("Inter-SemiBold.ttf", size);
    }

    /// <summary>The distance from one line of text to the next.</summary>
    public int LineHeight => TTF_GetFontLineSkip(_regular.Font);

    /// <summary>Changes the size of the text, in pixels; nothing is done when it is the same.</summary>
    public void SetSize(float size)
    {
        if (size == _size)
        {
            return;
        }

        SdlException.ThrowIfFalse(TTF_SetFontSize(_regular.Font, size), "TTF_SetFontSize");
        SdlException.ThrowIfFalse(TTF_SetFontSize(_semiBold.Font, size), "TTF_SetFontSize");
        _size = size;
    }

    /// <summary>How wide the text is, in pixels, in the regular face or the heading face.</summary>
    public float Measure(string text, bool heading = false)
    {
        int width;
        nuint length;
        return TTF_MeasureString(FontFor(heading), text, 0, 0, &width, &length) ? width : 0;
    }

    /// <summary>Draws the text with its top left corner at the given point.</summary>
    public void Draw(string text, float x, float y, SDL_Color color, bool heading = false)
    {
        if (text.Length == 0)
        {
            return;
        }

        var drawn = SdlException.ThrowIfNull(TTF_CreateText(_engine, FontFor(heading), text, 0), "TTF_CreateText");
        try
        {
            TTF_SetTextColor(drawn, color.r, color.g, color.b, color.a);
            SdlException.ThrowIfFalse(TTF_DrawRendererText(drawn, x, y), "TTF_DrawRendererText");
        }
        finally
        {
            TTF_DestroyText(drawn);
        }
    }

    private TTF_Font* FontFor(bool heading) => heading ? _semiBold.Font : _regular.Font;

    /// <summary>Opens one of the fonts built into the viewer (see the project file).</summary>
    private static Face Open(string fileName, float size)
    {
        using var stream = typeof(TextRenderer).Assembly.GetManifestResourceStream($"ComiCraft.Viewer.Fonts.{fileName}")
            ?? throw new SdlException($"The font {fileName} is missing from the viewer.");

        // the font is copied to memory of its own: SDL_ttf reads it for as long as the font is open
        void* data = NativeMemory.Alloc((nuint)stream.Length);
        try
        {
            stream.ReadExactly(new Span<byte>(data, (int)stream.Length));
            var io = SdlException.ThrowIfNull(SDL_IOFromConstMem((IntPtr)data, (nuint)stream.Length), "SDL_IOFromConstMem");
            var font = SdlException.ThrowIfNull(TTF_OpenFontIO(io, true, size), "TTF_OpenFontIO");
            return new Face { Font = font, Data = data };
        }
        catch
        {
            NativeMemory.Free(data);
            throw;
        }
    }

    private static void Close(ref Face face)
    {
        if (face.Font is not null)
        {
            TTF_CloseFont(face.Font);
        }

        NativeMemory.Free(face.Data);
        face = default;
    }

    /// <summary>Closes the fonts; it must be done before the renderer the text was drawn with is destroyed.</summary>
    public void Dispose()
    {
        if (_engine is null)
        {
            return;
        }

        TTF_DestroyRendererTextEngine(_engine);
        _engine = null;
        Close(ref _regular);
        Close(ref _semiBold);
        TTF_Quit();
    }
}
