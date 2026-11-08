using ComiCraft.Comics;
using SDL;
using static SDL.SDL3;
using static SDL.SDL3_image;

namespace ComiCraft.Viewer.Sdl;

/// <summary>
/// Decodes a page to its pixels with SDL_image, which reads every image the viewer shows: so a
/// page that can be read can be put in a PDF. It needs no window, and no display.
/// </summary>
internal static unsafe class PageDecoder
{
    public static Pixels Decode(ReadOnlyMemory<byte> image)
    {
        using var pin = image.Pin();
        var io = SdlException.ThrowIfNull(SDL_IOFromConstMem((IntPtr)pin.Pointer, (nuint)image.Length), "SDL_IOFromConstMem");
        var loaded = Loaded(IMG_Load_IO(io, true));
        try
        {
            // RGBA32 is the bytes red, green, blue, alpha in that order, whatever the machine's byte order
            var rgba = SdlException.ThrowIfNull(SDL_ConvertSurface(loaded, SDL_PIXELFORMAT_RGBA32), "SDL_ConvertSurface");
            try
            {
                int width = rgba->w, height = rgba->h, row = width * 4;
                byte[] pixels = new byte[row * height];
                for (int y = 0; y < height; y++)
                {
                    new ReadOnlySpan<byte>((byte*)rgba->pixels + (long)y * rgba->pitch, row).CopyTo(pixels.AsSpan(y * row, row));
                }

                return new Pixels(width, height, pixels);
            }
            finally
            {
                SDL_DestroySurface(rgba);
            }
        }
        finally
        {
            SDL_DestroySurface(loaded);
        }
    }

    private static SDL_Surface* Loaded(SDL_Surface* surface) =>
        surface is not null ? surface : throw new SdlException($"the image could not be read: {SDL_GetError()}");
}
