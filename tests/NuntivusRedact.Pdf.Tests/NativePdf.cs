using System.Runtime.InteropServices;
using System.Text;
using PDFiumCore;
using Xunit;

namespace NuntivusRedact.Pdf.Tests;

[CollectionDefinition("PDFium feasibility", DisableParallelization = true)]
public sealed class PdfiumCollection : ICollectionFixture<PdfiumRuntime>;

public sealed class PdfiumRuntime : IDisposable
{
    public NuntivusRedact.Pdf.PdfiumCore.PdfiumRuntime Runtime { get; } = new();
    public void Dispose() => Runtime.Dispose();
}

internal sealed class NativePdf : IDisposable
{
    private GCHandle input;
    internal FpdfDocumentT Document { get; }
    internal FpdfPageT Page { get; }

    internal NativePdf(byte[] bytes)
    {
        input = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var document = fpdfview.FPDF_LoadMemDocument64(input.AddrOfPinnedObject(), (ulong)bytes.Length, null!);
        if (document is null)
        {
            input.Free();
            throw new InvalidDataException("Synthetic PDF did not load.");
        }
        Document = document;
        var page = fpdfview.FPDF_LoadPage(document, 0);
        if (page is null)
        {
            fpdfview.FPDF_CloseDocument(document);
            input.Free();
            throw new InvalidDataException("Synthetic page did not load.");
        }
        Page = page;
    }

    internal string ReadText()
    {
        var textPage = fpdf_text.FPDFTextLoadPage(Page);
        try
        {
            var text = new StringBuilder();
            int count = fpdf_text.FPDFTextCountChars(textPage);
            Assert.InRange(count, 0, 10_000);
            for (int i = 0; i < count; i++)
            {
                uint scalar = fpdf_text.FPDFTextGetUnicode(textPage, i);
                if (scalar != 0) text.Append(char.ConvertFromUtf32((int)scalar));
            }
            return text.ToString();
        }
        finally { fpdf_text.FPDFTextClosePage(textPage); }
    }

    internal byte[] Render(int width = 200, int height = 200)
    {
        var bitmap = fpdfview.FPDFBitmapCreate(width, height, 1);
        Assert.NotNull(bitmap);
        try
        {
            Assert.Equal(1, fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF));
            fpdfview.FPDF_RenderPageBitmap(bitmap, Page, 0, 0, width, height, 0, 0);
            int stride = fpdfview.FPDFBitmapGetStride(bitmap);
            Assert.Equal(width * 4, stride);
            var pixels = new byte[checked(stride * height)];
            Marshal.Copy(fpdfview.FPDFBitmapGetBuffer(bitmap), pixels, 0, pixels.Length);
            return pixels;
        }
        finally { fpdfview.FPDFBitmapDestroy(bitmap); }
    }

    internal void RemoveObjects(int type)
    {
        for (int i = fpdf_edit.FPDFPageCountObjects(Page) - 1; i >= 0; i--)
        {
            var item = fpdf_edit.FPDFPageGetObject(Page, i);
            if (fpdf_edit.FPDFPageObjGetType(item) != type) continue;
            Assert.Equal(1, fpdf_edit.FPDFPageRemoveObject(Page, item));
            fpdf_edit.FPDFPageObjDestroy(item);
        }
        Assert.Equal(1, fpdf_edit.FPDFPageGenerateContent(Page));
    }

    internal byte[] Save() => Save(Document);

    internal static byte[] Save(FpdfDocumentT document)
    {
        using var output = new MemoryStream();
        bool failed = false;
        using var writer = new FPDF_FILEWRITE_ { Version = 1 };
        global::PDFiumCore.Delegates.Func_int___IntPtr___IntPtr_ulong callback = (_, data, count) =>
        {
            try
            {
                if (count > 1_000_000 || output.Length + (long)count > 1_000_000) { failed = true; return 0; }
                var bytes = new byte[(int)count];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                output.Write(bytes);
                return 1;
            }
            catch { failed = true; return 0; }
        };
        writer.WriteBlock = callback;
        int saved = fpdf_save.FPDF_SaveAsCopy(document, writer, 2);
        GC.KeepAlive(callback);
        Assert.False(failed);
        Assert.Equal(1, saved);
        return output.ToArray();
    }

    internal static byte[] CreateImageOnly(byte[] bgra, int width = 200, int height = 200)
    {
        var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        var document = fpdf_edit.FPDF_CreateNewDocument();
        try
        {
            var page = fpdf_edit.FPDFPageNew(document, 0, 200, 200);
            try
            {
                var bitmap = fpdfview.FPDFBitmapCreateEx(width, height, 4, pin.AddrOfPinnedObject(), width * 4);
                try
                {
                    var image = fpdf_edit.FPDFPageObjNewImageObj(document);
                    Assert.NotNull(image);
                    Assert.Equal(1, fpdf_edit.FPDFImageObjSetBitmap(page, 1, image, bitmap));
                    fpdf_edit.FPDFPageObjTransform(image, 200, 0, 0, 200, 0, 0);
                    Assert.Equal(1, fpdf_edit.FPDFPageInsertObject(page, image));
                    Assert.Equal(1, fpdf_edit.FPDFPageGenerateContent(page));
                    return Save(document);
                }
                finally { fpdfview.FPDFBitmapDestroy(bitmap); }
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
        finally { fpdfview.FPDF_CloseDocument(document); pin.Free(); }
    }

    public void Dispose()
    {
        fpdfview.FPDF_ClosePage(Page);
        fpdfview.FPDF_CloseDocument(Document);
        input.Free();
    }
}
