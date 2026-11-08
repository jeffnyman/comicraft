using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;

namespace ComiCraft.Comics.Tests;

/// <summary>Comics converted to PDFs, and read back by PdfPig, a reader of PDFs that is not ComiCraft's.</summary>
public sealed class ComicPdfTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-pdf-").FullName;
    private readonly List<int> _decoded = [];

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private const string Info = "<?xml version=\"1.0\"?>\n<ComicInfo>\n  <Series>Operation: Knightstrike</Series>\n  <Number>3</Number>\n  <Title>Enter... Bloodstrike</Title>\n  <Year>1995</Year>\n  <Month>7</Month>\n  <Writer>Brian Witten</Writer>\n  <Summary>Bloodstrike arrives.</Summary>\n  <Genre>Superhero, Action</Genre>\n</ComicInfo>";

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 6, 30, 0, TimeSpan.FromHours(-6));

    /// <summary>A decoder that gives the pixels it was given, for any page, and counts the pages it decoded.</summary>
    private Func<ReadOnlyMemory<byte>, Pixels> Decoder(Pixels pixels) => image =>
    {
        _decoded.Add(image.Length);
        return pixels;
    };

    private static Pixels NeverDecoded(ReadOnlyMemory<byte> image) => throw new InvalidOperationException("nothing should be decoded");

    private string Comic(params (string Name, byte[] Bytes)[] entries) => TestComics.CbzFile(_folder, "comic.cbz", entries);

    private PdfConversion Convert(string comic, Func<ReadOnlyMemory<byte>, Pixels>? decode = null) =>
        ComicPdf.ConvertToPdf(comic, null, decode ?? NeverDecoded, Now);

    private static IPdfImage ImageOf(PdfDocument pdf, int page) => Assert.Single(pdf.GetPage(page).GetImages());

    private static byte[] Decoded(IPdfImage image)
    {
        Assert.True(image.TryGetBytesAsMemory(out var bytes));
        return bytes.ToArray();
    }

    private string[] Files() => Directory.EnumerateFiles(_folder).Select(Path.GetFileName).Order().ToArray()!;

    [Fact]
    public void Jpegs_go_in_as_they_are_a_page_each_in_order()
    {
        byte[] cover = TestImages.Jpeg(2000, 3000), spread = TestImages.Jpeg(4000, 3000);
        string comic = Comic(("10.jpg", spread), ("2.jpg", cover));

        var conversion = Convert(comic);

        Assert.Equal(Path.Combine(_folder, "comic.pdf"), conversion.Path);
        using var pdf = PdfDocument.Open(conversion.Path);
        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal(cover, ImageOf(pdf, 1).RawMemory.ToArray());
        Assert.Equal(spread, ImageOf(pdf, 2).RawMemory.ToArray());
    }

    [Fact]
    public void A_page_is_as_high_as_a_printed_comic_and_as_wide_as_its_shape_makes_it()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(975, 1500)), ("2.jpg", TestImages.Jpeg(3000, 2300)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);

        Assert.Equal(738, pdf.GetPage(1).Height, 0.01);
        Assert.Equal(738 * 975 / 1500.0, pdf.GetPage(1).Width, 0.01);
        Assert.Equal(738, pdf.GetPage(2).Height, 0.01);
        Assert.Equal(738 * 3000 / 2300.0, pdf.GetPage(2).Width, 0.01);
    }

    [Fact]
    public void The_image_fills_its_page()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(1000, 1500)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);
        var bounds = ImageOf(pdf, 1).BoundingBox;

        Assert.Equal(0, bounds.Left, 0.01);
        Assert.Equal(0, bounds.Bottom, 0.01);
        Assert.Equal(pdf.GetPage(1).Width, bounds.Width, 0.01);
        Assert.Equal(pdf.GetPage(1).Height, bounds.Height, 0.01);
    }

    [Fact]
    public void The_pdfs_details_come_from_the_comics()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)), ("ComicInfo.xml", Encoding.UTF8.GetBytes(Info)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);

        Assert.Equal("Operation: Knightstrike #3 – Enter... Bloodstrike (July 1995)", pdf.Information.Title);
        Assert.Equal("Brian Witten", pdf.Information.Author);
        Assert.Equal("Bloodstrike arrives.", pdf.Information.Subject);
        Assert.Equal("Superhero, Action", pdf.Information.Keywords);
        Assert.Equal("ComiCraft", pdf.Information.Creator);
        Assert.Equal("D:20261108063000-06'00'", pdf.Information.CreationDate);
    }

    [Fact]
    public void The_comic_info_goes_in_byte_for_byte_as_an_attachment()
    {
        byte[] xml = Encoding.UTF8.GetBytes(Info);
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)), ("ComicInfo.xml", xml));

        using var pdf = PdfDocument.Open(Convert(comic).Path);

        Assert.True(pdf.Advanced.TryGetEmbeddedFiles(out var files));
        var file = Assert.Single(files);
        Assert.Equal("ComicInfo.xml", file.Name);
        Assert.Equal(xml, file.Bytes.ToArray());
    }

    [Fact]
    public void A_comic_without_comic_info_is_titled_by_its_file_and_has_nothing_attached()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);

        Assert.Equal("comic", pdf.Information.Title);
        Assert.Null(pdf.Information.Author);
        Assert.False(pdf.Advanced.TryGetEmbeddedFiles(out var files) && files.Count > 0);
    }

    [Fact]
    public void A_cmyk_jpeg_from_photoshop_has_its_inverted_inks_inverted_back()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15, components: 4, adobe: true)), ("2.jpg", TestImages.Jpeg(10, 15, components: 4)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);

        Assert.Equal([1, 0, 1, 0, 1, 0, 1, 0], ImageOf(pdf, 1).Decode);
        Assert.Empty(ImageOf(pdf, 2).Decode); // no Decode: the inks are drawn as they are
    }

    [Theory]
    [InlineData(12, 0xC0)] // 12 bits
    [InlineData(8, 0xC3)]  // lossless
    [InlineData(8, 0xC9)]  // arithmetic coded
    public void A_jpeg_a_pdf_cannot_hold_is_decoded(byte precision, byte frame)
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(2, 1, precision: precision, frame: frame)));

        Convert(comic, Decoder(new Pixels(2, 1, [9, 9, 9, 255, 7, 7, 7, 255])));

        Assert.Single(_decoded);
    }

    [Fact]
    public void A_gray_png_goes_in_as_it_is_and_reads_as_its_pixels()
    {
        byte[][] rows = [[0, 128, 255], [10, 20, 30]];
        string comic = Comic(("1.png", TestImages.Png(3, 2, colorType: 0, depth: 8, rows)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);
        var image = ImageOf(pdf, 1);

        Assert.Equal(ColorSpace.DeviceGray, image.ColorSpaceDetails!.Type);
        Assert.Equal([0, 128, 255, 10, 20, 30], Decoded(image));
    }

    [Fact]
    public void An_rgb_png_goes_in_as_it_is()
    {
        byte[][] rows = [[255, 0, 0, 0, 255, 0], [0, 0, 255, 1, 2, 3]];
        string comic = Comic(("1.png", TestImages.Png(2, 2, colorType: 2, depth: 8, rows)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);
        var image = ImageOf(pdf, 1);

        Assert.Equal(ColorSpace.DeviceRGB, image.ColorSpaceDetails!.Type);
        Assert.Equal([255, 0, 0, 0, 255, 0, 0, 0, 255, 1, 2, 3], Decoded(image));
    }

    [Fact]
    public void A_png_with_a_palette_goes_in_with_its_palette()
    {
        byte[] palette = [255, 0, 0, 0, 0, 255];
        string comic = Comic(("1.png", TestImages.Png(2, 1, colorType: 3, depth: 8, [[1, 0]], palette)));

        using var pdf = PdfDocument.Open(Convert(comic).Path);
        var image = ImageOf(pdf, 1);

        Assert.Equal(ColorSpace.Indexed, image.ColorSpaceDetails!.Type);
    }

    [Fact]
    public void A_png_with_transparency_is_decoded_and_its_transparency_kept_as_a_mask()
    {
        string comic = Comic(("1.png", TestImages.Png(2, 1, colorType: 6, depth: 8, [[255, 0, 0, 255, 0, 0, 255, 128]])));

        var conversion = Convert(comic, Decoder(new Pixels(2, 1, [255, 0, 0, 255, 0, 0, 255, 128])));

        Assert.Single(_decoded);
        using var pdf = PdfDocument.Open(conversion.Path);
        var image = ImageOf(pdf, 1);
        Assert.Equal(ColorSpace.DeviceRGB, image.ColorSpaceDetails!.Type);
        Assert.Equal([255, 0, 0, 0, 0, 255], Decoded(image));
        Assert.NotNull(image.MaskImage);
        Assert.Equal([255, 128], Decoded(image.MaskImage));
    }

    [Theory]
    [InlineData(false, true)] // a palette with transparency
    [InlineData(true, false)] // interlaced
    public void A_png_a_pdf_cannot_hold_as_it_is_is_decoded(bool interlaced, bool transparent)
    {
        byte[] png = TestImages.Png(1, 1, colorType: 3, depth: 8, [[0]], palette: [1, 2, 3], transparency: transparent ? [0] : null, interlaced: interlaced);
        string comic = Comic(("1.png", png));

        Convert(comic, Decoder(new Pixels(1, 1, [1, 2, 3, 255])));

        Assert.Single(_decoded);
    }

    [Fact]
    public void Another_kind_of_image_is_decoded_and_kept_in_its_pixels()
    {
        string comic = Comic(("1.gif", "GIF89a..."u8.ToArray()));

        var conversion = Convert(comic, Decoder(new Pixels(2, 1, [10, 20, 30, 255, 40, 50, 60, 255])));

        using var pdf = PdfDocument.Open(conversion.Path);
        var image = ImageOf(pdf, 1);
        Assert.Equal(ColorSpace.DeviceRGB, image.ColorSpaceDetails!.Type);
        Assert.Equal([10, 20, 30, 40, 50, 60], Decoded(image));
        Assert.Null(image.MaskImage);
    }

    [Fact]
    public void Decoded_pixels_that_are_all_gray_are_kept_in_gray()
    {
        string comic = Comic(("1.webp", "RIFF....WEBP"u8.ToArray()));

        var conversion = Convert(comic, Decoder(new Pixels(2, 1, [10, 10, 10, 255, 200, 200, 200, 255])));

        using var pdf = PdfDocument.Open(conversion.Path);
        var image = ImageOf(pdf, 1);
        Assert.Equal(ColorSpace.DeviceGray, image.ColorSpaceDetails!.Type);
        Assert.Equal([10, 200], Decoded(image));
    }

    [Fact]
    public void A_page_that_cannot_be_decoded_stops_the_conversion_and_leaves_nothing()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)), ("2.gif", "not an image"u8.ToArray()));

        var error = Assert.Throws<ComicEditException>(() => Convert(comic, _ => throw new InvalidOperationException("unreadable")));

        Assert.Equal("page 2 (2.gif) cannot be put in a PDF: unreadable", error.Message);
        Assert.Equal(["comic.cbz"], Files());
    }

    [Fact]
    public void An_existing_file_is_never_written_over()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)));
        File.WriteAllText(Path.Combine(_folder, "comic.pdf"), "someone's file");

        Assert.Throws<ComicEditException>(() => Convert(comic));

        Assert.Equal("someone's file", File.ReadAllText(Path.Combine(_folder, "comic.pdf")));
    }

    [Fact]
    public void A_comic_is_not_converted_to_itself()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)));

        Assert.Throws<ComicEditException>(() => ComicPdf.ConvertToPdf(comic, comic, NeverDecoded));
    }

    [Fact]
    public void A_comic_with_no_pages_is_not_converted()
    {
        string comic = Comic(("ComicInfo.xml", Encoding.UTF8.GetBytes(Info)));

        var error = Assert.Throws<ComicEditException>(() => Convert(comic));

        Assert.Equal("'comic.cbz' has no pages to make a PDF of.", error.Message);
        Assert.Equal(["comic.cbz"], Files());
    }

    [Fact]
    public void A_pdf_read_back_has_its_pages()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)), ("2.png", TestImages.Png(1, 1, 0, 8, [[7]])), ("3.gif", "GIF"u8.ToArray()));

        var conversion = Convert(comic, Decoder(new Pixels(1, 1, [1, 2, 3, 128])));

        Assert.True(ComicPdf.HasItsPages(conversion));
    }

    [Fact]
    public void A_pdf_whose_image_has_changed_has_not_its_pages()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)));
        var conversion = Convert(comic);

        // the scan of the test JPEG is 12 34 56; one bit of it changes, and nothing else
        byte[] pdf = File.ReadAllBytes(conversion.Path);
        int at = pdf.AsSpan().IndexOf([(byte)0x12, (byte)0x34, (byte)0x56]);
        pdf[at + 1] ^= 1;
        File.WriteAllBytes(conversion.Path, pdf);

        Assert.False(ComicPdf.HasItsPages(conversion));
    }

    [Fact]
    public void A_pdf_with_too_few_pages_or_none_at_all_has_not_its_pages()
    {
        string comic = Comic(("1.jpg", TestImages.Jpeg(10, 15)));
        var conversion = Convert(comic);

        Assert.False(ComicPdf.HasItsPages(conversion with { Pages = [.. conversion.Pages, .. conversion.Pages] }));
        File.WriteAllText(conversion.Path, "not a PDF");
        Assert.False(ComicPdf.HasItsPages(conversion));
    }

    [Fact]
    public void Text_is_kept_in_any_script()
    {
        Assert.Equal("<FEFF0041201300E9>", PdfWriter.Text("A–é"));
    }

    [Theory]
    [InlineData(-6, 0, "(D:20261108063000-06'00')")]
    [InlineData(5, 30, "(D:20261108063000+05'30')")]
    [InlineData(0, 0, "(D:20261108063000+00'00')")]
    public void A_date_is_written_with_its_offset(int hours, int minutes, string written)
    {
        var when = new DateTimeOffset(2026, 11, 8, 6, 30, 0, new TimeSpan(hours, minutes, 0));

        Assert.Equal(written, PdfWriter.Date(when));
    }

    [Fact]
    public void A_number_is_written_the_same_in_every_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("479.7", PdfWriter.Number(479.7));
            Assert.Equal("738", PdfWriter.Number(738));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
