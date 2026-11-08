using System.Buffers.Binary;
using System.IO.Compression;

namespace ComiCraft.Comics.Tests;

/// <summary>Small images made in memory, of the kinds comics' pages are, so the tests need no image files.</summary>
internal static class TestImages
{
    /// <summary>
    /// The start of a JPEG, as far as a PDF needs to know it (its frame: size, precision and
    /// colors), followed by a scan that is not a real one; an Adobe segment when asked.
    /// </summary>
    public static byte[] Jpeg(int width, int height, int components = 3, bool adobe = false, byte precision = 8, byte frame = 0xC0)
    {
        var jpeg = new MemoryStream();
        jpeg.Write([0xFF, 0xD8]);
        Segment(jpeg, 0xE0, "JFIF\0\u0001\u0001\0\0\u0001\0\u0001\0\0"u8);
        if (adobe)
        {
            Segment(jpeg, 0xEE, "Adobe\0d\0\0\0\0\u0002"u8);
        }

        byte[] sof = new byte[6 + 3 * components];
        sof[0] = precision;
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(1), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(3), (ushort)width);
        sof[5] = (byte)components;
        Segment(jpeg, frame, sof);
        Segment(jpeg, 0xDA, [1, 1, 0, 0, 63, 0]);
        jpeg.Write([0x12, 0x34, 0x56, 0xFF, 0xD9]);
        return jpeg.ToArray();
    }

    private static void Segment(Stream jpeg, byte marker, ReadOnlySpan<byte> body)
    {
        jpeg.Write([0xFF, marker, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2)]);
        jpeg.Write(body);
    }

    /// <summary>
    /// A PNG of the given rows (each a row's samples, without the filter byte, which is added:
    /// none), in the given color type and depth, with a palette and transparency when given.
    /// </summary>
    public static byte[] Png(int width, int height, byte colorType, byte depth, byte[][] rows, byte[]? palette = null, byte[]? transparency = null, bool interlaced = false)
    {
        var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        (header[8], header[9], header[12]) = (depth, colorType, (byte)(interlaced ? 1 : 0));
        Chunk(png, "IHDR", header);
        if (palette is not null)
        {
            Chunk(png, "PLTE", palette);
        }

        if (transparency is not null)
        {
            Chunk(png, "tRNS", transparency);
        }

        var raw = new MemoryStream();
        foreach (byte[] row in rows)
        {
            raw.WriteByte(0);
            raw.Write(row);
        }

        // split in two, as encoders often do, to show the parts are joined
        byte[] data = Deflate(raw.ToArray());
        Chunk(png, "IDAT", data[..(data.Length / 2)]);
        Chunk(png, "IDAT", data[(data.Length / 2)..]);
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, TestComics.Crc32(typed));
        png.Write(number);
    }

    public static byte[] Deflate(byte[] bytes)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(bytes);
        }

        return compressed.ToArray();
    }

    /// <summary>A BMP of 24 bits, of the given pixels (red, green, blue, row after row from the top).</summary>
    public static byte[] Bmp(int width, int height, byte[] rgb)
    {
        int stride = (width * 3 + 3) & ~3;
        var bmp = new MemoryStream();
        var write = new BinaryWriter(bmp);
        write.Write("BM"u8);
        write.Write(54 + stride * height);
        write.Write(0);
        write.Write(54);
        write.Write(40);
        write.Write(width);
        write.Write(height); // positive: the rows go from the bottom up
        write.Write((short)1);
        write.Write((short)24);
        write.Write(0);
        write.Write(stride * height);
        write.Write(2835);
        write.Write(2835);
        write.Write(0);
        write.Write(0);
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                int at = 3 * (y * width + x);
                write.Write([rgb[at + 2], rgb[at + 1], rgb[at]]);
            }

            write.Write(new byte[stride - width * 3]);
        }

        return bmp.ToArray();
    }
}
