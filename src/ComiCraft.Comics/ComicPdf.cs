using System.Runtime.InteropServices;
using UglyToad.PdfPig;

namespace ComiCraft.Comics;

/// <summary>A comic converted to a PDF: where it is, and what each of its pages' images must be.</summary>
public sealed record PdfConversion(string Path, IReadOnlyList<string> Pages)
{
    /// <summary>How many pages the PDF has.</summary>
    public int PageCount => Pages.Count;
}

/// <summary>
/// Converting a comic to a PDF: a page of the PDF for each page of the comic, in the same order,
/// each the image and nothing else. Nothing is lost: a JPEG, and most PNGs, go in as they are, and
/// anything else in its decoded pixels, compressed without loss. The comic's ComicInfo.xml says
/// what the PDF's own details are (its title, author, subject and keywords), and goes in whole as
/// well, as an attachment, so that it can be read back. Anything else in the archive (a scanner's
/// note, a folder's thumbnail) has no place in a PDF and is left out.
/// </summary>
public static class ComicPdf
{
    /// <summary>
    /// The height of every page, in points: 10¼ inches, a printed comic's. A page's width follows
    /// from its shape, so a page is about 6⅝ inches wide, as in print, and a spread twice that,
    /// however many pixels it was scanned at.
    /// </summary>
    public const double PageHeight = 10.25 * 72;

    /// <summary>The largest a page can be, in points: 200 inches, which is as large as Acrobat will show, and as wide as a strip can be.</summary>
    private const double Largest = 14_400;

