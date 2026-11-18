using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Strategies;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace NuntivusRedact.Formats.Tests;

internal static class OfficeFixtures
{
    internal static RedactionEngine Engine(string replacement = "[REDACTED]") => new([new(PersonalDataDetectors.Email()), new(PersonalDataDetectors.CreditCard())], new RedactionPolicyBuilder(new FixedReplacementStrategy(replacement)).Build());

    internal static byte[] Word(bool extras = false)
    {
        using var buffer = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            doc.ChangeIdOfPart(main, "rIdMain");
            main.Document = new W.Document(new W.Body(new W.Paragraph(
                new W.Run(new W.RunProperties(new W.Bold()), new W.Text("Hello alice")),
                new W.Run(new W.RunProperties(new W.Italic()), new W.Text("@example.com")),
                new W.Run(new W.Text("!")))));
            if (extras)
            {
                var header = main.AddNewPart<HeaderPart>("rIdHeader");
                header.Header = new W.Header(new W.Paragraph(new W.Run(new W.Text("alice@example.com"))));
                var footer = main.AddNewPart<FooterPart>("rIdFooter");
                footer.Footer = new W.Footer(new W.Paragraph(new W.Run(new W.Text("alice@example.com"))));
                var comments = main.AddNewPart<WordprocessingCommentsPart>("rIdComments");
                comments.Comments = new W.Comments(new W.Comment(new W.Paragraph(new W.Run(new W.Text("alice@example.com")))) { Id = "0", Author = "alice@example.com", Initials = "a" });
                main.Document.Body!.Append(new W.Paragraph(new W.ParagraphProperties(new W.SectionProperties(
                    new W.HeaderReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(header) },
                    new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) }))));
                Properties(doc);
            }
        }
        return buffer.ToArray();
    }

    internal static byte[] Excel(string formula = "SUM(C1:C1)", string numeric = "42")
    {
        using var buffer = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook, true))
        {
            var book = doc.AddWorkbookPart();
            doc.ChangeIdOfPart(book, "rIdWorkbook");
            book.Workbook = new S.Workbook();
            var strings = book.AddNewPart<SharedStringTablePart>("rIdStrings");
            strings.SharedStringTable = new S.SharedStringTable(
                new S.SharedStringItem(new S.Run(new S.RunProperties(new S.Bold()), new S.Text("alice")), new S.Run(new S.RunProperties(new S.Italic()), new S.Text("@example.com"))),
                new S.SharedStringItem(new S.Text("alice@example.com")));
            var styles = book.AddNewPart<WorkbookStylesPart>("rIdStyles");
            styles.Stylesheet = new S.Stylesheet(new S.Fonts(new S.Font()),
                new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
                new S.Borders(new S.Border()), new S.CellStyleFormats(new S.CellFormat()),
                new S.CellFormats(new S.CellFormat(), new S.CellFormat { NumberFormatId = 14U, ApplyNumberFormat = true }));
            var sheet = book.AddNewPart<WorksheetPart>("rIdVisible");
            sheet.Worksheet = new S.Worksheet(new S.Columns(new S.Column { Min = 1U, Max = 2U, Width = 25, CustomWidth = true }),
                new S.SheetData(new S.Row(
                    new S.Cell(new S.CellValue("0")) { CellReference = "A1", DataType = S.CellValues.SharedString },
                    new S.Cell(new S.InlineString(new S.Text("alice@example.com"))) { CellReference = "B1", DataType = S.CellValues.InlineString },
                    new S.Cell(new S.CellValue(numeric)) { CellReference = "C1", DataType = S.CellValues.Number },
                    new S.Cell(new S.CellFormula(formula), new S.CellValue("alice@example.com")) { CellReference = "D1", DataType = S.CellValues.String },
                    new S.Cell(new S.CellValue("45000")) { CellReference = "E1", StyleIndex = 1U })
                { RowIndex = 1U }),
                new S.MergeCells(new S.MergeCell { Reference = "A3:B3" }));
            var notes = sheet.AddNewPart<WorksheetCommentsPart>("rIdNotes");
            notes.Comments = new S.Comments(new S.Authors(new S.Author("alice@example.com")),
                new S.CommentList(new S.Comment(new S.CommentText(new S.Text("alice@example.com"))) { Reference = "A1", AuthorId = 0U }));
            var hidden = book.AddNewPart<WorksheetPart>("rIdHidden");
            hidden.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell(new S.InlineString(new S.Text("alice@example.com"))) { CellReference = "A1", DataType = S.CellValues.InlineString }) { RowIndex = 1U }));
            book.Workbook.AppendChild(new S.Sheets(
                new S.Sheet { Id = book.GetIdOfPart(sheet), SheetId = 1U, Name = "Visible" },
                new S.Sheet { Id = book.GetIdOfPart(hidden), SheetId = 2U, Name = "Hidden", State = S.SheetStateValues.VeryHidden }));
            var chain = book.AddNewPart<CalculationChainPart>("rIdChain");
            chain.CalculationChain = new S.CalculationChain(new S.CalculationCell { CellReference = "D1", SheetId = 1 });
            Properties(doc);
        }
        return buffer.ToArray();
    }

    internal static byte[] Embed(byte[] outer, byte[] inner, bool excel = false)
    {
        using var buffer = new MemoryStream();
        buffer.Write(outer);
        buffer.Position = 0;
        using (var doc = WordprocessingDocument.Open(buffer, true))
        {
            var embedded = doc.MainDocumentPart!.AddEmbeddedPackagePart(excel
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "rIdEmbedded");
            using var source = new MemoryStream(inner, false);
            embedded.FeedData(source);
        }
        return buffer.ToArray();
    }

    internal static Dictionary<string, byte[]> Entries(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var source = e.Open();
            using var target = new MemoryStream();
            source.CopyTo(target);
            return target.ToArray();
        }, StringComparer.Ordinal);
    }

    internal static byte[] Rewrite(byte[] source, Action<Dictionary<string, byte[]>> change)
    {
        var entries = Entries(source);
        change(entries);
        return Zip(entries);
    }

    internal static byte[] Zip(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in entries)
            {
                using var output = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                output.Write(bytes);
            }
        return result.ToArray();
    }

    internal static void EditXml(Dictionary<string, byte[]> entries, string name, Action<XDocument> edit)
    {
        using var buffer = new MemoryStream(entries[name], false);
        var xml = XDocument.Load(buffer);
        edit(xml);
        entries[name] = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
    }

    private static void Properties(OpenXmlPackage package)
    {
        var part = package.AddNewPart<CoreFilePropertiesPart>("rIdProperties");
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        new XDocument(new XElement(cp + "coreProperties", new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XElement(dc + "creator", "alice@example.com"), new XElement(dc + "description", "alice@example.com"))).Save(stream);
    }
}
