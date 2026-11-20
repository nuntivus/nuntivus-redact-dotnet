using PDFiumCore;
using Xunit;

namespace NuntivusRedact.Pdf.Tests;

[Collection("PDFium feasibility")]
public sealed class PdfiumFeasibilityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void LoadsTextAndPageGeometry(int rotation)
    {
        using var pdf = new NativePdf(PdfFixtures.Create(rotation: rotation));
        Assert.Equal(1, fpdfview.FPDF_GetPageCount(pdf.Document));
        Assert.Equal(200, fpdfview.FPDF_GetPageWidth(pdf.Page));
        Assert.Equal(200, fpdfview.FPDF_GetPageHeight(pdf.Page));
        Assert.Equal(rotation / 90, fpdf_edit.FPDFPageGetRotation(pdf.Page));
        Assert.Contains(PdfFixtures.Secret, pdf.ReadText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProvidesNativeCharacterBounds()
    {
        using var pdf = new NativePdf(PdfFixtures.Create());
        var text = fpdf_text.FPDFTextLoadPage(pdf.Page);
        try
        {
            double left = 0, right = 0, bottom = 0, top = 0;
            Assert.Equal(1, fpdf_text.FPDFTextGetCharBox(text, 0, ref left, ref right, ref bottom, ref top));
            Assert.InRange(left, 20, 30);
            Assert.True(right > left);
            Assert.InRange(bottom, 130, 150);
            Assert.True(top > bottom);
        }
        finally { fpdf_text.FPDFTextClosePage(text); }
    }

    [Fact]
    public void RendersTextAndEmbeddedImage()
    {
        using var pdf = new NativePdf(PdfFixtures.Create());
        var pixels = pdf.Render();
        Assert.Equal(200 * 200 * 4, pixels.Length);
        Assert.Contains(pixels, pixel => pixel == 0);
        Assert.True(pixels.Where((_, index) => index % 4 == 3).All(alpha => alpha == 255));
    }

    [Fact]
    public void RemovingTextObjectsRemovesVisibleExtractableText()
    {
        using var pdf = new NativePdf(PdfFixtures.Create());
        pdf.RemoveObjects(1);
        var output = pdf.Save();
        using var reopened = new NativePdf(output);
        Assert.Empty(reopened.ReadText());
        Assert.DoesNotContain(PdfFixtures.Secret, PdfFixtures.Recover(output), StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingImageObjectsRemovesVisibleImage()
    {
        using var pdf = new NativePdf(PdfFixtures.Create());
        var before = pdf.Render();
        pdf.RemoveObjects(3);
        using var reopened = new NativePdf(pdf.Save());
        var after = reopened.Render();
        Assert.NotEqual(before, after);
        Assert.All(after.Skip((170 * 200 + 25) * 4).Take(4), channel => Assert.Equal(255, channel));
        Assert.Contains(PdfFixtures.Secret, reopened.ReadText(), StringComparison.Ordinal);
    }

    [Fact]
    public void NonIncrementalSaveDoesNotSanitizeHiddenMetadata()
    {
        using var pdf = new NativePdf(PdfFixtures.Create(hiddenContent: true));
        pdf.RemoveObjects(1);
        pdf.RemoveObjects(3);
        var output = pdf.Save();
        using var reopened = new NativePdf(output);
        Assert.Empty(reopened.ReadText());
        Assert.Contains(PdfFixtures.HiddenSecret, PdfFixtures.Recover(output), StringComparison.Ordinal);
    }

    [Fact]
    public void FreshImportedEditedPagesDoNotRetainRemovedObjectsOrDocumentMetadata()
    {
        using var source = new NativePdf(PdfFixtures.Create(hiddenContent: true));
        source.RemoveObjects(1);
        var destination = fpdf_edit.FPDF_CreateNewDocument();
        try
        {
            Assert.Equal(1, fpdf_ppo.FPDF_ImportPages(destination, source.Document, "1", 0));
            var output = NativePdf.Save(destination);
            using var reopened = new NativePdf(output);
            Assert.Empty(reopened.ReadText());
            string recovered = PdfFixtures.Recover(output);
            Assert.DoesNotContain(PdfFixtures.Secret, recovered, StringComparison.Ordinal);
            Assert.DoesNotContain(PdfFixtures.HiddenSecret, recovered, StringComparison.Ordinal);
        }
        finally { fpdfview.FPDF_CloseDocument(destination); }
    }

    [Fact]
    public void FreshImageOnlyPdfPreservesPageAndCleanedPixelsWithoutSourceTextOrMetadata()
    {
        using var pdf = new NativePdf(PdfFixtures.Create(hiddenContent: true));
        var pixels = pdf.Render();
        for (int y = 40; y < 75; y++)
            for (int x = 18; x < 155; x++)
            {
                int index = (y * 200 + x) * 4;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = 0;
                pixels[index + 3] = 255;
            }
        var output = NativePdf.CreateImageOnly(pixels);
        using var reopened = new NativePdf(output);
        Assert.Empty(reopened.ReadText());
        Assert.Equal(200, fpdfview.FPDF_GetPageWidth(reopened.Page));
        Assert.Equal(200, fpdfview.FPDF_GetPageHeight(reopened.Page));
        var recovered = PdfFixtures.Recover(output);
        Assert.DoesNotContain(PdfFixtures.Secret, recovered, StringComparison.Ordinal);
        Assert.DoesNotContain(PdfFixtures.HiddenSecret, recovered, StringComparison.Ordinal);
        Assert.Equal(pixels, reopened.Render());
    }
}
