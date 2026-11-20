using System.Runtime.InteropServices;
using System.Text;
using PDFiumCore;

namespace NuntivusRedact.Pdf.PdfiumCore;

/// <summary>Low-level extraction/rendering/fresh-raster adapter. Does not inventory hidden content or implement secure native redaction.</summary>
public sealed class PdfiumBackend : IPdfBackend, IPdfNativeObjectBackend
{
    private readonly PdfiumRuntime runtime;
    /// <summary>Uses the caller's shared native runtime. No native types cross the backend-neutral boundary.</summary>
    public PdfiumBackend(PdfiumRuntime runtime) => this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    /// <inheritdoc />
    public PdfBackendCapabilities Capabilities { get; } = new(true, true, true, false);

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> RemoveObjectsAsync(ReadOnlyMemory<byte> input, IReadOnlyList<PdfObjectRemovalRegion> regions,
        PdfBackendLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (input.IsEmpty) throw new RedactionException(RedactionError.InvalidDocument);
        if (input.Length > limits.MaximumInputBytes || regions.Count > limits.MaximumCharactersPerPage)
            throw new RedactionException(RedactionError.LimitExceeded);
        var snapshot = regions.ToArray();
        if (snapshot.Any(region => region is null)) throw new ArgumentException("Invalid native region.");
        return runtime.RunAsync(() => new NativeObjectRemoval().Execute(input, snapshot, limits, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IPdfDocumentSession> OpenAsync(ReadOnlyMemory<byte> input, PdfBackendLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (input.IsEmpty) throw new RedactionException(RedactionError.InvalidDocument);
        if (input.Length > limits.MaximumInputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        return await runtime.RunAsync<IPdfDocumentSession>(() =>
        {
            var bytes = input.ToArray();
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            FpdfDocumentT? document = null;
            try
            {
                document = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.Length, null!);
                if (document is null) throw new RedactionException(RedactionError.InvalidDocument);
                int pages = fpdfview.FPDF_GetPageCount(document);
                if (pages < 1) throw new RedactionException(RedactionError.InvalidDocument);
                if (pages > limits.MaximumPages) throw new RedactionException(RedactionError.LimitExceeded);
                cancellationToken.ThrowIfCancellationRequested();
                runtime.AddDocument();
                return new DocumentSession(runtime, document, pin, limits, pages);
            }
            catch
            {
                if (document is not null) fpdfview.FPDF_CloseDocument(document);
                pin.Free();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IPdfRasterWriter> CreateRasterWriterAsync(PdfBackendLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        return await runtime.RunAsync<IPdfRasterWriter>(() =>
        {
            var document = fpdf_edit.FPDF_CreateNewDocument();
            if (document is null) throw new RedactionException(RedactionError.ExtensionFailed);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                runtime.AddDocument();
            }
            catch
            {
                fpdfview.FPDF_CloseDocument(document);
                throw;
            }
            return new RasterWriter(runtime, document, limits);
        }, cancellationToken).ConfigureAwait(false);
    }

    private sealed class DocumentSession(PdfiumRuntime runtime, FpdfDocumentT document, GCHandle pin, PdfBackendLimits limits, int pageCount) : IPdfDocumentSession
    {
        private bool disposed;
        public int PageCount { get; } = pageCount;

        private FpdfPageT LoadPage(int index)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
            return fpdfview.FPDF_LoadPage(document, index) ?? throw new RedactionException(RedactionError.InvalidDocument);
        }

        private PdfPageSize GetSize(FpdfPageT page)
        {
            var size = new PdfPageSize(fpdfview.FPDF_GetPageWidth(page), fpdfview.FPDF_GetPageHeight(page));
            if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
                throw new RedactionException(RedactionError.InvalidDocument);
            if (size.Width > limits.MaximumPageDimensionPoints || size.Height > limits.MaximumPageDimensionPoints ||
                size.Width > int.MaxValue / 1000d || size.Height > int.MaxValue / 1000d)
                throw new RedactionException(RedactionError.LimitExceeded);
            return size;
        }

        public ValueTask<PdfPageText> ReadPageAsync(int pageIndex, CancellationToken cancellationToken = default) => runtime.RunAsync(() =>
        {
            var page = LoadPage(pageIndex);
            try
            {
                var size = GetSize(page);
                var textPage = fpdf_text.FPDFTextLoadPage(page);
                if (textPage is null) throw new RedactionException(RedactionError.InvalidDocument);
                try
                {
                    int count = fpdf_text.FPDFTextCountChars(textPage);
                    if (count < 0) throw new RedactionException(RedactionError.InvalidDocument);
                    if (count > limits.MaximumCharactersPerPage) throw new RedactionException(RedactionError.LimitExceeded);
                    var text = new StringBuilder();
                    var characters = new List<PdfTextCharacter>();
                    int deviceWidth = Math.Max(1, (int)Math.Ceiling(size.Width * 1000));
                    int deviceHeight = Math.Max(1, (int)Math.Ceiling(size.Height * 1000));
                    PdfPoint Transform(double x, double y)
                    {
                        int dx = 0, dy = 0;
                        if (fpdfview.FPDF_PageToDevice(page, 0, 0, deviceWidth, deviceHeight, 0, x, y, ref dx, ref dy) != 1)
                            throw new RedactionException(RedactionError.InvalidDocument);
                        return new PdfPoint(dx * size.Width / deviceWidth, dy * size.Height / deviceHeight);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        uint scalar = fpdf_text.FPDFTextGetUnicode(textPage, i);
                        if (scalar == 0 || scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF ||
                            fpdf_text.FPDFTextHasUnicodeMapError(textPage, i) != 0)
                            throw new RedactionException(RedactionError.UnsupportedFormat);
                        string character = char.ConvertFromUtf32((int)scalar);
                        int start = text.Length;
                        if (start > limits.MaximumCharactersPerPage - character.Length)
                            throw new RedactionException(RedactionError.LimitExceeded);
                        text.Append(character);
                        double left = 0, right = 0, bottom = 0, top = 0;
                        PdfQuadrilateral? bounds = null;
                        if (fpdf_text.FPDFTextGetCharBox(textPage, i, ref left, ref right, ref bottom, ref top) == 1 &&
                            double.IsFinite(left) && double.IsFinite(right) && double.IsFinite(bottom) && double.IsFinite(top) && right > left && top > bottom)
                            bounds = new PdfQuadrilateral(Transform(left, top), Transform(right, top), Transform(right, bottom), Transform(left, bottom));
                        characters.Add(new PdfTextCharacter(new DetectionSpan(start, character.Length), bounds));
                    }
                    return new PdfPageText(size, text.ToString(), characters);
                }
                finally { fpdf_text.FPDFTextClosePage(textPage); }
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }, cancellationToken);

        public ValueTask<PdfRaster> RenderPageAsync(int pageIndex, int width, int height, CancellationToken cancellationToken = default) => runtime.RunAsync(() =>
        {
            if (width < 1 || height < 1 || (long)width * height > limits.MaximumPixelsPerPage || (long)width * height > int.MaxValue / 4)
                throw new RedactionException(RedactionError.LimitExceeded);
            var page = LoadPage(pageIndex);
            try
            {
                _ = GetSize(page);
                var bitmap = fpdfview.FPDFBitmapCreate(width, height, 1);
                if (bitmap is null) throw new RedactionException(RedactionError.ExtensionFailed);
                try
                {
                    if (fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF) != 1)
                        throw new RedactionException(RedactionError.ExtensionFailed);
                    fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 1);
                    cancellationToken.ThrowIfCancellationRequested();
                    int stride = fpdfview.FPDFBitmapGetStride(bitmap);
                    if (stride != width * 4) throw new RedactionException(RedactionError.ExtensionFailed);
                    var pixels = new byte[checked(stride * height)];
                    Marshal.Copy(fpdfview.FPDFBitmapGetBuffer(bitmap), pixels, 0, pixels.Length);
                    return new PdfRaster(width, height, pixels);
                }
                finally { fpdfview.FPDFBitmapDestroy(bitmap); }
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await runtime.CleanupAsync(() =>
            {
                if (disposed) return;
                fpdfview.FPDF_CloseDocument(document);
                pin.Free();
                disposed = true;
                runtime.RemoveDocument();
            }).ConfigureAwait(false);
        }
    }
}
