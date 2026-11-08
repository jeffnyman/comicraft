using System.Text;
using System.Xml.Linq;

namespace ComiCraft.Comics.Tests;

public class ComicInfoDocumentTests
{
    // as a scraper wrote it, with what this library does not read: the pages, a field of a later
    // schema, an element of the scraper's own, and a comment
    private const string Scraped = """
        <?xml version="1.0"?>
        <ComicInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <!-- tagged by hand -->
          <Series>Operation: Knightstrike</Series>
          <Number>3</Number>
          <Year>1995</Year>
          <Writer>Brian Witten</Writer>
          <Editor>Eric Stephenson</Editor>
          <Publisher>Image</Publisher>
          <ScraperId>CVDB174503</ScraperId>
          <Pages>
            <Page Image="0" ImageWidth="950" ImageHeight="1443" Type="FrontCover" />
            <Page Image="1" ImageWidth="950" ImageHeight="1448" />
          </Pages>
          <FutureField>kept</FutureField>
        </ComicInfo>
        """;

    private static ComicInfoDocument Load(string xml) => ComicInfoDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    /// <summary>The names of the root's elements, in order.</summary>
    private static string[] Order(ComicInfoDocument document) =>
        XDocument.Parse(document.ToString()).Root!.Elements().Select(e => e.Name.LocalName).ToArray();

    [Fact]
    public void Setting_a_field_changes_that_field_and_nothing_else()
    {
        var document = Load(Scraped);

        Assert.True(document.Set("Publisher", "Image Comics"));

        var before = XDocument.Parse(Scraped).Root!;
        var after = XDocument.Parse(document.ToString()).Root!;
        Assert.Equal("Image Comics", after.Element("Publisher")!.Value);
        after.Element("Publisher")!.Value = "Image";
        Assert.True(XNode.DeepEquals(before, after));
    }

    [Fact]
    public void What_the_library_does_not_read_is_kept()
    {
        var document = Load(Scraped);
        document.Set("Title", "Enter... Bloodstrike");
        string xml = document.ToString();

        Assert.Contains("<!-- tagged by hand -->", xml);
        Assert.Contains("<ScraperId>CVDB174503</ScraperId>", xml);
        Assert.Contains("<FutureField>kept</FutureField>", xml);
        Assert.Contains("""<Page Image="0" ImageWidth="950" ImageHeight="1443" Type="FrontCover" />""", xml);
        Assert.Contains("""xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance""", xml);
    }

    [Fact]
    public void A_new_field_goes_where_the_schema_puts_it()
    {
        var document = Load(Scraped);
        document.Set("Title", "Enter... Bloodstrike"); // before everything
        document.Set("Month", "7");                     // between Year and Writer
        document.Set("Translator", "Nobody");           // a 2.1 field, between Editor and Publisher
        document.Set("GTIN", "0123456789012");          // after everything the schema has

        Assert.Equal(
            ["Title", "Series", "Number", "Year", "Month", "Writer", "Editor", "Translator", "Publisher", "ScraperId", "Pages", "GTIN", "FutureField"],
            Order(document));
    }

    [Fact]
    public void Every_element_is_written_on_a_line_of_its_own_the_new_ones_too()
    {
        var document = Load(Scraped);
        document.Set("Translator", "Nobody");

        string[] lines = document.ToString().Split('\n');
        Assert.Contains("  <Editor>Eric Stephenson</Editor>", lines.Select(l => l.TrimEnd('\r')));
        Assert.Contains("  <Translator>Nobody</Translator>", lines.Select(l => l.TrimEnd('\r')));
    }

    [Fact]
    public void Spacing_inside_a_value_is_kept()
    {
        var document = Load("<ComicInfo><Summary>One.\n\n  Two.</Summary></ComicInfo>");
        document.Set("Series", "S");

        Assert.Equal("One.\n\n  Two.", document.Get("Summary"));
    }

    [Fact]
    public void Elements_the_schema_does_not_have_stay_where_they_are()
    {
        var document = Load("<ComicInfo><Custom>x</Custom><Series>S</Series></ComicInfo>");
        document.Set("Title", "T");

        Assert.Equal(["Custom", "Title", "Series"], Order(document));
    }

    [Fact]
    public void Removing_a_field_takes_it_out()
    {
        var document = Load(Scraped);

        Assert.True(document.Set("Editor", null));
        Assert.Null(document.Get("Editor"));
        Assert.False(document.Set("Editor", ""));
        Assert.DoesNotContain("Editor", Order(document));
    }

    [Fact]
    public void Setting_the_value_a_field_has_changes_nothing()
    {
        var document = Load(Scraped);

        Assert.False(document.Set("publisher", "  Image "));
    }

    [Fact]
    public void A_field_twice_over_is_left_once()
    {
        var document = Load("<ComicInfo><Series>A</Series><Series>B</Series></ComicInfo>");

        Assert.True(document.Set("Series", "C"));
        Assert.Equal(["Series"], Order(document));
        Assert.Equal("C", document.Get("Series"));
    }

    [Fact]
    public void A_bad_value_changes_nothing()
    {
        var document = Load(Scraped);
        string before = document.ToString();

        Assert.Throws<ComicEditException>(() => document.Set("Year", "soon"));
        Assert.Equal(before, document.ToString());
    }

    [Fact]
    public void What_the_document_says_is_read_from_it_as_it_now_is()
    {
        var document = Load(Scraped);
        document.Set("Colorist", "Gloria Vasquez,Extreme Color");

        Assert.Equal(["Gloria Vasquez", "Extreme Color"], document.Info.Colorists);
        Assert.Equal("Operation: Knightstrike #3 (1995)", document.Info.Heading);
    }

    [Fact]
    public void A_new_document_takes_fields_in_order()
    {
        var document = ComicInfoDocument.New();
        document.Set("Year", "1993");
        document.Set("Series", "Troll");

        Assert.Equal(["Series", "Year"], Order(document));
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", document.ToString());
    }

    [Fact]
    public void A_document_in_a_namespace_keeps_its_new_fields_in_it()
    {
        var document = Load("""<ComicInfo xmlns="urn:example"><Series>S</Series></ComicInfo>""");
        document.Set("Title", "T");

        var root = XDocument.Parse(document.ToString()).Root!;
        Assert.Equal("urn:example", root.Elements().First().Name.NamespaceName);
    }

    [Theory]
    [InlineData("<ComicInfo><Series>Unclosed</ComicInfo>")]
    [InlineData("")]
    [InlineData("<SomethingElse />")]
    public void A_document_that_cannot_be_read_is_not_edited(string xml)
    {
        Assert.Throws<ComicEditException>(() => Load(xml));
    }
}
