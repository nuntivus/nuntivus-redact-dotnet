namespace NuntivusRedact.Pdf;

/// <summary>Explicit output fidelity choices. Unsupported choices must fail, never silently fall back.</summary>
public enum PdfOutputMode
{
    /// <summary>Fresh image-only pages; source selectable text, vectors and accessibility are not retained.</summary>
    Rasterized,
    /// <summary>Retains supported unaffected native content; requires verified irreversible removal capabilities.</summary>
    NativePreserving
}

/// <summary>Capabilities of an adapter, not a guarantee that a document is supported or securely redacted.</summary>
public sealed record PdfBackendCapabilities(bool NativeTextExtraction, bool Rendering, bool RasterWriting, bool NativeRemoval);

/// <summary>Displayed page dimensions in PDF points (1/72 inch), after supported crop and rotation.</summary>
public readonly record struct PdfPageSize(double Width, double Height);

/// <summary>Point in displayed page space, origin top-left, x rightward and y downward, in PDF points.</summary>
public readonly record struct PdfPoint(double X, double Y);

/// <summary>Conservative glyph covering geometry in displayed page space. May include extra surrounding content.</summary>
public readonly record struct PdfQuadrilateral(PdfPoint TopLeft, PdfPoint TopRight, PdfPoint BottomRight, PdfPoint BottomLeft);

/// <summary>Original UTF-16 span of one extracted character and its conservative covering geometry, if available.</summary>
public sealed record PdfTextCharacter(DetectionSpan Span, PdfQuadrilateral? Bounds);

/// <summary>Sensitive extracted page data; never log, retain in telemetry or serialize into source-free reports.</summary>
public sealed class PdfPageText
{
    /// <summary>Copies character mapping. Consumers must validate coverage and geometry before mapping detections.</summary>
    public PdfPageText(PdfPageSize size, string text, IEnumerable<PdfTextCharacter> characters)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(characters);
        Size = size;
        Text = text;
        Characters = Array.AsReadOnly(characters.ToArray());
    }
    /// <summary>Gets displayed dimensions.</summary>
    public PdfPageSize Size { get; }
    /// <summary>Gets sensitive original extracted text; backend order is not necessarily logical reading order.</summary>
    public string Text { get; }
    /// <summary>Gets original-coordinate characters; missing bounds must not silently drop accepted redactions.</summary>
    public IReadOnlyList<PdfTextCharacter> Characters { get; }
}

/// <summary>Owned sensitive opaque BGRA pixels, top-down tightly packed rows. Not a source-free report.</summary>
public sealed class PdfRaster
{
    private readonly byte[] pixels;
    /// <summary>Validates dimensions, byte length and opaque alpha, then copies pixels.</summary>
    public PdfRaster(int width, int height, ReadOnlyMemory<byte> bgra)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if ((long)width * height * 4 != bgra.Length) throw new ArgumentException("Invalid raster length.", nameof(bgra));
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra.Span[i] != 255) throw new ArgumentException("Raster must be opaque.", nameof(bgra));
        Width = width;
        Height = height;
        pixels = bgra.ToArray();
    }
    /// <summary>Gets pixel width.</summary>
    public int Width { get; }
    /// <summary>Gets pixel height.</summary>
    public int Height { get; }
    /// <summary>Gets owned sensitive opaque BGRA pixels. Consumers must not mutate underlying memory.</summary>
    public ReadOnlyMemory<byte> Bgra => pixels;
}

/// <summary>Immutable managed admission/resource limits. Native memory, crashes and time require external isolation.</summary>
public sealed record PdfBackendLimits
{
    /// <summary>Gets maximum input bytes retained by one document session.</summary>
    public int MaximumInputBytes { get; init; } = 32 * 1024 * 1024;
    /// <summary>Gets maximum pages per document.</summary>
    public int MaximumPages { get; init; } = 100;
    /// <summary>Gets maximum displayed width or height in PDF points.</summary>
    public double MaximumPageDimensionPoints { get; init; } = 14_400;
    /// <summary>Gets maximum pixels in a single rendered/written page.</summary>
    public long MaximumPixelsPerPage { get; init; } = 16_000_000;
    /// <summary>Gets maximum aggregate pixels submitted to a raster writer.</summary>
    public long MaximumTotalPixels { get; init; } = 64_000_000;
    /// <summary>Gets maximum UTF-16 code units extracted from one page.</summary>
    public int MaximumCharactersPerPage { get; init; } = 1_000_000;
    /// <summary>Gets maximum encoded output bytes from a raster writer.</summary>
    public int MaximumOutputBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Rejects invalid immutable configuration before processing.</summary>
    public void Validate()
    {
        if (MaximumInputBytes < 1 || MaximumPages < 1 || !double.IsFinite(MaximumPageDimensionPoints) ||
            MaximumPageDimensionPoints <= 0 || MaximumPixelsPerPage < 1 || MaximumTotalPixels < MaximumPixelsPerPage ||
            MaximumCharactersPerPage < 1 || MaximumOutputBytes < 1)
            throw new ArgumentException("Invalid PDF limits.");
    }
}

/// <summary>Replaceable low-level backend, not a secure document processor. Parsing hostile files requires isolation.</summary>
public interface IPdfBackend
{
    /// <summary>Gets proven adapter operations; format security support is a separate profile.</summary>
    PdfBackendCapabilities Capabilities { get; }
    /// <summary>Copies bounded input into an owned session. No password/encryption support is implied.</summary>
    ValueTask<IPdfDocumentSession> OpenAsync(ReadOnlyMemory<byte> input, PdfBackendLimits limits, CancellationToken cancellationToken = default);
    /// <summary>Creates an empty document writer; never copies source PDF objects or metadata.</summary>
    ValueTask<IPdfRasterWriter> CreateRasterWriterAsync(PdfBackendLimits limits, CancellationToken cancellationToken = default);
}

/// <summary>Owned document state. Dispose before the backend runtime; operations enforce configured managed limits.</summary>
public interface IPdfDocumentSession : IAsyncDisposable
{
    /// <summary>Gets bounded page count.</summary>
    int PageCount { get; }
    /// <summary>Extracts sensitive character text/geometry. This does not inventory hidden content.</summary>
    ValueTask<PdfPageText> ReadPageAsync(int pageIndex, CancellationToken cancellationToken = default);
    /// <summary>Renders at explicit pixel dimensions; does not prove all content is visible or safe.</summary>
    ValueTask<PdfRaster> RenderPageAsync(int pageIndex, int width, int height, CancellationToken cancellationToken = default);
}

/// <summary>Owned fresh image-only document builder. Input pixels remain potentially sensitive.</summary>
public interface IPdfRasterWriter : IAsyncDisposable
{
    /// <summary>Adds copied opaque pixels at displayed page dimensions, enforcing page and aggregate budgets.</summary>
    ValueTask AddPageAsync(PdfPageSize size, PdfRaster raster, CancellationToken cancellationToken = default);
    /// <summary>Completes once and returns bounded sensitive bytes; no source text layer is added.</summary>
    ValueTask<ReadOnlyMemory<byte>> CompleteAsync(CancellationToken cancellationToken = default);
}
