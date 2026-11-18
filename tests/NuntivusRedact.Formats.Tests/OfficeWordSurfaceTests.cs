using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NuntivusRedact.Formats.Office;
using Xunit;
using C = DocumentFormat.OpenXml.CustomProperties;
using V = DocumentFormat.OpenXml.VariantTypes;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace NuntivusRedact.Formats.Tests;

public sealed class OfficeWordSurfaceTests
{
    [Fact]
    public async Task TablesFootnotesEndnotesAndCustomPropertiesAreProcessed()
    {
        using var source = new MemoryStream();
        source.Write(OfficeFixtures.Word());
        source.Position = 0;
        using (var doc = WordprocessingDocument.Open(source, true))
        {
            var main = doc.MainDocumentPart!;
            main.Document!.Body!.Append(new W.Table(new W.TableProperties(), new W.TableGrid(new W.GridColumn()),
                new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("alice")), new W.Run(new W.Text("@example.com")))))));
            var footnotes = main.AddNewPart<FootnotesPart>("rIdFootnotes");
            footnotes.Footnotes = new W.Footnotes(new W.Footnote(new W.Paragraph(new W.Run(new W.Text("alice@example.com")))) { Id = 1 });
            var endnotes = main.AddNewPart<EndnotesPart>("rIdEndnotes");
            endnotes.Endnotes = new W.Endnotes(new W.Endnote(new W.Paragraph(new W.Run(new W.Text("alice@example.com")))) { Id = 1 });
            main.Document.Descendants<W.Paragraph>().First().Append(new W.Run(new W.FootnoteReference { Id = 1 }), new W.Run(new W.EndnoteReference { Id = 1 }));
            var custom = doc.AddNewPart<CustomFilePropertiesPart>("rIdCustomProperties");
            custom.Properties = new C.Properties(new C.CustomDocumentProperty(new V.VTLPWSTR("alice@example.com"))
            {
                FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}",
                PropertyId = 2,
                Name = "Label"
            });
        }
        source.Position = 0;
        using var output = new MemoryStream();
        var report = await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(source, output);
        Assert.Equal(5, report.Detections.Count);
        Assert.All(OfficeFixtures.Entries(output.ToArray()).Values, bytes => Assert.DoesNotContain("alice", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase));
        output.Position = 0;
        using var result = WordprocessingDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(result));
        Assert.Single(result.MainDocumentPart!.Document!.Descendants<W.Table>());
        Assert.NotNull(result.MainDocumentPart.FootnotesPart);
        Assert.NotNull(result.MainDocumentPart.EndnotesPart);
    }

    [Fact]
    public async Task SeparateParagraphsAndBreaksAreNotConcatenatedIntoAMailbox()
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "word/document.xml", doc =>
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var body = doc.Root!.Element(w + "body")!;
            body.RemoveNodes();
            body.Add(new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "alice"))));
            body.Add(new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "@example.com"))));
            body.Add(new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "alice"), new XElement(w + "br"), new XElement(w + "t", "@example.com"))));
        }));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var report = await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output);
        Assert.Empty(report.Detections);
    }
}
