using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using NuntivusRedact.Formats.Office;
using Xunit;

namespace NuntivusRedact.Formats.Tests;

public sealed class OfficeStringTests
{
    private static readonly XNamespace X = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public async Task HexEscapedEmailsInRichInlineAndCellStringsCannotBeRecovered()
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Excel(), entries =>
        {
            OfficeFixtures.EditXml(entries, "xl/sharedStrings.xml", doc =>
            {
                var runs = doc.Descendants(X + "si").First().Descendants(X + "t").ToArray();
                runs[0].Value = "alice_x0040_";
                runs[1].Value = "example.com";
            });
            OfficeFixtures.EditXml(entries, "xl/worksheets/sheet1.xml", doc =>
            {
                doc.Descendants(X + "t").First().Value = "_x0061_lice_x0040_example.com";
                var cell = doc.Descendants(X + "c").Single(c => (string?)c.Attribute("r") == "C1");
                cell.SetAttributeValue("t", "str");
                cell.Element(X + "v")!.Value = "alice_x0040_example.com";
            });
            var comments = entries.Keys.Single(n => n.EndsWith("comments1.xml", StringComparison.Ordinal));
            OfficeFixtures.EditXml(entries, comments, doc => doc.Descendants(X + "author").Single().Value = "alice_x0040_example.com");
        });
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var result = await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output);
        Assert.True(result.Detections.Count >= 9);
        Assert.All(OfficeFixtures.Entries(output.ToArray()).Values, b => Assert.DoesNotContain("alice", Encoding.UTF8.GetString(b), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EscapedUnderscoresAreDecodedOnlyOnceAndLiteralTokensStayLiteral()
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Excel(), entries => OfficeFixtures.EditXml(entries, "xl/sharedStrings.xml", doc =>
        {
            var item = doc.Descendants(X + "si").First();
            item.RemoveNodes();
            item.Add(new XElement(X + "t", "_x005F_x0040_ alice_x0040_example.com"));
        }));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(OfficeFixtures.Entries(output.ToArray())["xl/sharedStrings.xml"]));
        Assert.Equal("_x005F_x0040_ [REDACTED]", xml.Descendants(X + "si").First().Value);
    }

    [Fact]
    public async Task ReplacementEscapeSyntaxRemainsLiteralAndIsNotRescanned()
    {
        using var input = new MemoryStream();
        input.Write(OfficeFixtures.Excel());
        input.Position = 0;
        using (var document = SpreadsheetDocument.Open(input, true)) document.DeletePart(document.CoreFilePropertiesPart!);
        input.Position = 0;
        using var output = new MemoryStream();
        var processor = new XlsxDocumentProcessor(OfficeFixtures.Engine("_x0040_"));
        await processor.ProcessAsync(input, output);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(OfficeFixtures.Entries(output.ToArray())["xl/sharedStrings.xml"]));
        Assert.Equal("_x005F_x0040_", xml.Descendants(X + "si").First().Value);
        output.Position = 0;
        using var again = new MemoryStream();
        Assert.Empty((await processor.ProcessAsync(output, again)).Detections);
    }

    [Theory]
    [InlineData("_x0000_")]
    [InlineData("_xD800_")]
    public async Task UnsupportedEncodedControlAndUnpairedSurrogateFailBeforeOutput(string value)
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Excel(), entries => OfficeFixtures.EditXml(entries, "xl/sharedStrings.xml", doc =>
            doc.Descendants(X + "t").First().Value = value));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<RedactionException>(async () => await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task ExcelLogicalStringLengthIsBoundedIndependentlyOfArchiveSize()
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Excel(), entries => OfficeFixtures.EditXml(entries, "xl/sharedStrings.xml", doc =>
            doc.Descendants(X + "t").First().Value = new string('a', 32768)));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new XlsxDocumentProcessor(OfficeFixtures.Engine(), new() { MaximumCompressionRatio = 10000 }).ProcessAsync(input, output));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Empty(output.ToArray());
    }
}
