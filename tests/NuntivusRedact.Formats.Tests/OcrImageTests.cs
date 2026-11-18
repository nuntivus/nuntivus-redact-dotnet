using System.Text;
using NuntivusRedact.Formats.Images;
using NuntivusRedact.Ocr;
using NuntivusRedact.Ocr.Tesseract;
using SkiaSharp;
using Xunit;

namespace NuntivusRedact.Formats.Tests;

public sealed class OcrImageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacesPixelsAndDropsOriginalPayloadAndMetadata(bool jpeg)
    {
        var source = OcrFixtures.Image(jpeg);
        if (!jpeg) source = OcrFixtures.AddPngMetadata(source);
        using var redactor = new RasterImageRedactor(new StubOcrProvider());
        var result = await redactor.RedactAsync(source, jpeg ? "image/jpeg" : "image/png", OfficeFixtures.Engine(), 9600);
        Assert.Single(result.Detections);
        using var pixels = SKBitmap.Decode(result.Png.ToArray());
        Assert.Equal(160, pixels.Width);
        Assert.Equal(60, pixels.Height);
        for (int y = 7; y < 43; y++)
            for (int x = 7; x < 93; x++) Assert.Equal(SKColors.Black, pixels.GetPixel(x, y));
        Assert.Equal(SKColors.White, pixels.GetPixel(130, 25));
        Assert.DoesNotContain("alice", Encoding.UTF8.GetString(result.Png.Span), StringComparison.OrdinalIgnoreCase);
        Assert.False(source.AsSpan().SequenceEqual(result.Png.Span));
    }

    [Fact]
    public async Task FlattensHiddenTransparentPixelsEvenWithoutMatches()
    {
        using var redactor = new RasterImageRedactor(new StubOcrProvider((_, _) => new OcrResult("", [])));
        var result = await redactor.RedactAsync(OcrFixtures.Image(transparent: true), "image/png", OfficeFixtures.Engine(), 9600);
        using var pixels = SKBitmap.Decode(result.Png.ToArray());
        Assert.Equal(SKColors.White, pixels.GetPixel(20, 20));
        Assert.Empty(result.Detections);
    }

    [Fact]
    public async Task ProviderCanBeReplacedWithoutChangingThePipeline()
    {
        using var first = new RasterImageRedactor(new StubOcrProvider());
        using var second = new RasterImageRedactor(new StubOcrProvider((_, _) => new OcrResult("safe", [new(new(0, 4), new(10, 10, 90, 40))])));
        var input = OcrFixtures.Image();
        Assert.Single((await first.RedactAsync(input, "image/png", OfficeFixtures.Engine(), 9600)).Detections);
        Assert.Empty((await second.RedactAsync(input, "image/png", OfficeFixtures.Engine(), 9600)).Detections);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task RejectsInvalidRecognitionCoverageGeometryAndConfidence(int kind)
    {
        var result = kind switch
        {
            0 => new OcrResult("alice@example.com", []),
            1 => new OcrResult("alice@example.com", [new(new(0, 17), new(-1, 0, 10, 10))]),
            2 => new OcrResult("alice@example.com", [new(new(0, 17), new(0, 0, 161, 10))]),
            3 => new OcrResult("alice@example.com", [new(new(1, 16), new(0, 0, 10, 10))]),
            4 => new OcrResult("alice@example.com", [new(new(0, 17), new(0, 0, 10, 10), double.NaN)]),
            _ => new OcrResult("alice@example.com", [new(new(0, 17), new(0, 0, 10, 10)), new(new(0, 1), new(0, 0, 5, 5))])
        };
        using var redactor = new RasterImageRedactor(new StubOcrProvider((_, _) => result));
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600));
        Assert.Equal(RedactionError.InvalidDetection, error.Error);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("alice", error.ToString());
    }

    [Fact]
    public async Task RejectsPixelLimitBeforeCallingProvider()
    {
        var provider = new StubOcrProvider();
        using var redactor = new RasterImageRedactor(provider);
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9599));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task CancelsAndSanitizesProviderFailure()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var redactor = new RasterImageRedactor(new StubOcrProvider((_, _) => throw new InvalidOperationException("alice@example.com")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600, cancelled.Token));
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600));
        Assert.Equal(RedactionError.ExtensionFailed, error.Error);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("alice", error.ToString());
    }

    [Fact]
    public async Task RejectsMismatchedOrMalformedImageData()
    {
        using var redactor = new RasterImageRedactor(new StubOcrProvider());
        await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/jpeg", OfficeFixtures.Engine(), 9600));
        await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync("broken"u8.ToArray(), "image/png", OfficeFixtures.Engine(), 9600));
        await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/svg+xml", OfficeFixtures.Engine(), 9600));
    }

    [Fact]
    public async Task ConcurrentOperationsRemainDeterministic()
    {
        using var redactor = new RasterImageRedactor(new StubOcrProvider());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            await redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600))));
        Assert.All(results, result => Assert.Equal(results[0].Png.ToArray(), result.Png.ToArray()));
    }

    [Fact]
    public void ValidatesNativeConfigurationWithoutDownloadingData()
    {
        Assert.Throws<ArgumentException>(() => new TesseractOcrProvider(new TesseractOptions { DataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }));
        Assert.Throws<ArgumentException>(() => new TesseractOcrProvider(new TesseractOptions { DataPath = ".", Languages = "../eng" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterImageRedactor(new StubOcrProvider(), new ImageLimits { MaximumPixels = 0 }));
    }
}
