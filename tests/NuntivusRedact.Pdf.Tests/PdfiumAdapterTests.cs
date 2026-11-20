using NuntivusRedact.Pdf.PdfiumCore;
using Xunit;

namespace NuntivusRedact.Pdf.Tests;

[Collection("PDFium feasibility")]
public sealed class PdfiumAdapterTests(PdfiumRuntime fixture)
{
    private IPdfBackend Backend => new PdfiumBackend(fixture.Runtime);

    [Fact]
    public void DoesNotAdvertiseNativeRemovalOrExposeNativeHandles()
    {
        Assert.False(Backend.Capabilities.NativeRemoval);
        Assert.True(Backend.Capabilities.Rendering);
        var publicTypes = typeof(PdfiumBackend).Assembly.GetExportedTypes();
        foreach (var type in publicTypes)
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType.Assembly.GetName().Name == "PDFiumCore");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task MapsCharacterBoundsIntoDisplayedPageSpace(int rotation)
    {
        await using var document = await Backend.OpenAsync(PdfFixtures.Create(rotation: rotation), new PdfBackendLimits());
        var page = await document.ReadPageAsync(0);
        Assert.Equal(new PdfPageSize(200, 200), page.Size);
        Assert.Contains(PdfFixtures.Secret, page.Text, StringComparison.Ordinal);
        Assert.Equal(page.Text.Length, page.Characters.Sum(character => character.Span.Length));
        var bounds = Assert.IsType<PdfQuadrilateral>(page.Characters[0].Bounds);
        foreach (var point in new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft })
        {
            Assert.InRange(point.X, 0, 200);
            Assert.InRange(point.Y, 0, 200);
        }
        if (rotation == 0) Assert.InRange(bounds.TopLeft.Y, 40, 65);
    }

    [Fact]
    public async Task RebuildsThroughNeutralContractsWithPixelEquality()
    {
        await using var source = await Backend.OpenAsync(PdfFixtures.Create(hiddenContent: true), new PdfBackendLimits());
        var page = await source.ReadPageAsync(0);
        var raster = await source.RenderPageAsync(0, 200, 200);
        await using var writer = await Backend.CreateRasterWriterAsync(new PdfBackendLimits());
        await writer.AddPageAsync(page.Size, raster);
        var bytes = await writer.CompleteAsync();
        await using var output = await Backend.OpenAsync(bytes, new PdfBackendLimits());
        Assert.Empty((await output.ReadPageAsync(0)).Text);
        Assert.Equal(raster.Bgra.ToArray(), (await output.RenderPageAsync(0, 200, 200)).Bgra.ToArray());
        Assert.DoesNotContain(PdfFixtures.HiddenSecret, PdfFixtures.Recover(bytes.ToArray()), StringComparison.Ordinal);
        await Assert.ThrowsAsync<RedactionException>(() => writer.CompleteAsync().AsTask());
    }

    [Fact]
    public async Task EnforcesInputPageCharacterAndPixelLimits()
    {
        var bytes = PdfFixtures.Create();
        var error = await Assert.ThrowsAsync<RedactionException>(() => Backend.OpenAsync(bytes, new PdfBackendLimits { MaximumInputBytes = 1 }).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        await using var document = await Backend.OpenAsync(bytes, new PdfBackendLimits { MaximumCharactersPerPage = 1, MaximumPixelsPerPage = 10 });
        error = await Assert.ThrowsAsync<RedactionException>(() => document.ReadPageAsync(0).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        error = await Assert.ThrowsAsync<RedactionException>(() => document.RenderPageAsync(0, 200, 200).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.ReadPageAsync(1).AsTask());
    }

    [Fact]
    public async Task FailedLoadIsSanitizedAndRuntimeRecovers()
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => Backend.OpenAsync("private-invalid-input"u8.ToArray(), new PdfBackendLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
        await using var document = await Backend.OpenAsync(PdfFixtures.Create(), new PdfBackendLimits());
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public async Task CancellationAndDisposalDoNotLeakSessions()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Backend.OpenAsync(PdfFixtures.Create(), new PdfBackendLimits(), cancellation.Token).AsTask());
        var document = await Backend.OpenAsync(PdfFixtures.Create(), new PdfBackendLimits());
        Assert.Throws<InvalidOperationException>(fixture.Runtime.Dispose);
        await document.DisposeAsync();
        await document.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => document.ReadPageAsync(0).AsTask());
    }

    [Fact]
    public async Task WriterEnforcesOutputAndAggregateBudgets()
    {
        var raster = new PdfRaster(1, 1, new byte[] { 0, 0, 0, 255 });
        await using var writer = await Backend.CreateRasterWriterAsync(new PdfBackendLimits { MaximumOutputBytes = 1 });
        await writer.AddPageAsync(new PdfPageSize(200, 200), raster);
        var error = await Assert.ThrowsAsync<RedactionException>(() => writer.CompleteAsync().AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        await using var bounded = await Backend.CreateRasterWriterAsync(new PdfBackendLimits { MaximumPixelsPerPage = 1, MaximumTotalPixels = 1 });
        await bounded.AddPageAsync(new PdfPageSize(200, 200), raster);
        error = await Assert.ThrowsAsync<RedactionException>(() => bounded.AddPageAsync(new PdfPageSize(200, 200), raster).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
    }

    [Fact]
    public async Task SharedRuntimeSerializesConcurrentDocumentCalls()
    {
        await using var document = await Backend.OpenAsync(PdfFixtures.Create(), new PdfBackendLimits());
        var pages = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () => await document.ReadPageAsync(0))));
        Assert.All(pages, page => Assert.Contains(PdfFixtures.Secret, page.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenDocumentAdmissionRecoversAfterRejectedWriter()
    {
        List<IPdfDocumentSession> documents = new();
        try
        {
            for (var i = 0; i < 8; i++)
                documents.Add(await Backend.OpenAsync(PdfFixtures.Create(), new PdfBackendLimits()));
            var error = await Assert.ThrowsAsync<RedactionException>(() => Backend.OpenAsync(PdfFixtures.Create(), new PdfBackendLimits()).AsTask());
            Assert.Equal(RedactionError.LimitExceeded, error.Error);
            error = await Assert.ThrowsAsync<RedactionException>(() => Backend.CreateRasterWriterAsync(new PdfBackendLimits()).AsTask());
            Assert.Equal(RedactionError.LimitExceeded, error.Error);
        }
        finally
        {
            foreach (var document in documents) await document.DisposeAsync();
        }
        await using (var writer = await Backend.CreateRasterWriterAsync(new PdfBackendLimits())) { }
    }

    [Fact]
    public async Task EmptyWriterAndInvalidGeometryFailClosed()
    {
        await using var writer = await Backend.CreateRasterWriterAsync(new PdfBackendLimits());
        await Assert.ThrowsAsync<RedactionException>(() => writer.CompleteAsync().AsTask());
        await Assert.ThrowsAsync<RedactionException>(() => writer.AddPageAsync(new PdfPageSize(double.NaN, 200), new PdfRaster(1, 1, new byte[] { 0, 0, 0, 255 })).AsTask());
        var raster = new PdfRaster(1, 1, new byte[] { 0, 0, 0, 255 });
        await writer.AddPageAsync(new PdfPageSize(200, 200), raster);
        await writer.CompleteAsync();
    }
}
