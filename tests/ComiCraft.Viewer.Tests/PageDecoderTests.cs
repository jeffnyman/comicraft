using ComiCraft.Comics.Tests;
using ComiCraft.Viewer.Sdl;

namespace ComiCraft.Viewer.Tests;

/// <summary>Pages decoded by SDL_image, as a PDF needs those it cannot hold as they are; no window is needed.</summary>
public class PageDecoderTests
{
    [Fact]
    public void A_bmp_is_decoded_to_its_pixels_from_the_top()
    {
        byte[] rgb = [255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30];

        var pixels = PageDecoder.Decode(TestImages.Bmp(2, 2, rgb));

        Assert.Equal(2, pixels.Width);
        Assert.Equal(2, pixels.Height);
        Assert.Equal([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 10, 20, 30, 255], pixels.Rgba);
    }

    [Fact]
    public void What_is_not_an_image_is_an_error()
    {
        Assert.Throws<SdlException>(() => PageDecoder.Decode("not an image"u8.ToArray()));
    }
}
