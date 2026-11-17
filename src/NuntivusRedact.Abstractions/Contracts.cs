namespace NuntivusRedact;

/// <summary>Input for a detector. Do not retain or log this context.</summary>
public sealed class DetectionContext
{
    /// <summary>Creates a context for an original text segment.</summary>
    public DetectionContext(string text) => Text = text ?? throw new ArgumentNullException(nameof(text));
    /// <summary>Gets sensitive source text. Offsets are UTF-16 code units.</summary>
    public string Text { get; }
}

/// <summary>A thread-safe detector explicitly supplied by the application.</summary>
public interface ISensitiveDataDetector
{
    /// <summary>Gets a stable, non-sensitive identifier unique within a pipeline.</summary>
    string Id { get; }
    /// <summary>Finds candidates without retaining source values in results.</summary>
    ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default);
}

/// <summary>Transforms one accepted original span. Implementations must be thread safe.</summary>
public interface IRedactionStrategy
{
    /// <summary>Returns a replacement; never log source or throw exceptions containing it.</summary>
    string Redact(ReadOnlySpan<char> source, Detection detection);
}

/// <summary>Applies a configured detection and replacement policy.</summary>
public interface ITextRedactor
{
    /// <summary>Redacts accepted matches, not a guarantee of complete sensitive-data discovery.</summary>
    ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Replacement output and accepted source-free detections. Contains sensitive output; never log it.</summary>
public sealed class RedactionResult
{
    /// <summary>Creates an output with a defensive copy of source-free detections.</summary>
    public RedactionResult(string text, IEnumerable<Detection> detections)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        ArgumentNullException.ThrowIfNull(detections);
        Detections = Array.AsReadOnly(detections.ToArray());
    }
    /// <summary>Gets output; undetected sensitive data may remain.</summary>
    public string Text { get; }
    /// <summary>Gets accepted detections in original input coordinates.</summary>
    public IReadOnlyList<Detection> Detections { get; }
}

/// <summary>Safe failure codes, never document values.</summary>
public enum RedactionError
{
    /// <summary>Resource budget exceeded.</summary>
    LimitExceeded,
    /// <summary>An extension failed.</summary>
    ExtensionFailed,
    /// <summary>A detector produced an invalid result.</summary>
    InvalidDetection,
    /// <summary>Malformed or unsafe structured document.</summary>
    InvalidDocument,
    /// <summary>Format or feature is unsupported.</summary>
    UnsupportedFormat
}

/// <summary>A sanitized processing failure with no source-bearing inner exception.</summary>
public sealed class RedactionException : Exception
{
    /// <summary>Creates a safe failure.</summary>
    public RedactionException(RedactionError error) : base($"Redaction failed: {error}.") => Error = error;
    /// <summary>Gets the non-sensitive failure code.</summary>
    public RedactionError Error { get; }
}
