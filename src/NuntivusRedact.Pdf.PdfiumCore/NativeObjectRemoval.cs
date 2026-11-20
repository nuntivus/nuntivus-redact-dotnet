using System.Runtime.InteropServices;
using PDFiumCore;

namespace NuntivusRedact.Pdf.PdfiumCore;

internal sealed class NativeObjectRemoval
{
    internal ReadOnlyMemory<byte> Execute(ReadOnlyMemory<byte> input, IReadOnlyList<PdfObjectRemovalRegion> regions,
        PdfBackendLimits limits, CancellationToken cancellationToken)
    {
        var pin = GCHandle.Alloc(input.ToArray(), GCHandleType.Pinned);
        FpdfDocumentT? source = null, destination = null;
        try
        {
            source = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)input.Length, null!);
            if (source is null) throw new RedactionException(RedactionError.InvalidDocument);
            int pages = fpdfview.FPDF_GetPageCount(source);
            if (pages < 1 || pages > limits.MaximumPages) throw new RedactionException(RedactionError.LimitExceeded);
            if (regions.Any(region => region.PageIndex < 0 || region.PageIndex >= pages)) throw new ArgumentException("Invalid native region page.");
            destination = fpdf_edit.FPDF_CreateNewDocument();
            if (destination is null) throw new RedactionException(RedactionError.ExtensionFailed);
            for (int index = 0; index < pages; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = fpdfview.FPDF_LoadPage(source, index);
                if (page is null) throw new RedactionException(RedactionError.InvalidDocument);
                try
                {
                    double width = fpdfview.FPDF_GetPageWidth(page), height = fpdfview.FPDF_GetPageHeight(page);
                    if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
                        width > limits.MaximumPageDimensionPoints || height > limits.MaximumPageDimensionPoints ||
                        width * 1000 > int.MaxValue || height * 1000 > int.MaxValue)
                        throw new RedactionException(RedactionError.LimitExceeded);
                    if (fpdf_annot.FPDFPageGetAnnotCount(page) != 0) throw new RedactionException(RedactionError.UnsupportedFormat);
                    var selected = regions.Where(region => region.PageIndex == index).ToArray();
                    foreach (var region in selected)
                        if (!double.IsFinite(region.Left) || !double.IsFinite(region.Top) || !double.IsFinite(region.Right) || !double.IsFinite(region.Bottom) ||
                            region.Left < 0 || region.Top < 0 || region.Right > width || region.Bottom > height || region.Right <= region.Left || region.Bottom <= region.Top)
                            throw new RedactionException(RedactionError.InvalidDocument);
                    int count = fpdf_edit.FPDFPageCountObjects(page);
                    if (count < 0 || count > limits.MaximumCharactersPerPage) throw new RedactionException(RedactionError.LimitExceeded);
                    for (int objectIndex = count - 1; objectIndex >= 0; objectIndex--)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var item = fpdf_edit.FPDFPageGetObject(page, objectIndex);
                        int type = fpdf_edit.FPDFPageObjGetType(item);
                        if (type is not (1 or 2 or 3)) throw new RedactionException(RedactionError.UnsupportedFormat);
                        float left = 0, bottom = 0, right = 0, top = 0;
                        if (fpdf_edit.FPDFPageObjGetBounds(item, ref left, ref bottom, ref right, ref top) != 1)
                            throw new RedactionException(RedactionError.InvalidDocument);
                        if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(top) || !float.IsFinite(bottom))
                            throw new RedactionException(RedactionError.InvalidDocument);
                        var bounds = DisplayBounds(page, left, bottom, right, top, width, height);
                        if (!selected.Any(region => region.Left < bounds.Right && region.Right > bounds.Left && region.Top < bounds.Bottom && region.Bottom > bounds.Top)) continue;
                        if (type == 2) throw new RedactionException(RedactionError.UnsupportedFormat);
                        if (fpdf_edit.FPDFPageRemoveObject(page, item) != 1) throw new RedactionException(RedactionError.ExtensionFailed);
                        fpdf_edit.FPDFPageObjDestroy(item);
                    }
                    if (fpdf_edit.FPDFPageGenerateContent(page) != 1) throw new RedactionException(RedactionError.ExtensionFailed);
                }
                finally { fpdfview.FPDF_ClosePage(page); }
                if (fpdf_ppo.FPDF_ImportPages(destination, source, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), index) != 1)
                    throw new RedactionException(RedactionError.ExtensionFailed);
            }
            return Save(destination, limits, cancellationToken);
        }
        finally
        {
            if (destination is not null) fpdfview.FPDF_CloseDocument(destination);
            if (source is not null) fpdfview.FPDF_CloseDocument(source);
            pin.Free();
        }
    }

    private PdfObjectRemovalRegion DisplayBounds(FpdfPageT page, float left, float bottom, float right, float top, double width, double height)
    {
        var points = new List<PdfPoint>();
        foreach (var point in new[] { (X: left, Y: bottom), (X: left, Y: top), (X: right, Y: bottom), (X: right, Y: top) })
        {
            int x = 0, y = 0;
            if (fpdfview.FPDF_PageToDevice(page, 0, 0, (int)Math.Ceiling(width * 1000), (int)Math.Ceiling(height * 1000), 0,
                point.X, point.Y, ref x, ref y) != 1) throw new RedactionException(RedactionError.InvalidDocument);
            points.Add(new PdfPoint(x / 1000d, y / 1000d));
        }
        return new(0, points.Min(point => point.X), points.Min(point => point.Y), points.Max(point => point.X), points.Max(point => point.Y));
    }

    private ReadOnlyMemory<byte> Save(FpdfDocumentT document, PdfBackendLimits limits, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        using var writer = new FPDF_FILEWRITE_ { Version = 1 };
        RedactionError? error = null;
        global::PDFiumCore.Delegates.Func_int___IntPtr___IntPtr_ulong callback = (_, data, count) =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested) return 0;
                if (count > (ulong)limits.MaximumOutputBytes || output.Length > limits.MaximumOutputBytes - (long)count)
                { error = RedactionError.LimitExceeded; return 0; }
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
    }
}