    /// <summary>
    /// Makes a PDF of the comic, beside it by the same name or at <paramref name="target"/>. An
    /// existing file is never written over, and the PDF is made in a copy and moved into place, so
    /// that it is either whole or not there. <paramref name="decode"/> decodes a page that must be
    /// decoded to go in: one in WebP, GIF or BMP, or one in a kind of PNG or JPEG a PDF cannot hold.
    /// </summary>
    public static PdfConversion ConvertToPdf(string path, string? target, Func<ReadOnlyMemory<byte>, Pixels> decode, DateTimeOffset? now = null)
    {
        target ??= System.IO.Path.ChangeExtension(path, ".pdf");
        string name = System.IO.Path.GetFileName(path);
        if (System.IO.Path.GetFullPath(target) == System.IO.Path.GetFullPath(path))
        {
            throw new ComicEditException($"'{name}' cannot be converted to itself; give the PDF another name.");
        }

        if (File.Exists(target))
        {
            throw new ComicEditException($"'{System.IO.Path.GetFileName(target)}' is already there, and is left as it is.");
        }

        ComicBook comic;
        byte[]? comicInfo;
        try
        {
            comic = ComicBook.Open(path);
            comicInfo = ComicArchive.ReadComicInfoBytes(path);
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException &&
            // an archive that cannot be opened says so, naming it; a PDF that is not a comic says why, without
            !(ex is ComicEditException && ex.Message.StartsWith($"'{name}'", StringComparison.Ordinal)))
        {
            throw new ComicEditException($"'{name}' cannot be read as a comic: {ex.Message}", ex);
        }

        if (comic.PageCount == 0)
        {
            throw new ComicEditException($"'{name}' has no pages to make a PDF of.");
        }

        var fingerprints = new List<string>();
        ComicArchive.ReplaceSafely(target, overwrite: false, copy =>
        {
            fingerprints.Clear();
            using var file = new FileStream(copy, FileMode.CreateNew, FileAccess.Write);
            var pdf = new PdfWriter(file);
            pdf.Start();
            int catalog = pdf.Reserve(), pages = pdf.Reserve(), info = pdf.Reserve();
            var kids = new List<int>();
            for (int i = 0; i < comic.PageCount; i++)
            {
                var image = ImageOf(comic, i, decode);
                fingerprints.Add(image.Fingerprint);
                kids.Add(WritePage(pdf, pages, image));
            }

            pdf.Object(pages, $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>");
            pdf.Object(info, Details(comic, now ?? DateTimeOffset.Now));

            string names = "";
            if (comicInfo is not null)
            {
                int embedded = pdf.Reserve(), spec = pdf.Reserve();
                pdf.Stream(embedded, $"/Type /EmbeddedFile /Subtype /text#2Fxml /Params << /Size {comicInfo.Length} >>", comicInfo);
                pdf.Object(spec, $"<< /Type /Filespec /F (ComicInfo.xml) /UF {PdfWriter.Text("ComicInfo.xml")} /Desc {PdfWriter.Text("The comic's details, as its archive held them")} /AFRelationship /Data /EF << /F {embedded} 0 R /UF {embedded} 0 R >> >>");
                names = $" /Names << /EmbeddedFiles << /Names [(ComicInfo.xml) {spec} 0 R] >> >> /AF [{spec} 0 R]";
            }

            pdf.Object(catalog, $"<< /Type /Catalog /Pages {pages} 0 R{names} >>");
            pdf.Finish(catalog, info);
        });

        return new PdfConversion(target, fingerprints);
    }

    /// <summary>
    /// Whether the PDF, read back by a reader of PDFs of its own, has the comic's pages: as many,
    /// each with one image, whose data is what was written. A PDF that cannot be read has not.
    /// </summary>
    public static bool HasItsPages(PdfConversion conversion)
    {
        try
        {
            using var document = PdfDocument.Open(conversion.Path);
            if (document.NumberOfPages != conversion.PageCount)
            {
                return false;
            }

            for (int number = 1; number <= document.NumberOfPages; number++)
            {
                var images = document.GetPage(number).GetImages().ToList();
                if (images is not [var image] ||
                    Fingerprints.Of(image.RawMemory.Span, image.MaskImage is { } mask ? mask.RawMemory.Span : default) != conversion.Pages[number - 1])
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static PdfImage ImageOf(ComicBook comic, int index, Func<ReadOnlyMemory<byte>, Pixels> decode)
    {
        if (comic.Problem(index) is { } problem)
        {
            throw new ComicEditException($"page {index + 1} ({comic.PageNames[index]}) cannot be put in a PDF: {problem}.");
        }

        var page = comic.Page(index);
        byte[] bytes = MemoryMarshal.TryGetArray(page, out var segment) && segment is { Offset: 0 } && segment.Count == segment.Array!.Length
            ? segment.Array
            : page.ToArray();
        try
        {
            return PdfImage.Of(bytes, decode);
        }
        catch (Exception ex) when (ex is not ComicEditException and not OutOfMemoryException)
        {
            throw new ComicEditException($"page {index + 1} ({comic.PageNames[index]}) cannot be put in a PDF: {ex.Message}", ex);
        }
    }

    /// <summary>A page, <see cref="PageHeight"/> high and its image's shape, with the image filling it; returns the page's number.</summary>
    private static int WritePage(PdfWriter pdf, int pages, PdfImage image)
    {
        int? mask = null;
        if (image.Mask is { } alpha)
        {
            mask = pdf.Reserve();
            pdf.Stream(mask.Value, $"/Type /XObject /Subtype /Image /Width {alpha.Width} /Height {alpha.Height} {alpha.Dictionary}", alpha.Data);
        }

        int picture = pdf.Reserve();
        pdf.Stream(picture, $"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} {image.Dictionary}{(mask is { } m ? $" /SMask {m} 0 R" : "")}", image.Data);

        double width = PageHeight * image.Width / image.Height, height = PageHeight;
        double scale = Math.Min(1, Largest / Math.Max(width, height));
        (string w, string h) = (PdfWriter.Number(width * scale), PdfWriter.Number(height * scale));

        int contents = pdf.Reserve();
        pdf.Stream(contents, "", System.Text.Encoding.ASCII.GetBytes($"q {w} 0 0 {h} 0 0 cm /Page Do Q"));

        int page = pdf.Reserve();
        pdf.Object(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Page {picture} 0 R >> >> /Contents {contents} 0 R >>");
        return page;
    }

    /// <summary>The PDF's details, from the comic's: its heading for a title, its writers for an author.</summary>
    private static string Details(ComicBook comic, DateTimeOffset now)
    {
        var info = comic.Info;
        var details = new List<string> { $"/Title {PdfWriter.Text(comic.Heading)}" };
        if (info is not null)
        {
            if (info.Writers.Count > 0)
            {
                details.Add($"/Author {PdfWriter.Text(string.Join(", ", info.Writers))}");
            }

            if (info.Summary is { Length: > 0 } summary)
            {
                details.Add($"/Subject {PdfWriter.Text(summary)}");
            }

            if (info.Genres.Count > 0)
            {
                details.Add($"/Keywords {PdfWriter.Text(string.Join(", ", info.Genres))}");
            }
        }

        details.Add($"/Creator {PdfWriter.Text("ComiCraft")}");
        details.Add($"/Producer {PdfWriter.Text("ComiCraft")}");
        details.Add($"/CreationDate {PdfWriter.Date(now)}");
        return $"<< {string.Join(" ", details)} >>";
    }
}
