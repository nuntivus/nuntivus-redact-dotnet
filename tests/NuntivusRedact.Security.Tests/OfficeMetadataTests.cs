using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using NuntivusRedact.Formats.Office;
using NuntivusRedact.Formats.Tests;
using Xunit;
using C = DocumentFormat.OpenXml.CustomProperties;
using V = DocumentFormat.OpenXml.VariantTypes;

namespace NuntivusRedact.Security.Tests;

public sealed class OfficeMetadataTests
{
    [Fact]
    public async Task OpaqueBinaryPropertyIsRejectedEvenWhenItsBase64DoesNotMatchDetectors()
    {
        using var input = new MemoryStream();
        input.Write(OfficeFixtures.Word());
        input.Position = 0;
        using (var doc = WordprocessingDocument.Open(input, true))
        {
            var custom = doc.AddNewPart<CustomFilePropertiesPart>("rIdCustomProperties");
            custom.Properties = new C.Properties(new C.CustomDocumentProperty(new V.VTBlob(Convert.ToBase64String(Encoding.UTF8.GetBytes("alice@example.com"))))
            {
                FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}",
                PropertyId = 2,
                Name = "Label"
            });
        }
        input.Position = 0;
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnverifiedMetadataAndWordHexEscapesFailClosed(bool metadata)
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Word(true), entries =>
        {
            string name = metadata ? entries.Keys.Single(n => n.EndsWith("core.xml", StringComparison.Ordinal)) : "word/document.xml";
            OfficeFixtures.EditXml(entries, name, xml =>
            {
                XNamespace ns = metadata ? "http://purl.org/dc/elements/1.1/" : "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                xml.Descendants(ns + (metadata ? "creator" : "t")).First().Value = "alice_x0040_example.com";
            });
        });
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Empty(output.ToArray());
    }
}
