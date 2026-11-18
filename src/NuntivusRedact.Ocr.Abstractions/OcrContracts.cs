namespace NuntivusRedact.Ocr;

/// <summary>Axis-aligned pixel bounds in the unrotated input raster; origin is top-left, right/bottom are exclusive.</summary>
public readonly record struct OcrBounds(int Left, int Top, int Right, int Bottom);

/// <summary>A word's UTF-16 span in the complete recognized text and its covering pixel rectangle.</summary>
public sealed record OcrWord(DetectionSpan Span, OcrBounds Bounds, double? Confidence = null);

/// <summary>Sensitive recognition data. Never log text or serialize it into reports.</summary>
public sealed class OcrResult
{
    /// <summary>Copies ordered words. Consumers must validate text coverage, spans and geometry before redaction.</summary>
    public OcrResult(string text, IEnumerable<OcrWord> words)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        ArgumentNullException.ThrowIfNull(words);
        Words = Array.AsReadOnly(words.ToArray());
    }
    /// <summary>Gets sensitive recognized text, with words in reading order.</summary>
    public string Text { get; }
    /// <summary>Gets words covering every non-whitespace character in Text.</summary>
    public IReadOnlyList<OcrWord> Words { get; }
}

/// <summary>Replaceable OCR backend. Implementations must be reusable and safe for concurrent calls.</summary>
public interface IOcrProvider
{
    /// <summary>Recognizes a bounded, single-frame canonical PNG. Return coordinates in its original pixels, without applying orientation transforms. Cancellation may be cooperative at native-call boundaries.</summary>
    ValueTask<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> png, int width, int height, CancellationToken cancellationToken = default);
}

/// <summary>Optional image redaction extension, independent of Office and of any native OCR SDK.</summary>
public interface IImageRedactor
{
    /// <summary>Rebuilds a PNG/JPEG as metadata-free PNG with matched pixel regions replaced. Enforce the caller's remaining pixel budget before decoding pixels. Never return partial success. Returned detections use recognized-text UTF-16 offsets.</summary>
    ValueTask<ImageRedactionResult> RedactAsync(ReadOnlyMemory<byte> image, string contentType,
        IEditAwareTextRedactor redactor, long maximumPixels, CancellationToken cancellationToken = default);
}

/// <summary>Rebuilt image and source-free detections. Pixel data remains potentially sensitive.</summary>
public sealed class ImageRedactionResult
{
    private readonly byte[] png;
    /// <summary>Copies encoded pixels and detections. The caller must still enforce aggregate document budgets.</summary>
    public ImageRedactionResult(ReadOnlyMemory<byte> png, int width, int height, IEnumerable<Detection> detections)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        ArgumentNullException.ThrowIfNull(detections);
        this.png = png.ToArray();
        Width = width;
        Height = height;
        Detections = Array.AsReadOnly(detections.ToArray());
    }
    /// <summary>Gets rebuilt PNG bytes. Contains no original compressed payload or source metadata.</summary>
    public ReadOnlyMemory<byte> Png => png;
    /// <summary>Gets raster width.</summary>
    public int Width { get; }
    /// <summary>Gets raster height.</summary>
    public int Height { get; }
    /// <summary>Gets accepted recognized-text detections; not a completeness guarantee.</summary>
    public IReadOnlyList<Detection> Detections { get; }
}
