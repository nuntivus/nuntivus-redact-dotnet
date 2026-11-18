using NuntivusRedact.Formats.Images;
using NuntivusRedact.Ocr;
using SkiaSharp;
using Xunit;

namespace NuntivusRedact.Formats.Tests;

public sealed class OcrSecurityTests
{
    [Fact]
    public async Task RejectsApngAndTruncatedPngBeforeRecognition()
    {
        var png = OcrFixtures.Image();
        var animated = png[..33].Concat(new byte[] { 0, 0, 0, 0, 97, 99, 84, 76, 0, 0, 0, 0 }).Concat(png[33..]).ToArray();
        var provider = new StubOcrProvider();
        using var redactor = new RasterImageRedactor(provider);
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(animated, "image/png", OfficeFixtures.Engine(), 9600));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(png[..^1], "image/png", OfficeFixtures.Engine(), 9600));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task RejectsExifRotationRatherThanMisplacingRedaction()
    {
        byte[] app1 = Convert.FromHexString("FFE1002245786966000049492A0008000000010012010300010000000600000000000000");
        var jpeg = OcrFixtures.Image(true);
        var rotated = jpeg[..2].Concat(app1).Concat(jpeg[2..]).ToArray();
        var provider = new StubOcrProvider();
        using var redactor = new RasterImageRedactor(provider);
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(rotated, "image/jpeg", OfficeFixtures.Engine(), 9600));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EnforcesEncodedTextWordAndOutputBudgets(int kind)
    {
        var limits = kind switch
        {
            0 => new ImageLimits { MaximumInputBytes = 1 },
            1 => new ImageLimits { MaximumTextLength = 1 },
            2 => new ImageLimits { MaximumWords = 1 },
            _ => new ImageLimits { MaximumOutputBytes = 1 }
        };
        using var redactor = new RasterImageRedactor(new StubOcrProvider(), limits);
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
    }

    [Fact]
    public async Task RemovesAllWordsTouchedByASingleDetection()
    {
        var provider = new StubOcrProvider((_, _) => new OcrResult("alice@example.com", [new(new(0, 5), new(10, 10, 40, 40)), new(new(5, 12), new(60, 10, 90, 40))]));
        using var redactor = new RasterImageRedactor(provider);
        var result = await redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600);
        Assert.Single(result.Detections);
        using var pixels = SKBitmap.Decode(result.Png.ToArray());
        Assert.Equal(SKColors.Black, pixels.GetPixel(20, 20));
        Assert.Equal(SKColors.Black, pixels.GetPixel(70, 20));
    }

    [Fact]
    public async Task RejectsUnreconstructableTextEdits()
    {
        using var redactor = new RasterImageRedactor(new StubOcrProvider());
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await redactor.RedactAsync(OcrFixtures.Image(), "image/png", new InvalidRedactor(), 9600));
        Assert.Equal(RedactionError.InvalidDetection, error.Error);
    }

    [Fact]
    public async Task QueuedCancellationDoesNotInvokeAnotherNativeOperation()
    {
        var provider = new BlockingProvider();
        using var redactor = new RasterImageRedactor(provider, new ImageLimits { MaximumConcurrency = 1 });
        var first = redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600).AsTask();
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        try
        {
            var second = redactor.RedactAsync(OcrFixtures.Image(), "image/png", OfficeFixtures.Engine(), 9600, cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.Equal(1, provider.Calls);
        }
        finally
        {
            provider.Release.TrySetResult();
            await first;
        }
    }

    private sealed class InvalidRedactor : IEditAwareTextRedactor
    {
        public ValueTask<EditRedactionResult> RedactWithEditsAsync(string text, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EditRedactionResult("different", []));
        public ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BlockingProvider : IOcrProvider
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        public async ValueTask<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> png, int width, int height, CancellationToken cancellationToken = default)
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new OcrResult("", []);
        }
    }
}
