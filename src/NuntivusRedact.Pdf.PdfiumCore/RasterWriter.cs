using System.Runtime.InteropServices;
using PDFiumCore;

namespace NuntivusRedact.Pdf.PdfiumCore;

internal sealed class RasterWriter(PdfiumRuntime runtime, FpdfDocumentT document, PdfBackendLimits limits) : IPdfRasterWriter
{
    private bool disposed;
    private bool completed;
    private bool failed;
    private int pages;
    private long pixels;

    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed || failed) throw new RedactionException(RedactionError.InvalidDocument);
    }

    public async ValueTask AddPageAsync(PdfPageSize size, PdfRaster raster, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raster);
        await runtime.RunAsync(() =>
        {
            EnsureWritable();
            if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
                throw new RedactionException(RedactionError.InvalidDocument);
            long pagePixels = (long)raster.Width * raster.Height;
            if (size.Width > limits.MaximumPageDimensionPoints || size.Height > limits.MaximumPageDimensionPoints ||
                pages >= limits.MaximumPages || pagePixels > limits.MaximumPixelsPerPage || pixels > limits.MaximumTotalPixels - pagePixels)
                throw new RedactionException(RedactionError.LimitExceeded);
            failed = true;
            var copy = raster.Bgra.ToArray();
            var pin = GCHandle.Alloc(copy, GCHandleType.Pinned);
            try
            {
                var page = fpdf_edit.FPDFPageNew(document, pages, size.Width, size.Height);
                if (page is null) throw new RedactionException(RedactionError.ExtensionFailed);
                try
                {
                    var bitmap = fpdfview.FPDFBitmapCreateEx(raster.Width, raster.Height, 4, pin.AddrOfPinnedObject(), checked(raster.Width * 4));
                    if (bitmap is null) throw new RedactionException(RedactionError.ExtensionFailed);
                    try
                    {
                        var image = fpdf_edit.FPDFPageObjNewImageObj(document);
                        if (image is null) throw new RedactionException(RedactionError.ExtensionFailed);
                        bool inserted = false;
                        try
                        {
                            if (fpdf_edit.FPDFImageObjSetBitmap(page, 1, image, bitmap) != 1)
                                throw new RedactionException(RedactionError.ExtensionFailed);
                            fpdf_edit.FPDFPageObjTransform(image, size.Width, 0, 0, size.Height, 0, 0);
                            if (fpdf_edit.FPDFPageInsertObject(page, image) != 1)
                                throw new RedactionException(RedactionError.ExtensionFailed);
                            inserted = true;
                            if (fpdf_edit.FPDFPageGenerateContent(page) != 1)
                                throw new RedactionException(RedactionError.ExtensionFailed);
                        }
                        finally { if (!inserted) fpdf_edit.FPDFPageObjDestroy(image); }
                    }
                    finally { fpdfview.FPDFBitmapDestroy(bitmap); }
                }
                finally { fpdfview.FPDF_ClosePage(page); }
            }
            finally { pin.Free(); }
            cancellationToken.ThrowIfCancellationRequested();
            pages++;
            pixels += pagePixels;
            failed = false;
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ReadOnlyMemory<byte>> CompleteAsync(CancellationToken cancellationToken = default) => runtime.RunAsync<ReadOnlyMemory<byte>>(() =>
    {
        EnsureWritable();
        if (pages == 0) throw new RedactionException(RedactionError.InvalidDocument);
        completed = true;
        using var output = new MemoryStream();
        using var writer = new FPDF_FILEWRITE_ { Version = 1 };
        RedactionError? error = null;
        global::PDFiumCore.Delegates.Func_int___IntPtr___IntPtr_ulong callback = (_, data, count) =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested) return 0;
                if (count > (ulong)limits.MaximumOutputBytes || output.Length > limits.MaximumOutputBytes - (long)count)
                {
                    error = RedactionError.LimitExceeded;
                    return 0;
                }
                var block = new byte[(int)count];
                Marshal.Copy(data, block, 0, block.Length);
                output.Write(block);
                return 1;
            }
            catch { error = RedactionError.ExtensionFailed; return 0; }
        };
        writer.WriteBlock = callback;
        int saved = fpdf_save.FPDF_SaveAsCopy(document, writer, 2);
        GC.KeepAlive(callback);
        cancellationToken.ThrowIfCancellationRequested();
        if (error is not null) throw new RedactionException(error.Value);
        if (saved != 1) throw new RedactionException(RedactionError.ExtensionFailed);
        return output.ToArray();
    }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await runtime.CleanupAsync(() =>
        {
            if (disposed) return;
            fpdfview.FPDF_CloseDocument(document);
            disposed = true;
            runtime.RemoveDocument();
        }).ConfigureAwait(false);
    }
}
