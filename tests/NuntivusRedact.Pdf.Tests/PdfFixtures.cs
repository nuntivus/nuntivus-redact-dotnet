using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace NuntivusRedact.Pdf.Tests;

internal static class PdfFixtures
{
    internal const string Secret = "secret@example.test";
    internal const string HiddenSecret = "hidden@example.test";

    internal static byte[] Create(bool hiddenContent = false, int rotation = 0)
    {
        const string image = "FF000000FF000000FFFFFFFF>";
        var content = $"BT /F1 12 Tf 20 140 Td ({Secret}) Tj ET\nq 20 0 0 20 20 20 cm /Im1 Do Q\n";
        string[] objects = [
            $"<< /Type /Catalog /Pages 2 0 R{(hiddenContent ? " /Metadata 7 0 R" : "")} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Rotate {rotation} /Resources << /Font << /F1 5 0 R >> /XObject << /Im1 6 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length {image.Length} >>\nstream\n{image}\nendstream",
            $"<< /Type /Metadata /Subtype /XML /Length {HiddenSecret.Length} >>\nstream\n{HiddenSecret}\nendstream",
            $"<< /HistoricalValue ({HiddenSecret}) >>"
        ];
        if (!hiddenContent) objects = objects[..6];
        return Build(objects);
    }

    internal static byte[] Build(string[] objects, string trailerEntries = "")
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        foreach (var body in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets) pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R {trailerEntries} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    internal static string Recover(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var recovered = new StringBuilder(raw);
        var matches = Regex.Matches(raw, @"(?s)<<(.*?)>>\s*stream\r?\n(.*?)\r?\n?endstream", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (Match match in matches)
        {
            if (!match.Groups[1].Value.Contains("/FlateDecode", StringComparison.Ordinal)) continue;
            using var input = new MemoryStream(Encoding.Latin1.GetBytes(match.Groups[2].Value));
            using var decoder = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = decoder.Read(buffer)) != 0)
            {
                if (output.Length + count > 1_000_000) throw new InvalidDataException("Fixture recovery limit exceeded.");
                output.Write(buffer, 0, count);
            }
            recovered.Append(Encoding.Latin1.GetString(output.ToArray()));
        }
        return recovered.ToString();
    }
}
