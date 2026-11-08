using System.Buffers.Binary;
using System.IO.Compression;

namespace ComiCraft.Comics;

/// <summary>
/// Writing a PNG, for a PDF's page that is not a JPEG. A PDF and a PNG compress pixels in the same
/// way (zlib, with each row filtered as a PNG's are, when the PDF says so), so a page that a PDF
/// holds that way goes into a PNG as it is; any other is compressed again, without loss.
/// </summary>
internal static class Png
{
    /// <summary>A PNG's color types: gray, RGB, a palette, and gray and RGB with alpha.</summary>
    public const byte Gray = 0, Rgb = 2, Palette = 3, GrayAlpha = 4, RgbAlpha = 6;

    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>A PNG of the image whose rows, each led by its filter byte, are compressed in <paramref name="zlib"/>.</summary>
    public static byte[] Of(int width, int height, int depth, byte colorType, ReadOnlySpan<byte> zlib, ReadOnlySpan<byte> palette = default)
    {
        using var png = new MemoryStream(zlib.Length + 1024);
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        (header[8], header[9], header[10], header[11], header[12]) = ((byte)depth, colorType, 0, 0, 0);
        Chunk(png, "IHDR"u8, header);
        if (!palette.IsEmpty)
        {
            Chunk(png, "PLTE"u8, palette);
        }

        // a chunk holds at most 2³¹ − 1 bytes; a page's are far fewer, but the data is split all the same
        const int Most = 1 << 30;
        for (int at = 0; at < zlib.Length; at += Most)
        {
            Chunk(png, "IDAT"u8, zlib.Slice(at, Math.Min(Most, zlib.Length - at)));
        }

        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    /// <summary>The rows, each <paramref name="rowLength"/> bytes, led by a filter byte of none, and compressed.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> rows, int rowLength)
    {
        using var compressed = new MemoryStream(rows.Length / 2);
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int at = 0; at + rowLength <= rows.Length; at += rowLength)
            {
                zlib.WriteByte(0);
                zlib.Write(rows.Slice(at, rowLength));
            }
        }

        return compressed.ToArray();
    }

    private static void Chunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(type);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~Crc(Crc(0xFFFFFFFF, type), data));
        png.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
