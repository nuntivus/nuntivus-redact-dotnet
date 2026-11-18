using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NuntivusRedact.Formats.Images;
using NuntivusRedact.Formats.Office;
using SkiaSharp;
using Xunit;

namespace NuntivusRedact.Formats.Tests;

public sealed class OfficeImageTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RebuildsPicturePixelsAndRedactsAlternativeText(bool excel, bool jpeg)
    {
        var image = OcrFixtures.Image(jpeg);
        if (!jpeg) image = OcrFixtures.AddPngMetadata(image);
        var source = OcrFixtures.Office(excel, image, jpeg);
        using var images = new RasterImageRedactor(new StubOcrProvider());
        var engine = OfficeFixtures.Engine();
        IDocumentProcessor processor = excel ? new XlsxDocumentProcessor(engine, null, images) : new DocxDocumentProcessor(engine, null, images);
        using var input = new MemoryStream(source);
        using var output = new MemoryStream();
        var result = await processor.ProcessAsync(input, output);
        Assert.Contains(result.Detections, d => d.Location.EndsWith("/image/0", StringComparison.Ordinal));
        var entries = OfficeFixtures.Entries(output.ToArray());
        var manifest = XDocument.Parse(Encoding.UTF8.GetString(entries["[Content_Types].xml"]));
        var imageName = (string)manifest.Root!.Elements().Single(e => (string?)e.Attribute("ContentType") == "image/png" && e.Name.LocalName == "Override").Attribute("PartName")!;
        var media = new KeyValuePair<string, byte[]>(imageName[1..], entries[imageName[1..]]);
        using var pixels = SKBitmap.Decode(media.Value);
        for (int y = 7; y < 43; y++)
            for (int x = 7; x < 93; x++) Assert.Equal(SKColors.Black, pixels.GetPixel(x, y));
        Assert.Equal(SKColors.White, pixels.GetPixel(130, 25));
        Assert.DoesNotContain("alice", Encoding.UTF8.GetString(media.Value), StringComparison.OrdinalIgnoreCase);
        foreach (var xml in entries.Where(e => e.Key.EndsWith(".xml", StringComparison.Ordinal)))
            Assert.DoesNotContain("alice", Encoding.UTF8.GetString(xml.Value), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(manifest.Root!.Elements(), e => (string?)e.Attribute("PartName") == "/" + media.Key && (string?)e.Attribute("ContentType") == "image/png");
        output.Position = 0;
        using OpenXmlPackage document = excel ? SpreadsheetDocument.Open(output, false) : WordprocessingDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        Assert.True(input.CanRead && output.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingConstructorsStillRejectImagesBeforeCommit(bool excel)
    {
        using var input = new MemoryStream(OcrFixtures.Office(excel, OcrFixtures.Image()));
        using var output = new MemoryStream();
        IDocumentProcessor processor = excel ? new XlsxDocumentProcessor(OfficeFixtures.Engine()) : new DocxDocumentProcessor(OfficeFixtures.Engine());
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await processor.ProcessAsync(input, output));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharesImageBudgetsAcrossNestedPackages(bool pixelBudget)
    {
        var inner = OcrFixtures.Office(false, OcrFixtures.Image());
        var source = OfficeFixtures.Embed(inner, OcrFixtures.Office(true, OcrFixtures.Image()), true);
        using var images = new RasterImageRedactor(new StubOcrProvider());
        var limits = pixelBudget ? new OfficeLimits { MaximumImagePixels = 9600 } : new OfficeLimits { MaximumImages = 1 };
        using var input = new MemoryStream(source);
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(OfficeFixtures.Engine(), limits, images).ProcessAsync(input, output));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailureDiscardsAllStagedOfficeChanges(bool excel)
    {
        using var images = new RasterImageRedactor(new StubOcrProvider((_, _) => throw new InvalidOperationException("alice@example.com")));
        IDocumentProcessor processor = excel ? new XlsxDocumentProcessor(OfficeFixtures.Engine(), null, images) : new DocxDocumentProcessor(OfficeFixtures.Engine(), null, images);
        using var input = new MemoryStream(OcrFixtures.Office(excel, OcrFixtures.Image()));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await processor.ProcessAsync(input, output));
        Assert.Equal(RedactionError.ExtensionFailed, error.Error);
        Assert.DoesNotContain("alice", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsUnsupportedDrawingFeatures(bool excel)
    {
        var source = OfficeFixtures.Rewrite(OcrFixtures.Office(excel, OcrFixtures.Image()), entries =>
        {
            var name = excel ? entries.Keys.Single(n => n.StartsWith("xl/drawings/", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal)) : "word/document.xml";
            OfficeFixtures.EditXml(entries, name, xml =>
            {
                var picture = xml.Descendants().First(e => e.Name.LocalName == "pic");
                picture.Name = picture.Name.Namespace + "sp";
            });
        });
        using var images = new RasterImageRedactor(new StubOcrProvider());
        IDocumentProcessor processor = excel ? new XlsxDocumentProcessor(OfficeFixtures.Engine(), null, images) : new DocxDocumentProcessor(OfficeFixtures.Engine(), null, images);
        using var input = new MemoryStream(source);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<RedactionException>(async () => await processor.ProcessAsync(input, output));
        Assert.Empty(output.ToArray());
    }
}
