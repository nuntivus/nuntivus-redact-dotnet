using System.Text;
using System.Xml.Linq;
using NuntivusRedact.Formats.Office;
using NuntivusRedact.Formats.Tests;
using Xunit;

namespace NuntivusRedact.Security.Tests;

public sealed class OfficeProfileSecurityTests
{
    private static async Task Reject(byte[] bytes, bool excel = false)
    {
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        IDocumentProcessor processor = excel ? new XlsxDocumentProcessor(OfficeFixtures.Engine()) : new DocxDocumentProcessor(OfficeFixtures.Engine());
        await Assert.ThrowsAsync<RedactionException>(async () => await processor.ProcessAsync(input, output));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("application/vnd.ms-word.document.macroEnabled.main+xml")]
    [InlineData("application/vnd.ms-office.vbaProject")]
    [InlineData("application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml")]
    [InlineData("application/vnd.openxmlformats-officedocument.oleObject")]
    public async Task ActiveSignedAndOpaqueContentTypesAreRejected(string type)
    {
        await Reject(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "[Content_Types].xml", xml =>
            xml.Root!.Elements().First(e => e.Attribute("ContentType")!.Value.Contains("wordprocessingml", StringComparison.Ordinal)).SetAttributeValue("ContentType", type))));
    }

    [Fact]
    public async Task ManifestCannotDeclareItselfAsAnIgnoredSensitivePart()
    {
        await Reject(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "[Content_Types].xml", xml =>
            xml.Root!.Add(new XElement(xml.Root.Name.Namespace + "Override", new XAttribute("PartName", "/[Content_Types].xml"), new XAttribute("ContentType", "alice@example.com"))))));
    }

    [Fact]
    public async Task UnusedExtensionMetadataIsNotSilentlyPreserved()
    {
        await Reject(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "[Content_Types].xml", xml =>
            xml.Root!.Add(new XElement(xml.Root.Name.Namespace + "Default", new XAttribute("Extension", "alice@example.com"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))))));
    }

    [Theory]
    [InlineData("definedNames")]
    [InlineData("externalReferences")]
    [InlineData("pivotCaches")]
    public async Task AlternateWorkbookSurfacesAreExplicitlyRejected(string surface)
    {
        await Reject(OfficeFixtures.Rewrite(OfficeFixtures.Excel(), entries => OfficeFixtures.EditXml(entries, "xl/workbook.xml", xml =>
            xml.Root!.Add(new XElement(xml.Root.Name.Namespace + surface)))), true);
    }

    [Fact]
    public async Task OutOfRangeSharedStringIndicesAreRejected()
    {
        await Reject(OfficeFixtures.Rewrite(OfficeFixtures.Excel(), entries => OfficeFixtures.EditXml(entries, "xl/worksheets/sheet1.xml", xml =>
        {
            XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            xml.Descendants(x + "c").First(e => (string?)e.Attribute("t") == "s").Element(x + "v")!.Value = "999";
        })), true);
    }

    [Fact]
    public async Task CompressedInflationBombFailsBeforeSdkParsing()
    {
        var bomb = OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => entries["word/document.xml"] = Encoding.UTF8.GetBytes(new string('x', 500_000)));
        using var input = new MemoryStream(bomb);
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task XlsxConcurrentReuseAndRepeatProcessingKeepMatchedTextRemoved()
    {
        var bytes = OfficeFixtures.Excel();
        var processor = new XlsxDocumentProcessor(OfficeFixtures.Engine());
        var outputs = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            await processor.ProcessAsync(input, output);
            return output.ToArray();
        }));
        Assert.All(outputs, output => Assert.Equal(outputs[0], output));
        using var again = new MemoryStream(outputs[0]);
        using var destination = new MemoryStream();
        var result = await processor.ProcessAsync(again, destination);
        Assert.Empty(result.Detections);
        Assert.All(OfficeFixtures.Entries(destination.ToArray()).Values, b => Assert.DoesNotContain("alice", Encoding.UTF8.GetString(b), StringComparison.OrdinalIgnoreCase));
    }
}
