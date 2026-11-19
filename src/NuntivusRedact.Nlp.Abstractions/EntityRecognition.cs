namespace NuntivusRedact.Nlp;

/// <summary>A source-free entity in the original input's UTF-16 coordinates.</summary>
public sealed record RecognizedEntity
{
    /// <summary>Creates an entity. Labels must be fixed model metadata, never source text.</summary>
    public RecognizedEntity(DetectionSpan span, string label, double confidence)
    {
        if (span.Length < 1) throw new ArgumentException("A nonempty span is required.", nameof(span));
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (label.Length > 64) throw new ArgumentException("Invalid entity label.", nameof(label));
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        Span = span;
        Label = label;
        Confidence = confidence;
    }

    /// <summary>Gets the range in the original input, not normalized text or token coordinates.</summary>
    public DetectionSpan Span { get; }
    /// <summary>Gets a fixed provider label such as PER, ORG or LOC, without a BIO prefix.</summary>
    public string Label { get; }
    /// <summary>Gets a heuristic score, not a calibrated probability or discovery guarantee.</summary>
    public double Confidence { get; }
}

/// <summary>A replaceable, concurrency-safe local entity recognizer. Never retain or log input text.</summary>
public interface IEntityRecognitionProvider
{
    /// <summary>Recognizes entities without changing the source coordinate system.</summary>
    /// <remarks>Implementations must bound work and fail rather than silently truncate input. Cancellation may be cooperative.</remarks>
    ValueTask<IReadOnlyList<RecognizedEntity>> RecognizeAsync(string text, CancellationToken cancellationToken = default);
}
