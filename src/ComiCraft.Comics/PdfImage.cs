using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace ComiCraft.Comics;

/// <summary>
/// A page as a PDF image: the entries of its dictionary (all but its size and length), its data,
/// and, for a page that is partly transparent, its mask. A PDF can hold a JPEG as it is, and most
/// PNGs too, with nothing decoded or encoded again, so nothing about them changes. Anything else
/// (a WebP, a GIF, a BMP, or a PNG or JPEG of a kind a PDF cannot hold) is decoded to its pixels
/// and kept as they are, compressed without loss.
/// </summary>
internal sealed record PdfImage(int Width, int Height, string Dictionary, byte[] Data, PdfImage? Mask = null)
{
    /// <summary>The page as a PDF image, decoded with <paramref name="decode"/> only when it must be.</summary>
    public static PdfImage Of(byte[] image, Func<ReadOnlyMemory<byte>, Pixels> decode) =>
        FromJpeg(image) ?? FromPng(image) ?? FromPixels(decode(image));

    /// <summary>
    /// A JPEG, kept as it is: a baseline, extended or progressive one, of 8 bits, in gray, RGB
    /// or CMYK. Photoshop's CMYK JPEGs (marked by an Adobe segment) hold their inks inverted, and
    /// are drawn with them inverted back, as every reader of them does.
    /// </summary>
    internal static PdfImage? FromJpeg(byte[] jpeg)
    {
        ReadOnlySpan<byte> bytes = jpeg;
        if (bytes is not [0xFF, 0xD8, ..])
        {
            return null;
        }

        bool adobe = false;
        (int Width, int Height, int Components)? frame = null;
        for (int at = 2; at + 4 <= bytes.Length;)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            byte marker = bytes[at + 1];
            if (marker == 0xFF)
            {
                at++; // a marker may be padded with any number of 0xFF before it
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                at += 2; // markers that stand alone, with no length
                continue;
            }

            if (marker is 0xD9)
            {
                return null; // the end, with no image
            }

            if (marker is 0xDA)
            {
                break; // the image's data begins: everything about it has been said
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
            if (length < 2 || at + 2 + length > bytes.Length)
            {
                return null;
            }

            var segment = bytes.Slice(at + 4, length - 2);
            if (marker == 0xEE && segment.StartsWith("Adobe"u8))
            {
                adobe = true;
            }
            else if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                // a PDF's DCTDecode is Huffman coded, 8 bits, with the size given in the frame
                if (segment.Length < 6 || segment[0] != 8)
                {
                    return null;
                }

                frame = (BinaryPrimitives.ReadUInt16BigEndian(segment[3..]), BinaryPrimitives.ReadUInt16BigEndian(segment[1..]), segment[5]);
            }
            else if (marker is >= 0xC3 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return null; // lossless, hierarchical or arithmetic coded: decoded instead
            }

            at += 2 + length;
        }

        if (frame is not ({ } width and > 0, { } height and > 0, { } components))
        {
            return null;
        }

        string? space = components switch
        {
            1 => "/DeviceGray",
            3 => "/DeviceRGB",
            4 => "/DeviceCMYK",
            _ => null,
        };
        if (space is null)
        {
            return null;
        }

