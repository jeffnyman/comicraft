using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ComiCraft.Comics.Tests;

/// <summary>PDFs read as comics: those ComiCraft makes, and others made by hand, of pages that are pictures and pages that are not.</summary>
public sealed class PdfComicTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-pdf-read-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private const string Info = "<?xml version=\"1.0\"?>\n<ComicInfo>\n  <Series>Original Sins</Series>\n  <Number>1</Number>\n</ComicInfo>";

    /// <summary>A PDF made by ComiCraft of a CBZ of the pages; pages that are not JPEG or PNG are decoded to the pixels given.</summary>
    private string Converted(Pixels? pixels, params (string Name, byte[] Bytes)[] entries)
    {
        string comic = TestComics.CbzFile(_folder, "comic.cbz", entries);
        return ComicPdf.ConvertToPdf(comic, null, _ => pixels ?? throw new InvalidOperationException("nothing should be decoded")).Path;
    }

    /// <summary>A PNG's size, color type and depth, and its rows, which must be unfiltered, without their filter bytes.</summary>
    private static (int Width, int Height, byte Depth, byte ColorType, byte[] Palette, byte[] Rows) ReadPng(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        int width = 0, height = 0;
        byte depth = 0, colorType = 0;
        byte[] palette = [];
        var data = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length);
            byte[] typed = png[(at + 4)..(at + 8 + length)];
            Assert.Equal(TestComics.Crc32(typed), BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)));
            switch (type)
            {
                case "IHDR":
                    (width, height, depth, colorType) = (BinaryPrimitives.ReadInt32BigEndian(body), BinaryPrimitives.ReadInt32BigEndian(body[4..]), body[8], body[9]);
                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "IDAT":
                    data.Write(body);
                    break;
            }

            at += 12 + length;
        }

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(data.ToArray()), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        int samples = colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };
        int rowLength = (width * samples * depth + 7) / 8;
        byte[] raw = inflated.ToArray();
        Assert.Equal(height * (rowLength + 1), raw.Length);
        var rows = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, raw[y * (rowLength + 1)]);
            rows.Write(raw, y * (rowLength + 1) + 1, rowLength);
        }

        return (width, height, depth, colorType, palette, rows.ToArray());
    }

    [Fact]
    public void A_pdf_of_jpegs_reads_back_as_the_same_jpegs_byte_for_byte()
    {
        byte[] cover = TestImages.Jpeg(2000, 3000), spread = TestImages.Jpeg(4000, 3000, components: 1);
        string pdf = Converted(null, ("01.jpg", cover), ("02.jpg", spread));

        var comic = ComicBook.Open(pdf);

        Assert.Equal(2, comic.PageCount);
        Assert.Equal(["Page 001", "Page 002"], comic.PageNames);
        Assert.Equal(cover, comic.Page(0).ToArray());
        Assert.Equal(spread, comic.Page(1).ToArray());
        Assert.Null(comic.Problem(0));
        Assert.Equal("comic", comic.Name);
    }

    [Fact]
    public void A_pdf_of_pngs_reads_back_as_pngs_of_the_same_pixels()
    {
        byte[][] gray = [[0, 128, 255], [10, 20, 30]];
        byte[][] rgb = [[255, 0, 0, 0, 255, 0], [0, 0, 255, 9, 9, 9]];
        byte[][] indexed = [[0x12], [0x30]];
        byte[] palette = [0, 0, 0, 255, 255, 255, 200, 100, 50, 1, 2, 3];
        string pdf = Converted(null,
            ("1.png", TestImages.Png(3, 2, 0, 8, gray)),
            ("2.png", TestImages.Png(2, 2, 2, 8, rgb)),
            ("3.png", TestImages.Png(4, 2, 3, 2, indexed, palette)));

        var comic = ComicBook.Open(pdf);

        var first = ReadPng(comic.Page(0).ToArray());
        Assert.Equal((3, 2, (byte)8, Png.Gray), (first.Width, first.Height, first.Depth, first.ColorType));
        Assert.Equal(gray.SelectMany(r => r), first.Rows);

        var second = ReadPng(comic.Page(1).ToArray());
        Assert.Equal((2, 2, (byte)8, Png.Rgb), (second.Width, second.Height, second.Depth, second.ColorType));
        Assert.Equal(rgb.SelectMany(r => r), second.Rows);

        var third = ReadPng(comic.Page(2).ToArray());
        Assert.Equal((4, 2, (byte)2, Png.Palette), (third.Width, third.Height, third.Depth, third.ColorType));
        Assert.Equal(palette, third.Palette);
        Assert.Equal(indexed.SelectMany(r => r), third.Rows);
    }

    [Fact]
    public void A_decoded_page_reads_back_as_its_pixels_and_their_transparency()
    {
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 50, 60, 70, 255];
        string pdf = Converted(new Pixels(2, 2, rgba), ("1.bmp", TestImages.Bmp(1, 1, [0, 0, 0])));

        var page = ReadPng(ComicBook.Open(pdf).Page(0).ToArray());

        Assert.Equal((2, 2, (byte)8, Png.RgbAlpha), (page.Width, page.Height, page.Depth, page.ColorType));
        Assert.Equal(rgba, page.Rows);
    }

    [Fact]
    public void A_decoded_gray_page_reads_back_in_gray()
    {
        byte[] rgba = [7, 7, 7, 255, 200, 200, 200, 255];
        string pdf = Converted(new Pixels(2, 1, rgba), ("1.bmp", TestImages.Bmp(1, 1, [0, 0, 0])));

        var page = ReadPng(ComicBook.Open(pdf).Page(0).ToArray());

        Assert.Equal(Png.Gray, page.ColorType);
        Assert.Equal([7, 200], page.Rows);
    }

    [Fact]
    public void The_comic_info_attached_to_a_pdf_is_read()
    {
        string pdf = Converted(null, ("1.jpg", TestImages.Jpeg(10, 10)), ("ComicInfo.xml", Encoding.UTF8.GetBytes(Info)));

        var comic = ComicBook.Open(pdf);

        Assert.Equal("Original Sins", comic.Info?.Series);
        Assert.Equal(Encoding.UTF8.GetBytes(Info), ComicArchive.ReadComicInfoBytes(pdf));
        Assert.Equal(ArchiveKind.Pdf, ComicArchive.KindOf(pdf));
    }

    [Fact]
    public void A_pdf_converts_to_a_pdf_again_with_the_same_pages()
    {
        string pdf = Converted(null, ("1.jpg", TestImages.Jpeg(10, 10)), ("2.png", TestImages.Png(1, 1, 0, 8, [[5]])), ("ComicInfo.xml", Encoding.UTF8.GetBytes(Info)));

        var again = ComicPdf.ConvertToPdf(pdf, Path.Combine(_folder, "again.pdf"), _ => throw new InvalidOperationException("nothing should be decoded"));

        Assert.True(ComicPdf.HasItsPages(again));
        Assert.Equal(ComicArchive.ReadComicInfoBytes(pdf), ComicArchive.ReadComicInfoBytes(again.Path));
    }

    [Fact]
    public void A_pdfs_comic_info_is_not_changed_and_it_is_not_made_a_cbz()
    {
        string pdf = Converted(null, ("1.jpg", TestImages.Jpeg(10, 10)));

        var write = Assert.Throws<ComicEditException>(() => ComicArchive.WriteComicInfo(pdf, ComicInfoDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(Info)))));
        Assert.Contains("is a PDF", write.Message);
        var convert = Assert.Throws<ComicEditException>(() => ComicArchive.ConvertToCbz(pdf, Path.Combine(_folder, "made.cbz")));
        Assert.Contains("is a PDF", convert.Message);
    }

    // PDFs made by hand, with pages other than ComiCraft's: each page's content, and its resources

    private const string Helvetica = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";

    private static readonly byte[] Jpeg = TestImages.Jpeg(100, 150);

    private string HandMade(params (string Content, string? Extra)[] pages)
    {
        string path = Path.Combine(_folder, "hand.pdf");
        using (var file = File.Create(path))
        {
            var pdf = new PdfWriter(file);
            pdf.Start();
            int catalog = pdf.Reserve(), tree = pdf.Reserve(), info = pdf.Reserve(), image = pdf.Reserve();
            pdf.Stream(image, "/Type /XObject /Subtype /Image /Width 100 /Height 150 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", Jpeg);
            var kids = new List<int>();
            foreach (var (content, extra) in pages)
            {
                int contents = pdf.Reserve(), page = pdf.Reserve();
                pdf.Stream(contents, "", Encoding.ASCII.GetBytes(content));
                pdf.Object(page, $"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 100 150] /Resources << /XObject << /Im {image} 0 R >> {Helvetica} >> /Contents {contents} 0 R {extra} >>");
                kids.Add(page);
            }

            pdf.Object(tree, $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>");
            pdf.Object(info, "<< >>");
            pdf.Object(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
            pdf.Finish(catalog, info);
        }

        return path;
    }

    private const string Picture = "q 100 0 0 150 0 0 cm /Im Do Q";

    [Fact]
    public void A_page_with_hidden_text_on_its_picture_is_its_picture()
    {
        // a scan made searchable: the words are there, but not drawn
        var comic = ComicBook.Open(HandMade((Picture + " BT 3 Tr /F1 12 Tf 10 10 Td (Searchable) Tj ET", null)));

        Assert.Null(comic.Problem(0));
        Assert.Equal(Jpeg, comic.Page(0).ToArray());
    }

    [Theory]
    [InlineData("BT /F1 12 Tf 10 10 Td (Lettering) Tj ET " + Picture, null, "text on it")]
    [InlineData(Picture + " " + Picture, null, "made of 2 pictures")]
    [InlineData("0 0 50 50 re f", null, "no picture")]
    [InlineData(Picture, "/Rotate 90", "turned or flipped")]
    [InlineData("q 100 0 0 -150 0 150 cm /Im Do Q", null, "turned or flipped")]
    [InlineData("q 0 150 -100 0 100 0 cm /Im Do Q", null, "turned or flipped")]
    public void A_page_that_is_not_only_a_picture_upright_says_why_it_is_not_shown(string content, string? extra, string why)
    {
        var comic = ComicBook.Open(HandMade((Picture, null), (content, extra)));

        Assert.Equal(2, comic.PageCount);
        Assert.Null(comic.Problem(0));
        Assert.Contains(why, comic.Problem(1));
        Assert.True(comic.Page(1).IsEmpty);
    }

    [Fact]
    public void A_pdf_none_of_whose_pages_is_a_picture_is_not_a_comic()
    {
        string pdf = HandMade(("BT /F1 12 Tf 10 10 Td (Chapter One) Tj ET", null), ("BT /F1 12 Tf 10 10 Td (Chapter Two) Tj ET", null));

        var error = Assert.Throws<ComicEditException>(() => ComicBook.Open(pdf));

        Assert.Contains("none of its 2 pages is a picture", error.Message);
        Assert.Contains("text on it", error.Message);
    }

    [Fact]
    public void A_page_that_is_not_a_picture_stops_a_conversion_and_says_why()
    {
        string pdf = HandMade((Picture, null), ("BT /F1 12 Tf 10 10 Td (Ads) Tj ET " + Picture, null));

        var error = Assert.Throws<ComicEditException>(() => ComicPdf.ConvertToPdf(pdf, Path.Combine(_folder, "out.pdf"), _ => throw new InvalidOperationException()));

        Assert.Contains("page 2 (Page 002) cannot be put in a PDF: it has text on it", error.Message);
        Assert.False(File.Exists(Path.Combine(_folder, "out.pdf")));
    }

    [Fact]
    public void A_png_is_written_that_its_own_reader_reads()
    {
        byte[] rows = [1, 2, 3, 4, 5, 6];

        var png = ReadPng(Png.Of(3, 2, 8, Png.Gray, Png.Compress(rows, 3)));

        Assert.Equal((3, 2), (png.Width, png.Height));
        Assert.Equal(rows, png.Rows);
    }
}
