using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace NuntivusRedact.Pdf.Tests;

internal static class StructuralPdfFixtures
{
    internal static byte[] Create(string catalogEntries = "", string pageEntries = "", string[]? additionalObjects = null, string trailerEntries = "")
    {
        string[] objects = [
            $"<< /Type /Catalog /Pages 2 0 R {catalogEntries} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R {pageEntries} >>",
            "<< /Length 0 >>\nstream\n\nendstream"
        ];
        return PdfFixtures.Build([.. objects, .. additionalObjects ?? []], trailerEntries);
    }

    internal static byte[] Metadata(bool compressed = false)
    {
        byte[] payload = Encoding.ASCII.GetBytes("private synthetic metadata /JavaScript");
        if (compressed)
        {
            using var encoded = new MemoryStream();
            using (var compressor = new ZLibStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
                compressor.Write(new byte[1_000_000]);
            payload = encoded.ToArray();
        }
        var metadata = $"<< /Type /Metadata /Subtype /XML /Length {payload.Length} {(compressed ? "/Filter /FlateDecode" : "")} >>\nstream\n";
        var first = Create(catalogEntries: "/Metadata 5 0 R", additionalObjects: [metadata + new string('x', payload.Length) + "\nendstream"]);
        var text = Encoding.Latin1.GetString(first);
        int start = text.IndexOf(new string('x', payload.Length), StringComparison.Ordinal);
        payload.CopyTo(first.AsSpan(start, payload.Length));
        return first;
    }

    internal static byte[] Incremental()
    {
        var original = Create();
        string raw = Encoding.ASCII.GetString(original);
        int start = raw.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        long previous = long.Parse(raw.AsSpan(start, raw.IndexOf('\n', start) - start), CultureInfo.InvariantCulture);
        string info = "5 0 obj\n<< /Author (private synthetic author) >>\nendobj\n";
        long xref = original.Length + info.Length;
        string addition = info + $"xref\n5 1\n{original.Length:D10} 00000 n \ntrailer\n<< /Size 6 /Root 1 0 R /Info 5 0 R /Prev {previous} >>\nstartxref\n{xref}\n%%EOF\n";
        return [.. original, .. Encoding.ASCII.GetBytes(addition)];
    }

    internal static byte[] CompressedXref(string entries = "", bool truncate = false, bool corrupt = false, bool bomb = false,
        bool objectStream = false, int predictor = 1, bool invalidTag = false, bool flateObjectStream = false,
        string objectHeader = "1 0 ", int objectCount = 1, int? objectFirst = null, int objectIndex = 0)
    {
        string[] bodies = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream"
        ];
        if (objectStream || flateObjectStream)
        {
            string payload = objectHeader + "<< /Type /Catalog /Pages 2 0 R >>";
            byte[] objectData = Encoding.ASCII.GetBytes(payload);
            if (flateObjectStream)
            {
                using var compressed = new MemoryStream();
                using (var compressor = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) compressor.Write(objectData);
                objectData = compressed.ToArray();
            }
            bodies[2] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>";
            bodies[3] = $"<< /Type /ObjStm /N {objectCount} /First {objectFirst ?? objectHeader.Length} /Length {objectData.Length} {(flateObjectStream ? "/Filter /FlateDecode" : "")} >>\nstream\n{Encoding.Latin1.GetString(objectData)}\nendstream";
        }
        using var pdf = new MemoryStream();
        pdf.Write("%PDF-1.7\n"u8);
        var offsets = new List<int> { 0 };
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Add((int)pdf.Length);
            pdf.Write(Encoding.Latin1.GetBytes($"{i + 1} 0 obj\n{bodies[i]}\nendobj\n"));
        }
        offsets.Add((int)pdf.Length);
        var rows = new byte[6 * 7];
        for (int i = 0; i < 6; i++)
        {
            rows[i * 7] = i == 0 ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteInt32BigEndian(rows.AsSpan(i * 7 + 1), offsets[i]);
            BinaryPrimitives.WriteUInt16BigEndian(rows.AsSpan(i * 7 + 5), i == 0 ? ushort.MaxValue : (ushort)0);
        }
        if (objectStream || flateObjectStream)
        {
            rows[7] = 2;
            BinaryPrimitives.WriteInt32BigEndian(rows.AsSpan(8), 4);
            BinaryPrimitives.WriteUInt16BigEndian(rows.AsSpan(12), (ushort)objectIndex);
        }
        if (predictor != 1)
        {
            var predicted = new byte[6 * 8];
            for (int row = 0; row < 6; row++)
            {
                int filter = predictor == 15 ? row % 5 : predictor - 10;
                predicted[row * 8] = (byte)(invalidTag ? 5 : filter);
                for (int column = 0; column < 7; column++)
                {
                    int left = column == 0 ? 0 : rows[row * 7 + column - 1];
                    int above = row == 0 ? 0 : rows[(row - 1) * 7 + column];
                    int corner = row == 0 || column == 0 ? 0 : rows[(row - 1) * 7 + column - 1];
                    int p = left + above - corner;
                    int a = Math.Abs(p - left), b = Math.Abs(p - above), c = Math.Abs(p - corner);
                    int correction = filter switch
                    {
                        0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2,
                        4 => a <= b && a <= c ? left : b <= c ? above : corner,
                        _ => 0
                    };
                    predicted[row * 8 + column + 1] = unchecked((byte)(rows[row * 7 + column] - correction));
                }
            }
            rows = predicted;
            if (entries.Length == 0) entries = $"/DecodeParms << /Predictor {predictor} /Columns 7 >>";
        }
        using var encoded = new MemoryStream();
        using (var compressor = new ZLibStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
            compressor.Write(bomb ? new byte[1_000_000] : truncate ? rows.AsSpan(0, rows.Length - 1) : rows);
        var data = encoded.ToArray();
        if (corrupt) data[0] = 0;
        pdf.Write(Encoding.ASCII.GetBytes($"5 0 obj\n<< /Type /XRef /Size 6 /Root 1 0 R /W [1 4 2] /Filter /FlateDecode /Length {data.Length} {entries} >>\nstream\n"));
        pdf.Write(data);
        pdf.Write(Encoding.ASCII.GetBytes($"\nendstream\nendobj\nstartxref\n{offsets[5]}\n%%EOF\n"));
        return pdf.ToArray();
    }
}