        string decode = components == 4 && adobe ? " /Decode [1 0 1 0 1 0 1 0]" : "";
        return new PdfImage(width, height, $"/ColorSpace {space} /BitsPerComponent 8 /Filter /DCTDecode{decode}", jpeg);
    }

    /// <summary>
    /// A PNG, kept as it is: its compressed rows go into the PDF unchanged, which reads them the
    /// way a PNG reader does. That is so for a PNG in gray, RGB or a palette, not interlaced, with
    /// no transparency; one with transparency is decoded, for its transparency to go in a mask.
    /// </summary>
    internal static PdfImage? FromPng(byte[] png)
    {
        ReadOnlySpan<byte> bytes = png;
        if (!bytes.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return null;
        }

        int width = 0, height = 0, depth = 0, colorType = -1;
        byte[]? palette = null;
        var data = new MemoryStream();
        for (int at = 8; ;)
        {
            if (at + 12 > bytes.Length)
            {
                return null; // it ends before its end
            }

            int length = BinaryPrimitives.ReadInt32BigEndian(bytes[at..]);
            if (length < 0 || at + 12L + length > bytes.Length)
            {
                return null;
            }

            var type = bytes.Slice(at + 4, 4);
            var chunk = bytes.Slice(at + 8, length);
            at += 12 + length;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (chunk.Length < 13 || chunk[10] != 0 || chunk[11] != 0 || chunk[12] != 0)
                {
                    return null; // an unknown compression or filtering, or interlaced
                }

                (width, height, depth, colorType) = (BinaryPrimitives.ReadInt32BigEndian(chunk), BinaryPrimitives.ReadInt32BigEndian(chunk[4..]), chunk[8], chunk[9]);
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = chunk.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                return null; // transparent, so decoded
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                data.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (width <= 0 || height <= 0 || data.Length == 0)
        {
            return null;
        }

        (string? space, int colors) = (colorType, depth) switch
        {
            (0, 1 or 2 or 4 or 8 or 16) => ("/DeviceGray", 1),
            (2, 8 or 16) => ("/DeviceRGB", 3),
            (3, 1 or 2 or 4 or 8) when palette is { Length: > 0 and <= 768 } && palette.Length % 3 == 0 =>
                ($"[/Indexed /DeviceRGB {palette.Length / 3 - 1} <{Convert.ToHexString(palette)}>]", 1),
            _ => (null, 0), // with alpha, or not as the PNG specification allows
        };
        if (space is null)
        {
            return null;
        }

        return new PdfImage(width, height, string.Create(CultureInfo.InvariantCulture,
            $"/ColorSpace {space} /BitsPerComponent {depth} /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors {colors} /BitsPerComponent {depth} /Columns {width} >>"),
            data.ToArray());
    }

    /// <summary>
    /// Decoded pixels, compressed without loss: in gray where every pixel is gray, as most of a
    /// black and white comic's are, and otherwise in RGB; with a mask only where some are transparent.
    /// </summary>
    internal static PdfImage FromPixels(Pixels pixels)
    {
        var rgba = pixels.Rgba;
        int count = pixels.Width * pixels.Height;
        bool gray = true, opaque = true;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            gray &= rgba[i] == rgba[i + 1] && rgba[i] == rgba[i + 2];
            opaque &= rgba[i + 3] == 255;
        }

        byte[] color = new byte[count * (gray ? 1 : 3)];
        for (int p = 0, c = 0; p < count; p++)
        {
            color[c++] = rgba[4 * p];
            if (!gray)
            {
                color[c++] = rgba[4 * p + 1];
                color[c++] = rgba[4 * p + 2];
            }
        }

        PdfImage? mask = null;
        if (!opaque)
        {
            byte[] alpha = new byte[count];
            for (int p = 0; p < count; p++)
            {
                alpha[p] = rgba[4 * p + 3];
            }

            mask = new PdfImage(pixels.Width, pixels.Height, "/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", Deflate(alpha));
        }

        return new PdfImage(pixels.Width, pixels.Height, $"/ColorSpace {(gray ? "/DeviceGray" : "/DeviceRGB")} /BitsPerComponent 8 /Filter /FlateDecode", Deflate(color), mask);
    }

    /// <summary>Bytes compressed as a PDF's FlateDecode reads them: zlib's format.</summary>
    private static byte[] Deflate(byte[] bytes)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(bytes);
        }

        return compressed.ToArray();
    }

    /// <summary>What the image's data must be, for the PDF to be the comic: its data and its mask's, hashed.</summary>
    public string Fingerprint => Fingerprints.Of(Data, Mask?.Data);
}

/// <summary>A page's image data, hashed, as the PDF holds it, so that it can be checked when read back.</summary>
internal static class Fingerprints
{
    public static string Of(ReadOnlySpan<byte> data, ReadOnlySpan<byte> mask) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)) + "/" +
        (mask.IsEmpty ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mask)));

    public static string Of(byte[] data, byte[]? mask) => Of((ReadOnlySpan<byte>)data, mask is null ? default : (ReadOnlySpan<byte>)mask);
}
