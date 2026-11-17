namespace NuntivusRedact;

/// <summary>Source-free location in a structured document, using numeric structural paths rather than field names.</summary>
public sealed record DocumentDetection
{
    /// <summary>Creates a location without storing the sensitive field value.</summary>
    public DocumentDetection(string location, Detection detection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        Location = location;
        Detection = detection ?? throw new ArgumentNullException(nameof(detection));
    }
    /// <summary>Gets a processor-defined structural path containing no source field names.</summary>
    public string Location { get; }
    /// <summary>Gets field-relative UTF-16 coordinates.</summary>
    public Detection Detection { get; }
}

/// <summary>Document processing report; never a guarantee of complete sensitive-data discovery.</summary>
public sealed class DocumentRedactionResult
{
    /// <summary>Copies source-free document detections.</summary>
    public DocumentRedactionResult(IEnumerable<DocumentDetection> detections)
    {
        ArgumentNullException.ThrowIfNull(detections);
        Detections = Array.AsReadOnly(detections.ToArray());
    }
    /// <summary>Gets accepted field-relative detections in numeric structural locations.</summary>
    public IReadOnlyList<DocumentDetection> Detections { get; }
}

/// <summary>Explicit format extension; caller-owned streams must stay open.</summary>
public interface IDocumentProcessor
{
    /// <summary>Gets a stable format identifier.</summary>
    string Format { get; }
    /// <summary>Stages processing before writing; discard the destination on any exception, including output I/O failure.</summary>
    ValueTask<DocumentRedactionResult> ProcessAsync(Stream input, Stream output, CancellationToken cancellationToken = default);
}
