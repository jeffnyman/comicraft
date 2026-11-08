using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace ComiCraft.Comics;

/// <summary>
/// A PDF read as a comic. A PDF can hold anything a printed page can, but a comic's holds a picture
/// to each page, as the PDFs ComiCraft makes do, and as most scanned comics' do. Such a page is
/// read as its picture, taken out as it is: a JPEG as the JPEG the PDF holds, and anything else as
/// a PNG of the same pixels, so nothing is lost and nothing has to be drawn. A page that is more
/// than a picture (one with text on it, or made of several pictures, or of none) could only be
/// shown by drawing the PDF, which ComiCraft does not do; it says why it is not shown instead.
/// The PDF's ComicInfo.xml, when it holds one as an attachment, as ComiCraft's do, is read too.
/// </summary>
internal static class PdfComic
{
    /// <summary>Whether the file starts as a PDF does.</summary>
    public static bool IsPdf(ReadOnlySpan<byte> start) => start.StartsWith("%PDF-"u8);

    /// <summary>
    /// The comic in the PDF. Each page is looked at when the PDF is opened, to find whether it is a
    /// picture, but its picture is only taken out when it is first asked for, so a PDF opens at
    /// once however many pages it has. A PDF none of whose pages is a picture is not a comic.
    /// </summary>
    public static ComicBook Read(byte[] pdf, string name)
    {
        // the document is kept, unclosed, by the pages still to be read; read from memory, it holds nothing else
        var document = PdfDocument.Open(pdf);
        var pictures = new IPdfImage?[document.NumberOfPages];
        var problems = new string?[document.NumberOfPages];
        for (int i = 0; i < pictures.Length; i++)
        {
            try
            {
                (pictures[i], problems[i]) = PictureOf(document.GetPage(i + 1));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                problems[i] = $"it could not be read: {ex.Message}";
            }
        }

        if (pictures.Length > 0 && pictures.All(p => p is null))
        {
            throw new ComicEditException(pictures.Length == 1
                ? $"its page is not a picture, as a comic's are: {problems[0]}."
                : $"none of its {pictures.Length} pages is a picture, as a comic's are; the first is not because {problems[0]}.");
        }

        int digits = Math.Max(3, pictures.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        // named as a page of a CBZ would be, for what it comes out as: a JPEG, or a PNG
        var names = Enumerable.Range(0, pictures.Length)
            .Select(i => "Page " + (i + 1).ToString("D" + digits, System.Globalization.CultureInfo.InvariantCulture) + pictures[i] switch
            {
                null => "",
                var picture when IsJpeg(picture) => ".jpg",
                _ => ".png",
            })
            .ToList();

        return ComicBook.FromPdf(name, ComicInfoOf(document), names, index =>
        {
            if (pictures[index] is not { } picture)
            {
                return ([], problems[index]);
            }

            try
            {
                return ImageOf(picture) is { } image
                    ? (image, null)
                    : ([], $"its picture is in a form ComiCraft cannot read ({Describe(picture)})");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return ([], $"its picture could not be read: {ex.Message}");
            }
        });
    }

    /// <summary>The ComicInfo.xml attached to the PDF, as it is, byte for byte; null when it has none.</summary>
    public static byte[]? ComicInfoBytes(string path)
    {
        using var document = PdfDocument.Open(path);
        return ComicInfoBytes(document);
    }

    private static byte[]? ComicInfoBytes(PdfDocument document) =>
        document.Advanced.TryGetEmbeddedFiles(out var files) && files.FirstOrDefault(f => ComicInfo.IsComicInfo(f.Name)) is { } file
            ? file.Memory.ToArray()
            : null;

    private static ComicInfo? ComicInfoOf(PdfDocument document)
    {
        try
        {
            return ComicInfoBytes(document) is { } xml ? ComicInfo.Parse(new MemoryStream(xml)) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // a PDF whose attachments cannot be read is still a comic, without its details
            return null;
        }
    }

    /// <summary>The page's picture, when the page is nothing but a picture the right way up; otherwise why it is not.</summary>
    private static (IPdfImage? Picture, string? Problem) PictureOf(Page page)
    {
        // text that is drawn, that is, other than the hidden text that makes a scan searchable
        if (page.Letters.Any(l => l.RenderingMode is not (TextRenderingMode.Neither or TextRenderingMode.NeitherClip)))
        {
            return (null, "it has text on it, not only a picture");
        }

        var images = page.GetImages().ToList();
        if (images.Count == 0)
        {
            return (null, "it has no picture on it");
        }

        if (images.Count > 1)
        {
            return (null, $"it is made of {images.Count} pictures, not one");
        }

        var picture = images[0];
        if (picture.IsImageMask)
        {
            return (null, "its picture is a stencil, which takes its color from the page");
        }

        if (page.Rotation.Value != 0 || !Upright(picture.BoundingBox))
        {
            return (null, "its picture is turned or flipped, which ComiCraft cannot yet show");
        }

        return (picture, null);
    }

    /// <summary>Whether the picture is drawn as it is: its top at the top, its left at the left, and not turned.</summary>
    private static bool Upright(PdfRectangle box)
    {
        const double Near = 0.01;
        return box.TopLeft.Y > box.BottomLeft.Y && box.TopRight.X > box.TopLeft.X
            && Math.Abs(box.TopLeft.X - box.BottomLeft.X) < Near && Math.Abs(box.TopLeft.Y - box.TopRight.Y) < Near;
    }

    /// <summary>
    /// The picture as an image the viewer can show: a JPEG as it is, and anything else as a PNG of
    /// its pixels. Null when it is in a form that cannot be read.
    /// </summary>
    internal static byte[]? ImageOf(IPdfImage picture)
    {
        if (IsJpeg(picture))
        {
            return picture.RawMemory.ToArray();
        }

        var filters = Filters(picture.ImageDictionary);
        if (filters is not null && !picture.ImageDictionary.ContainsKey(NameToken.Decode) && Kind(picture) is { } kind)
        {
            if (Predicted(picture, filters, kind) is { } png)
            {
                return png;
            }

            if (Repacked(picture, kind) is { } repacked)
            {
                return repacked;
            }
        }

        return picture.TryGetPng(out var other) ? other : null;
    }

    /// <summary>How a picture's pixels are held: a PNG's color type, and the number of samples to each pixel; null for any other way.</summary>
    private readonly record struct PixelKind(byte ColorType, int Samples, byte[] Palette);

    private static PixelKind? Kind(IPdfImage picture)
    {
        static int? Channels(ColorSpaceDetails? space) => space switch
        {
            null => null,
            { Type: ColorSpace.DeviceGray or ColorSpace.CalGray } => 1,
            { Type: ColorSpace.DeviceRGB or ColorSpace.CalRGB } => 3,
            { Type: ColorSpace.ICCBased, NumberOfColorComponents: 1 or 3 } => space.NumberOfColorComponents,
            _ => null,
        };

        int bits = picture.BitsPerComponent;
        var space = picture.ColorSpaceDetails;
        if (space is IndexedColorSpaceDetails indexed)
        {
            int colors = indexed.HiVal + 1;
            if (Channels(indexed.BaseColorSpace) != 3 || bits is not (1 or 2 or 4 or 8) || indexed.ColorTable.Length < 3 * colors)
            {
                return null;
            }

            return new PixelKind(Png.Palette, 1, indexed.ColorTable[..(3 * colors)].ToArray());
        }

        return Channels(space) switch
        {
            1 when bits is 1 or 2 or 4 or 8 or 16 => new PixelKind(Png.Gray, 1, []),
            3 when bits is 8 or 16 => new PixelKind(Png.Rgb, 3, []),
            _ => null,
        };
    }

    /// <summary>
    /// A picture compressed with zlib alone, its rows filtered as a PNG's are, goes into a PNG as
    /// it is, without being decompressed: that is how ComiCraft's PDFs hold a PNG, and others'
    /// often do. Not when it has a mask, which a PNG holds in among its pixels instead.
    /// </summary>
    private static byte[]? Predicted(IPdfImage picture, List<string> filters, PixelKind kind)
    {
        if (filters is not ["FlateDecode" or "Fl"] || picture.MaskImage is not null ||
            !picture.ImageDictionary.TryGet(NameToken.DecodeParms, out DictionaryToken parameters))
        {
            return null;
        }

        int Number(NameToken key, int otherwise) =>
            parameters.TryGet(key, out NumericToken number) ? number.Int : otherwise;

        if (Number(NameToken.Predictor, 1) < 10 || Number(NameToken.Colors, 1) != kind.Samples ||
            Number(NameToken.BitsPerComponent, 8) != picture.BitsPerComponent || Number(NameToken.Columns, 1) != picture.WidthInSamples)
        {
            return null;
        }

        return Png.Of(picture.WidthInSamples, picture.HeightInSamples, picture.BitsPerComponent, kind.ColorType, picture.RawMemory.Span, kind.Palette);
    }

    /// <summary>
    /// Any other picture of those kinds, decompressed and compressed again as a PNG; with its
    /// mask, when it has one of 8 bits as large as itself, as its alpha.
    /// </summary>
    private static byte[]? Repacked(IPdfImage picture, PixelKind kind)
    {
        int width = picture.WidthInSamples, height = picture.HeightInSamples, bits = picture.BitsPerComponent;
        long rowLength = ((long)width * kind.Samples * bits + 7) / 8;
        if (!picture.TryGetBytesAsMemory(out var decoded) || decoded.Length < rowLength * height)
        {
            return null;
        }

        var samples = decoded.Span[..(int)(rowLength * height)];
        if (Alpha(picture, kind) is { } alpha)
        {
            int channels = kind.Samples + 1;
            byte[] pixels = new byte[(long)width * height * channels];
            for (long p = 0; p < (long)width * height; p++)
            {
                samples.Slice((int)(p * kind.Samples), kind.Samples).CopyTo(pixels.AsSpan((int)(p * channels)));
                pixels[p * channels + kind.Samples] = alpha[(int)p];
            }

            byte colorType = kind.ColorType == Png.Gray ? Png.GrayAlpha : Png.RgbAlpha;
            return Png.Of(width, height, 8, colorType, Png.Compress(pixels, width * channels));
        }

        return Png.Of(width, height, bits, kind.ColorType, Png.Compress(samples, (int)rowLength), kind.Palette);
    }

    /// <summary>The picture's mask, a byte to each pixel, when it can be its alpha: 8 bits, as large as it, over 8-bit gray or RGB.</summary>
    private static byte[]? Alpha(IPdfImage picture, PixelKind kind)
    {
        if (picture.MaskImage is not { } mask || kind.ColorType == Png.Palette || picture.BitsPerComponent != 8 ||
            mask.BitsPerComponent != 8 || mask.WidthInSamples != picture.WidthInSamples || mask.HeightInSamples != picture.HeightInSamples ||
            mask.ImageDictionary.ContainsKey(NameToken.Decode) || !mask.TryGetBytesAsMemory(out var alpha) ||
            alpha.Length < (long)picture.WidthInSamples * picture.HeightInSamples)
        {
            return null;
        }

        return alpha.ToArray();
    }

    /// <summary>Whether the picture is a JPEG, and nothing else: so it is taken out as it is.</summary>
    private static bool IsJpeg(IPdfImage picture) => Filters(picture.ImageDictionary) is ["DCTDecode" or "DCT"];

    /// <summary>The names of the filters the picture's data is compressed with, in order; null when they cannot be told.</summary>
    private static List<string>? Filters(DictionaryToken dictionary)
    {
        if (!dictionary.TryGet(NameToken.Filter, out var filter) && !dictionary.TryGet(NameToken.F, out filter))
        {
            return [];
        }

        return filter switch
        {
            NameToken name => [name.Data],
            ArrayToken array when array.Data.All(t => t is NameToken) => array.Data.Cast<NameToken>().Select(n => n.Data).ToList(),
            _ => null,
        };
    }

    /// <summary>What the picture is, for saying why it cannot be read: its filters and colors.</summary>
    private static string Describe(IPdfImage picture)
    {
        var filters = Filters(picture.ImageDictionary);
        string compressed = filters is { Count: > 0 } ? string.Join(", ", filters) : "uncompressed";
        string colors = picture.ColorSpaceDetails?.Type.ToString() ?? "no colors";
        return $"{compressed}, {colors}, {picture.BitsPerComponent} bits";
    }
}
