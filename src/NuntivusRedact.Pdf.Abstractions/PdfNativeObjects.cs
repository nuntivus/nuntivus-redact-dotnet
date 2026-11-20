namespace NuntivusRedact.Pdf;

/// <summary>Removal rectangle in displayed top-left PDF-point coordinates; page index is zero-based.</summary>
public sealed record PdfObjectRemovalRegion(int PageIndex, double Left, double Top, double Right, double Bottom);

/// <summary>Optional experimental whole-object native operation, not a secure document admission certificate.</summary>
public interface IPdfNativeObjectBackend
{
    /// <summary>Removes whole text/image objects intersecting regions and imports regenerated pages into a fresh document.
    /// May remove additional unaffected content within matched objects. Unsupported page features must abort.
    /// Caller must enforce input admission and independently validate recovery before security-sensitive use.</summary>
    ValueTask<ReadOnlyMemory<byte>> RemoveObjectsAsync(ReadOnlyMemory<byte> input, IReadOnlyList<PdfObjectRemovalRegion> regions,
        PdfBackendLimits limits, CancellationToken cancellationToken = default);
}
