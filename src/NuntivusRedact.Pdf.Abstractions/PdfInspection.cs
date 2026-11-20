namespace NuntivusRedact.Pdf;

/// <summary>Conservatively observed structural features. Absence of flags is not proof of complete content inspection.</summary>
[Flags]
public enum PdfStructuralFeatures
{
    /// <summary>No listed feature was observed; not an admission or anonymization guarantee.</summary>
    None = 0,
    /// <summary>Document information or metadata dictionaries/streams.</summary>
    Metadata = 1,
    /// <summary>Page annotations or annotation objects.</summary>
    Annotations = 2,
    /// <summary>AcroForm/XFA or form field structures.</summary>
    Forms = 4,
    /// <summary>File specifications, embedded files or associated files.</summary>
    Attachments = 8,
    /// <summary>Actions, JavaScript or other active-content structures.</summary>
    Actions = 16,
    /// <summary>Signatures or signature/recovery-related structures.</summary>
    Signatures = 32,
    /// <summary>Optional-content groups, layers or visibility properties.</summary>
    OptionalContent = 64,
    /// <summary>Reusable form XObjects, distinct from interactive forms.</summary>
    FormXObjects = 128,
    /// <summary>Image masks or soft-mask/transparency information.</summary>
    ImageMasks = 256,
    /// <summary>Embedded font program references.</summary>
    EmbeddedFonts = 512,
    /// <summary>Encoded stream payloads were intentionally not decoded or inspected.</summary>
    OpaqueStreams = 1024,
    /// <summary>Multiple cross-reference revisions; historical payloads are not inventoried completely.</summary>
    AdditionalRevisions = 2048,
    /// <summary>Current cross-reference objects not visited from the catalog and document-information roots; other trailer extensions are not covered.</summary>
    UnreferencedObjects = 4096,
    /// <summary>Unknown object types or invalid/unclassified type information.</summary>
    UnclassifiedObjects = 8192,
    /// <summary>Image XObjects.</summary>
    Images = 16384,
    /// <summary>Cross-reference stream syntax was observed; not proof of complete payload inspection.</summary>
    CrossReferenceStreams = 32768,
    /// <summary>A bounded cross-reference or object stream was decoded; other payloads remain opaque.</summary>
    DecodedStructuralStreams = 65536,
    /// <summary>Object-stream containers were observed; no historical or secure-removal guarantee.</summary>
    ObjectStreams = 131072
}

/// <summary>Source-free current-xref structural observations, not a complete inventory or permission to process securely.</summary>
public sealed record PdfStructuralInventory(int PageCount, int ObjectCount, int StreamCount, int MetadataStreamCount,
    int RevisionCount, int UnreferencedObjectCount, int MaximumTokenDepth, PdfStructuralFeatures Features);

/// <summary>Immutable bounds for managed structural inspection. Parser isolation is still required for hard time/memory containment.</summary>
public sealed record PdfInspectionLimits
{
    /// <summary>Gets maximum input bytes copied for inspection.</summary>
    public int MaximumInputBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Gets cumulative bytes read, including parser rewinds.</summary>
    public long MaximumReadBytes { get; init; } = 128 * 1024 * 1024;
    /// <summary>Gets maximum stream reads/seeks/property operations.</summary>
    public int MaximumStreamOperations { get; init; } = 1_000_000;
    /// <summary>Gets maximum current indirect objects.</summary>
    public int MaximumObjects { get; init; } = 10_000;
    /// <summary>Gets maximum cumulative token visits during inventory and reachability traversal.</summary>
    public int MaximumTokens { get; init; } = 200_000;
    /// <summary>Gets parser/traversal nesting bound. A parser nesting rejection may be reported as malformed input.</summary>
    public int MaximumDepth { get; init; } = 64;
    /// <summary>Gets maximum cross-reference sections.</summary>
    public int MaximumRevisions { get; init; } = 16;
    /// <summary>Gets maximum declared pages.</summary>
    public int MaximumPages { get; init; } = 100;
    /// <summary>Gets maximum encoded bytes in one stream; decoded payloads are not read by this inspector.</summary>
    public int MaximumEncodedStreamBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Gets maximum total encoded stream bytes across current indirect objects.</summary>
    public long MaximumTotalEncodedStreamBytes { get; init; } = 32 * 1024 * 1024;
    /// <summary>Gets maximum decoded bytes per supported structural cross-reference or object stream.</summary>
    public int MaximumDecodedStructuralStreamBytes { get; init; } = 4 * 1024 * 1024;
    /// <summary>Gets maximum cumulative decoded structural bytes, including repeated decoding.</summary>
    public long MaximumTotalDecodedStructuralStreamBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Rejects invalid configuration before input access.</summary>
    public void Validate()
    {
        if (MaximumInputBytes < 1 || MaximumReadBytes < 1 || MaximumStreamOperations < 1 || MaximumObjects < 1 ||
            MaximumTokens < 1 || MaximumDepth < 1 || MaximumRevisions < 1 || MaximumPages < 1 ||
            MaximumEncodedStreamBytes < 1 || MaximumTotalEncodedStreamBytes < 1 ||
            MaximumDecodedStructuralStreamBytes < 1 || MaximumTotalDecodedStructuralStreamBytes < 1)
            throw new ArgumentException("Invalid PDF inspection limits.");
    }
}

/// <summary>Optional replaceable structural inspection, independent of native rendering and secure format admission.</summary>
public interface IPdfStructuralInspector
{
    /// <summary>Inspects copied bounded input and returns source-free observations. Unsupported syntax or failed parsing must abort.
    /// Coverage limitations must be documented; never treat this result alone as proof of secure processing.</summary>
    ValueTask<PdfStructuralInventory> InspectAsync(ReadOnlyMemory<byte> input, PdfInspectionLimits limits, CancellationToken cancellationToken = default);
}
