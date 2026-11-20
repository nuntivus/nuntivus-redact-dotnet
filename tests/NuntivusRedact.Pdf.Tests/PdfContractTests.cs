using Xunit;

namespace NuntivusRedact.Pdf.Tests;

public sealed class PdfContractTests
{
    [Fact]
    public void ContractsDoNotReferencePdfiumOrCore()
    {
        var references = typeof(IPdfBackend).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name == "PDFiumCore" || reference.Name == "NuntivusRedact.Core");
    }

    [Fact]
    public void RasterCopiesPixelsAndRequiresOpaqueAlpha()
    {
        byte[] source = [0, 0, 0, 255];
        var raster = new PdfRaster(1, 1, source);
        source[0] = 100;
        Assert.Equal(0, raster.Bgra.Span[0]);
        Assert.Throws<ArgumentException>(() => new PdfRaster(1, 1, new byte[4]));
        Assert.Throws<ArgumentException>(() => new PdfRaster(2, 1, source));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfRaster(0, 1, source));
    }

    [Fact]
    public void LimitsAreImmutableAndValidated()
    {
        var original = new PdfBackendLimits();
        var invalid = original with { MaximumPages = 0 };
        original.Validate();
        Assert.Equal(100, original.MaximumPages);
        Assert.Throws<ArgumentException>(invalid.Validate);
        Assert.Throws<ArgumentException>((original with { MaximumPageDimensionPoints = double.NaN }).Validate);
        Assert.Throws<ArgumentException>((original with { MaximumTotalPixels = 1 }).Validate);
    }
}
