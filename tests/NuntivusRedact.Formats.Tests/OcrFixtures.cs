using System.Text;
using DocumentFormat.OpenXml.Packaging;
using NuntivusRedact.Ocr;
using SkiaSharp;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace NuntivusRedact.Formats.Tests;

internal sealed class StubOcrProvider(Func<int, int, OcrResult>? recognize = null) : IOcrProvider
{
    internal int Calls { get; private set; }
    public ValueTask<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> png, int width, int height, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return ValueTask.FromResult(recognize is null
            ? new OcrResult("alice@example.com safe", [new(new DetectionSpan(0, 17), new(10, 10, 90, 40), .95), new(new DetectionSpan(18, 4), new(110, 10, 150, 40), .9)])
            : recognize(width, height));
    }
}

internal static class OcrFixtures
{
    internal static byte[] Image(bool jpeg = false, bool transparent = false)
    {
        using var bitmap = new SKBitmap(160, 60);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? new SKColor(255, 0, 0, 0) : SKColors.White);
        using var paint = new SKPaint { Color = transparent ? new SKColor(255, 0, 0, 0) : SKColors.Red, BlendMode = SKBlendMode.Src };
        canvas.DrawRect(10, 10, 80, 30, paint);
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static byte[] Office(bool excel, byte[] image, bool jpeg = false)
    {
        using var buffer = new MemoryStream();
        buffer.Write(excel ? OfficeFixtures.Excel() : OfficeFixtures.Word());
        buffer.Position = 0;
        if (excel)
        {
            using var document = SpreadsheetDocument.Open(buffer, true);
            var sheet = document.WorkbookPart!.WorksheetParts.First(p => p.Worksheet!.Descendants<S.Cell>().Any(c => c.CellReference == "D1"));
            var drawing = sheet.AddNewPart<DrawingsPart>("rIdDrawing");
            var picture = drawing.AddImagePart(jpeg ? ImagePartType.Jpeg : ImagePartType.Png, "rIdImage");
            using (var source = new MemoryStream(image)) picture.FeedData(source);
            drawing.WorksheetDrawing = new Xdr.WorksheetDrawing(SpreadsheetDrawing);
            sheet.Worksheet!.Append(new S.Drawing { Id = "rIdDrawing" });
        }
        else
        {
            using var document = WordprocessingDocument.Open(buffer, true);
            var main = document.MainDocumentPart!;
            var picture = main.AddImagePart(jpeg ? ImagePartType.Jpeg : ImagePartType.Png, "rIdImage");
            using (var source = new MemoryStream(image)) picture.FeedData(source);
            main.Document!.Body!.Append(new W.Paragraph(new W.Run(new W.Drawing(WordDrawing))));
        }
        return buffer.ToArray();
    }

    internal static byte[] AddPngMetadata(byte[] png)
    {
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, 33));
        byte[] data = Encoding.ASCII.GetBytes("Comment\0alice@example.com");
        byte[] type = "tEXt"u8.ToArray();
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in type.Concat(data))
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        output.Write(length);
        output.Write(png.AsSpan(33));
        return output.ToArray();
    }

    private const string WordDrawing = """
        <w:drawing xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <wp:inline><wp:extent cx="1524000" cy="571500"/><wp:docPr id="1" name="alice@example.com" descr="alice@example.com"/>
            <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic>
              <pic:nvPicPr><pic:cNvPr id="0" name="Picture"/><pic:cNvPicPr/></pic:nvPicPr>
              <pic:blipFill><a:blip r:embed="rIdImage"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
              <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1524000" cy="571500"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
            </pic:pic></a:graphicData></a:graphic>
          </wp:inline>
        </w:drawing>
        """;

    private const string SpreadsheetDrawing = """
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx="1524000" cy="571500"/>
            <xdr:pic><xdr:nvPicPr><xdr:cNvPr id="1" name="alice@example.com" descr="alice@example.com"/><xdr:cNvPicPr/></xdr:nvPicPr>
              <xdr:blipFill><a:blip r:embed="rIdImage"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>
              <xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1524000" cy="571500"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr>
            </xdr:pic><xdr:clientData/>
          </xdr:oneCellAnchor>
        </xdr:wsDr>
        """;
}
