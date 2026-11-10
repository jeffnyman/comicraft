namespace ComiCraft.Comics;

/// <summary>
/// A page decoded to its pixels: four bytes to each, red, green, blue and alpha, row after row
/// from the top. Pages that cannot go into a PDF as they are (WebP, GIF, BMP) go in as these.
/// </summary>
public sealed record Pixels
{
    /// <summary>The pixels of a page <paramref name="width"/> by <paramref name="height"/>, with <paramref name="rgba"/> exactly four bytes to each.</summary>
    public Pixels(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.LongLength != 4L * width * height)
        {
            throw new ArgumentException($"{width}×{height} pixels are {4L * width * height} bytes, not {rgba.LongLength}.", nameof(rgba));
        }

        (Width, Height, Rgba) = (width, height, rgba);
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>Red, green, blue and alpha bytes for each pixel, row after row from the top.</summary>
    public byte[] Rgba { get; }
}
