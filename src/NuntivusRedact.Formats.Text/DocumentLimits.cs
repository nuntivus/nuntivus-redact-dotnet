namespace NuntivusRedact.Formats.Text;

/// <summary>Immutable resource budgets for buffered UTF-8 processing.</summary>
public sealed record DocumentLimits
{
    /// <summary>Gets the maximum encoded input size.</summary>
    public int MaximumInputBytes { get; init; } = 4_000_000;
    /// <summary>Gets the maximum encoded output size.</summary>
    public int MaximumOutputBytes { get; init; } = 8_000_000;
    /// <summary>Gets the maximum nesting depth.</summary>
    public int MaximumDepth { get; init; } = 64;
    /// <summary>Gets the maximum number of inspected fields and structural nodes.</summary>
    public int MaximumFields { get; init; } = 10_000;
    /// <summary>Gets the maximum number of report detections.</summary>
    public int MaximumDetections { get; init; } = 10_000;
    internal void Validate()
    {
        if (MaximumInputBytes is < 1 or > 100_000_000 || MaximumOutputBytes is < 1 or > 100_000_000 ||
            MaximumDepth is < 1 or > 256 || MaximumFields is < 1 or > 1_000_000 || MaximumDetections is < 1 or > 1_000_000)
            throw new ArgumentException("Document limits are outside supported ranges.");
    }
}

/// <summary>A per-request text-redaction and reporting budget; never retain it after processing.</summary>
public sealed class DocumentProcessingContext
{
    private readonly ITextRedactor redactor;
    private readonly DocumentLimits limits;
    private readonly CancellationToken cancellationToken;
    private readonly List<DocumentDetection> detections = [];
    private int fields;
    private long outputCharacters;
    internal DocumentProcessingContext(ITextRedactor redactor, DocumentLimits limits, CancellationToken cancellationToken)
    {
        this.redactor = redactor;
        this.limits = limits;
        this.cancellationToken = cancellationToken;
    }
    /// <summary>Redacts one semantic field and records only numeric paths and accepted detections.</summary>
    public async ValueTask<string> RedactAsync(string text, string location)
    {
        InspectNode();
        var result = await redactor.RedactAsync(text, cancellationToken).ConfigureAwait(false);
        outputCharacters += result.Text.Length;
        if (outputCharacters > limits.MaximumOutputBytes || result.Detections.Count > limits.MaximumDetections - detections.Count)
            throw new RedactionException(RedactionError.LimitExceeded);
        foreach (var detection in result.Detections) detections.Add(new(location, detection));
        return result.Text;
    }
    /// <summary>Consumes a structural-node budget without redacting a field.</summary>
    public void InspectNode()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++fields > limits.MaximumFields) throw new RedactionException(RedactionError.LimitExceeded);
    }
    /// <summary>Rejects sensitive matches in structural identifiers that cannot safely be renamed.</summary>
    public async ValueTask AssertUnchangedAsync(string text)
    {
        int before = detections.Count;
        var result = await RedactAsync(text, "/structure").ConfigureAwait(false);
        if (before != detections.Count || result != text) throw new RedactionException(RedactionError.UnsupportedFormat);
    }
    internal DocumentRedactionResult Report() => new(detections);
}
