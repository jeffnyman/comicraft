namespace ComiCraft.Comics;

/// <summary>
/// A page decoded to its pixels: four bytes to each, red, green, blue and alpha, row after row
/// from the top. Pages that cannot go into a PDF as they are (WebP, GIF, BMP) go in as these.
/// </summary>
public sealed record Pixels
{
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

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }
}
