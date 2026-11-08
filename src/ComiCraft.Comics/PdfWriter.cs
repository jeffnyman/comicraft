using System.Globalization;
using System.Text;

namespace ComiCraft.Comics;

/// <summary>
/// Writes a PDF (1.7), object by object, as the specification (ISO 32000) lays one out: a
/// header, the objects, each numbered, a table of where each begins, and a trailer saying which
/// is the document. The objects are written as they are made, in any order, so a page is written
/// and its memory let go before the next is made.
/// </summary>
internal sealed class PdfWriter(Stream stream)
{
    private readonly List<long> _offsets = [0];
    private long _at;

    /// <summary>A number for an object to be written later, so that others can refer to it first.</summary>
    public int Reserve()
    {
        _offsets.Add(-1);
        return _offsets.Count - 1;
    }

    public void Start()
    {
        // the second line's bytes above 127 tell a reader that the file is binary
        Write("%PDF-1.7\n%");
        Write([0xE2, 0xE3, 0xCF, 0xD3]);
        Write("\n");
    }

    public void Object(int number, string body)
    {
        Begin(number);
        Write($"{number} 0 obj\n{body}\nendobj\n");
    }

    public void Stream(int number, string dictionary, ReadOnlySpan<byte> data)
    {
        Begin(number);
        Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< {dictionary} /Length {data.Length} >>\nstream\n"));
        Write(data);
        Write("\nendstream\nendobj\n");
    }

    /// <summary>The table of where each object is, and the trailer naming the document and its details.</summary>
    public void Finish(int root, int info)
    {
        int missing = _offsets.IndexOf(-1);
        if (missing >= 0)
        {
            throw new InvalidOperationException($"object {missing} was reserved but not written.");
        }

        long xref = _at;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {_offsets.Count}\n");
        table.Append("0000000000 65535 f \n"); // every entry is 20 bytes, its end of line included
        foreach (long offset in _offsets.Skip(1))
        {
            table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        string id = Guid.NewGuid().ToString("N").ToUpperInvariant();
        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {_offsets.Count} /Root {root} 0 R /Info {info} 0 R /ID [<{id}> <{id}>] >>\nstartxref\n{xref}\n%%EOF\n");
        Write(table.ToString());
    }

    private void Begin(int number)
    {
        if (_offsets[number] != -1)
        {
            throw new InvalidOperationException($"object {number} was written twice, or never reserved.");
        }

        _offsets[number] = _at;
    }

    private void Write(string text) => Write(Encoding.ASCII.GetBytes(text));

    private void Write(ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
        _at += bytes.Length;
    }

    /// <summary>A text string, as UTF-16 with its byte order mark, in hex: so any text, in any script, is kept.</summary>
    public static string Text(string text) =>
        "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text)) + ">";

    /// <summary>A date, as a PDF writes one: (D:20261108063000-06'00').</summary>
    public static string Date(DateTimeOffset when)
    {
        var offset = when.Offset;
        char sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"(D:{when:yyyyMMddHHmmss}{sign}{offset.Hours:D2}'{offset.Minutes:D2}')");
    }

    /// <summary>A number, as few digits as say it, and never in the culture's own way.</summary>
    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
