using NuntivusRedact.Pdf.Experimental;
using NuntivusRedact.Pdf.PdfiumCore;
using Xunit;

namespace NuntivusRedact.Pdf.Tests;

[Collection("PDFium feasibility")]
public sealed class ExperimentalPdfRedactorTests(PdfiumRuntime fixture)
{
    private IPdfBackend Backend => new PdfiumBackend(fixture.Runtime);

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task RasterizedRemovesMatchedTextAndSourceMetadata(int rotation)
    {
        var engine = new ExperimentalPdfRedactor(Backend, new FixtureRedactor());
        var output = await engine.RedactAsync(PdfFixtures.Create(rotation: rotation, hiddenContent: true),
            new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized, Dpi = 72 });
        await using var document = await Backend.OpenAsync(output, new PdfBackendLimits());
        Assert.Empty((await document.ReadPageAsync(0)).Text);
        string recovered = PdfFixtures.Recover(output.ToArray());
        Assert.DoesNotContain(PdfFixtures.Secret, recovered, StringComparison.Ordinal);
        Assert.DoesNotContain(PdfFixtures.HiddenSecret, recovered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualRegionPermanentlyReplacesPixels()
    {
        var engine = new ExperimentalPdfRedactor(Backend, new FixtureRedactor());
        var output = await engine.RedactAsync(PdfFixtures.Create(), new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized, Dpi = 72 },
            [new PdfManualRegion(0, 0, 0, 20, 20)]);
        await using var document = await Backend.OpenAsync(output, new PdfBackendLimits());
        var raster = await document.RenderPageAsync(0, 200, 200);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, raster.Bgra.Slice(0, 4).ToArray());
    }

    [Fact]
    public async Task NativeModeFailsWithoutFallbackOrDetection()
    {
        var detector = new FixtureRedactor();
        var engine = new ExperimentalPdfRedactor(new RasterOnlyBackend(Backend), detector);
        var error = await Assert.ThrowsAsync<RedactionException>(() => engine.RedactAsync(PdfFixtures.Create(),
            new ExperimentalPdfOptions { Mode = PdfOutputMode.NativePreserving }).AsTask());
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Equal(0, detector.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task NativePreservingRemovesWholeMatchedTextAndKeepsImage(int rotation)
    {
        var input = PdfFixtures.Create(rotation: rotation, hiddenContent: true);
        var output = await new ExperimentalPdfRedactor(Backend, new FixtureRedactor()).RedactAsync(input,
            new ExperimentalPdfOptions { Mode = PdfOutputMode.NativePreserving });
        await using var document = await Backend.OpenAsync(output, new PdfBackendLimits());
        Assert.Empty((await document.ReadPageAsync(0)).Text);
        var recovered = PdfFixtures.Recover(output.ToArray());
        Assert.DoesNotContain(PdfFixtures.Secret, recovered, StringComparison.Ordinal);
        Assert.DoesNotContain(PdfFixtures.HiddenSecret, recovered, StringComparison.Ordinal);
        using var native = new NativePdf(output.ToArray());
        Assert.Contains(Enumerable.Range(0, PDFiumCore.fpdf_edit.FPDFPageCountObjects(native.Page)),
            index => PDFiumCore.fpdf_edit.FPDFPageObjGetType(PDFiumCore.fpdf_edit.FPDFPageGetObject(native.Page, index)) == 3);
    }

    [Fact]
    public async Task NativeManualImageRemovalKeepsUnmatchedSelectableText()
    {
        var output = await new ExperimentalPdfRedactor(Backend, new FixtureRedactor { NoMatches = true }).RedactAsync(PdfFixtures.Create(),
            new ExperimentalPdfOptions { Mode = PdfOutputMode.NativePreserving }, [new PdfManualRegion(0, 20, 160, 50, 190)]);
        await using var document = await Backend.OpenAsync(output, new PdfBackendLimits());
        Assert.Contains(PdfFixtures.Secret, (await document.ReadPageAsync(0)).Text, StringComparison.Ordinal);
        using var native = new NativePdf(output.ToArray());
        Assert.DoesNotContain(Enumerable.Range(0, PDFiumCore.fpdf_edit.FPDFPageCountObjects(native.Page)),
            index => PDFiumCore.fpdf_edit.FPDFPageObjGetType(PDFiumCore.fpdf_edit.FPDFPageGetObject(native.Page, index)) == 3);
    }

    private sealed class RasterOnlyBackend(IPdfBackend inner) : IPdfBackend
    {
        public PdfBackendCapabilities Capabilities => inner.Capabilities;
        public ValueTask<IPdfDocumentSession> OpenAsync(ReadOnlyMemory<byte> input, PdfBackendLimits limits, CancellationToken cancellationToken = default)
            => inner.OpenAsync(input, limits, cancellationToken);
        public ValueTask<IPdfRasterWriter> CreateRasterWriterAsync(PdfBackendLimits limits, CancellationToken cancellationToken = default)
            => inner.CreateRasterWriterAsync(limits, cancellationToken);
    }

    [Fact]
    public async Task NativeOutputLimitsAndCancellationRecoverWithoutLeakingRuntime()
    {
        var native = Assert.IsAssignableFrom<IPdfNativeObjectBackend>(Backend);
        var error = await Assert.ThrowsAsync<RedactionException>(() => native.RemoveObjectsAsync(PdfFixtures.Create(), [],
            new PdfBackendLimits { MaximumOutputBytes = 1 }).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => native.RemoveObjectsAsync(PdfFixtures.Create(), [],
            new PdfBackendLimits(), cancelled.Token).AsTask());
        Assert.NotEmpty((await native.RemoveObjectsAsync(PdfFixtures.Create(), [], new PdfBackendLimits())).ToArray());
    }

    [Fact]
    public async Task NativeAnnotationsAreRejectedInsteadOfCopied()
    {
        var input = StructuralPdfFixtures.Create(pageEntries: "/Annots [5 0 R]",
            additionalObjects: ["<< /Type /Annot /Subtype /Text /Rect [0 0 20 20] /Contents (private fixture) >>"]);
        var error = await Assert.ThrowsAsync<RedactionException>(() => Assert.IsAssignableFrom<IPdfNativeObjectBackend>(Backend)
            .RemoveObjectsAsync(input, [], new PdfBackendLimits()).AsTask());
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
    }

    [Fact]
    public async Task InvalidRegionsAndPixelBudgetsFailAndRecover()
    {
        var engine = new ExperimentalPdfRedactor(Backend, new FixtureRedactor());
        await Assert.ThrowsAsync<RedactionException>(() => engine.RedactAsync(PdfFixtures.Create(),
            new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized }, [new PdfManualRegion(0, double.NaN, 0, 20, 20)]).AsTask());
        var error = await Assert.ThrowsAsync<RedactionException>(() => engine.RedactAsync(PdfFixtures.Create(),
            new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized, Limits = new PdfBackendLimits { MaximumPixelsPerPage = 1 } }).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.NotEmpty((await engine.RedactAsync(PdfFixtures.Create(), new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized, Dpi = 72 })).ToArray());
    }

    [Fact]
    public async Task CancellationReturnsNoOutput()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var engine = new ExperimentalPdfRedactor(Backend, new FixtureRedactor());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RedactAsync(PdfFixtures.Create(),
            new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized }, cancellationToken: source.Token).AsTask());
    }

    [Theory]
    [InlineData(false, RedactionError.InvalidDetection)]
    [InlineData(true, RedactionError.ExtensionFailed)]
    public async Task InvalidEditsAndSensitiveExtensionFailuresAbort(bool fail, RedactionError expected)
    {
        var engine = new ExperimentalPdfRedactor(Backend, new FixtureRedactor { Invalid = !fail, Fail = fail });
        var error = await Assert.ThrowsAsync<RedactionException>(() => engine.RedactAsync(PdfFixtures.Create(),
            new ExperimentalPdfOptions { Mode = PdfOutputMode.Rasterized }).AsTask());
        Assert.Equal(expected, error.Error);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(PdfFixtures.Secret, error.Message, StringComparison.Ordinal);
    }

    private sealed class FixtureRedactor : IEditAwareTextRedactor
    {
        internal int Calls { get; private set; }
        internal bool Invalid { get; init; }
        internal bool Fail { get; init; }
        internal bool NoMatches { get; init; }
        public ValueTask<EditRedactionResult> RedactWithEditsAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (NoMatches) return ValueTask.FromResult(new EditRedactionResult(text, []));
            if (Fail) throw new InvalidOperationException(PdfFixtures.Secret);
            if (Invalid) return ValueTask.FromResult(new EditRedactionResult("incorrect", []));
            int start = text.IndexOf(PdfFixtures.Secret, StringComparison.Ordinal);
            if (start < 0) return ValueTask.FromResult(new EditRedactionResult(text, []));
            var detection = new Detection(new DetectionSpan(start, PdfFixtures.Secret.Length), DetectionCategories.Email, 1, "fixture");
            return ValueTask.FromResult(new EditRedactionResult(text.Remove(start, PdfFixtures.Secret.Length), [new RedactionEdit(detection, "")]));
        }
        public async ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default)
        {
            var result = await RedactWithEditsAsync(text, cancellationToken);
            return new RedactionResult(result.Text, result.Edits.Select(edit => edit.Detection));
        }
    }
}
