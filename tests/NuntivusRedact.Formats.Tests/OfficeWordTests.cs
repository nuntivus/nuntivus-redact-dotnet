using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NuntivusRedact.Formats.Office;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace NuntivusRedact.Formats.Tests;

public sealed class OfficeWordTests
{
    [Theory]
    [InlineData("[REDACTED]")]
    [InlineData("")]
    [InlineData("replacement longer than the original match")]
    public async Task SplitRunsKeepUnaffectedFormattingAndOriginalReports(string replacement)
    {
        using var input = new MemoryStream(OfficeFixtures.Word());
        using var output = new MemoryStream();
        var report = await new DocxDocumentProcessor(OfficeFixtures.Engine(replacement)).ProcessAsync(input, output);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        output.Position = 0;
        using var document = WordprocessingDocument.Open(output, false);
        var runs = document.MainDocumentPart!.Document!.Descendants<W.Run>().ToArray();
        Assert.Equal("Hello " + replacement, runs[0].InnerText);
        Assert.NotNull(runs[0].RunProperties!.GetFirstChild<W.Bold>());
        Assert.Equal("", runs[1].InnerText);
        Assert.NotNull(runs[1].RunProperties!.GetFirstChild<W.Italic>());
        Assert.Equal("!", runs[2].InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var found = Assert.Single(report.Detections);
        Assert.Equal(6, found.Detection.Span.Start);
        Assert.DoesNotContain("alice", found.ToString());
        Assert.Matches(@"^/packages/0/parts/\d+/fields/\d+$", found.Location);
    }

    [Fact]
    public async Task HeadersFootersCommentsAndPropertiesCannotRecoverMatchedText()
    {
        using var input = new MemoryStream(OfficeFixtures.Word(true));
        using var output = new MemoryStream();
        var report = await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output);
        Assert.True(report.Detections.Count >= 7);
        Assert.All(OfficeFixtures.Entries(output.ToArray()).Values, bytes => Assert.DoesNotContain("alice", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ins")]
    [InlineData("fldSimple")]
    [InlineData("drawing")]
    [InlineData("sdt")]
    public async Task UnsupportedWordSurfacesFailWithoutOutput(string feature)
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "word/document.xml", doc =>
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            doc.Descendants(w + "p").First().Add(new XElement(w + feature));
        }));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task HiddenTextAndMultipleMatchesInOneRunAreProcessed()
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "word/document.xml", doc =>
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var run = doc.Descendants(w + "r").Last();
            run.AddFirst(new XElement(w + "rPr", new XElement(w + "vanish")));
            run.Element(w + "t")!.Value = " alice@example.com alice@example.com";
        }));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var result = await new DocxDocumentProcessor(OfficeFixtures.Engine("X")).ProcessAsync(input, output);
        Assert.Equal(3, result.Detections.Count);
        Assert.DoesNotContain("alice", Encoding.UTF8.GetString(OfficeFixtures.Entries(output.ToArray())["word/document.xml"]));
    }
}
