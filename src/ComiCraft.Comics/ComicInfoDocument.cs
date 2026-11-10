using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ComiCraft.Comics;

/// <summary>
/// A ComicInfo.xml to be edited. It is the document as it was read, changed only where a field
/// is set: everything else in it is kept, whether this library knows it or not (the pages,
/// fields of later versions of the schema, a tagger's own elements, comments). A field set that
/// was not there before goes where the schema puts it. Only the spacing between elements may
/// change, as the document is written out indented.
/// </summary>
public sealed class ComicInfoDocument
{
    private readonly XDocument _document;

    private ComicInfoDocument(XDocument document) => _document = document;

    private XElement Root => _document.Root!;

    /// <summary>A ComicInfo.xml with nothing in it yet, for a comic that has none.</summary>
    public static ComicInfoDocument New() =>
        new(new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(ComicInfo.RootName)));

    /// <summary>
    /// Reads a ComicInfo.xml to edit. Unlike reading one to show, a document that cannot be read
    /// is an error: what is in it is not known, and so is not to be written over.
    /// </summary>
    public static ComicInfoDocument Load(Stream stream)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, ComicInfo.ReaderSettings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new ComicEditException($"The comic's ComicInfo.xml cannot be read, so it is left as it is: {ex.Message}", ex);
        }

        if (document.Root?.Name.LocalName != ComicInfo.RootName)
        {
            throw new ComicEditException($"The comic's ComicInfo.xml is not a ComicInfo document (it is a '{document.Root?.Name.LocalName}'), so it is left as it is.");
        }

        return new ComicInfoDocument(document);
    }

    /// <summary>What the document now says, read as it is for showing.</summary>
    public ComicInfo Info => ComicInfo.FromElement(Root);

    /// <summary>A field's value as it is written, or null when the document does not have it.</summary>
    public string? Get(string field)
    {
        string name = ComicInfoField.Find(field).Name;
        return Elements(name).FirstOrDefault()?.Value;
    }

    /// <summary>
    /// Sets a field (its name in any case), or removes it when the value is null or blank. The
    /// value is checked and written as <see cref="ComicInfoField.Normalize"/> says. Returns
    /// whether the document changed.
    /// </summary>
    public bool Set(string field, string? value)
    {
        var definition = ComicInfoField.Find(field);
        string? normalized = definition.Normalize(value);
        var existing = Elements(definition.Name).ToList();

        if (normalized is null)
        {
            existing.ForEach(e => e.Remove());
            return existing.Count > 0;
        }

        // a field twice over is read as its first, so the first is set and the others go
        if (existing.Count > 0)
        {
            bool changed = existing[0].Value != normalized || existing.Count > 1 || existing[0].HasElements;
            existing[0].RemoveNodes();
            existing[0].Value = normalized;
            existing.Skip(1).ToList().ForEach(e => e.Remove());
            return changed;
        }

        Insert(new XElement(Root.Name.Namespace + definition.Name, normalized));
        return true;
    }

    /// <summary>The document as UTF-8 XML, indented.</summary>
    public byte[] ToBytes()
    {
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false) };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            _document.Save(writer);
        }

        return stream.ToArray();
    }

    /// <summary>The document as XML text, indented.</summary>
    public override string ToString() => Encoding.UTF8.GetString(ToBytes());

    private IEnumerable<XElement> Elements(string name) => Root.Elements().Where(e => e.Name.LocalName == name);

    /// <summary>
    /// Puts a new element where the schema orders it: after the last element that comes before it,
    /// or else before the first that comes after it. Elements the schema does not have stay where
    /// they are, and are passed over.
    /// </summary>
    private void Insert(XElement element)
    {
        int order = ComicInfoField.OrderOf(element.Name.LocalName);
        var known = Root.Elements()
            .Select(e => (Element: e, Order: ComicInfoField.OrderOf(e.Name.LocalName)))
            .Where(e => e.Order >= 0)
            .ToList();

        var before = known.LastOrDefault(e => e.Order < order).Element;
        var after = known.FirstOrDefault(e => e.Order > order).Element;
        if (before is not null)
        {
            before.AddAfterSelf(element);
        }
        else if (after is not null)
        {
            after.AddBeforeSelf(element);
        }
        else
        {
            Root.Add(element);
        }
    }
}
