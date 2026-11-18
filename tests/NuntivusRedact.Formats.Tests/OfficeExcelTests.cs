using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NuntivusRedact.Formats.Office;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace NuntivusRedact.Formats.Tests;

public sealed class OfficeExcelTests
{
    [Fact]
    public async Task RichStringsHiddenSheetsNotesAndCachesCannotRecoverMatchedText()
    {
        using var input = new MemoryStream(OfficeFixtures.Excel());
        using var output = new MemoryStream();
        var report = await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output);
        Assert.True(report.Detections.Count >= 8);
        Assert.All(OfficeFixtures.Entries(output.ToArray()).Values, bytes => Assert.DoesNotContain("alice", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase));
        output.Position = 0;
        using var doc = SpreadsheetDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var book = doc.WorkbookPart!;
        var runs = book.SharedStringTablePart!.SharedStringTable!.Descendants<S.Run>().ToArray();
        Assert.Equal("[REDACTED]", runs[0].InnerText);
        Assert.NotNull(runs[0].RunProperties!.GetFirstChild<S.Bold>());
        Assert.Equal("", runs[1].InnerText);
        Assert.NotNull(runs[1].RunProperties!.GetFirstChild<S.Italic>());
        Assert.Equal(S.SheetStateValues.VeryHidden, book.Workbook!.Descendants<S.Sheet>().Last().State!.Value);
        var sheet = book.WorksheetParts.Single(p => p.Worksheet!.Descendants<S.Cell>().Any(c => c.CellReference == "D1"));
        var cells = sheet.Worksheet!.Descendants<S.Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("42", cells["C1"].CellValue!.Text);
        Assert.Equal("45000", cells["E1"].CellValue!.Text);
        Assert.Equal(1U, cells["E1"].StyleIndex!.Value);
        Assert.Equal("SUM(C1:C1)", cells["D1"].CellFormula!.Text);
        Assert.Null(cells["D1"].CellValue);
        Assert.Null(book.CalculationChainPart);
        Assert.True(book.Workbook.CalculationProperties!.FullCalculationOnLoad!.Value);
        Assert.NotEmpty(sheet.Worksheet.Descendants<S.MergeCell>());
        Assert.Equal(25, sheet.Worksheet.Descendants<S.Column>().First().Width!.Value);
        Assert.All(report.Detections, d => Assert.Matches(@"^/packages/0/parts/\d+/fields/\d+$", d.Location));
    }

    [Theory]
    [InlineData("A1+2*($C$1-1)")]
    [InlineData("AVERAGE(C1:C1)")]
    [InlineData("MAX(1, MIN(2, 3))")]
    [InlineData("COUNT(A1:B1)")]
    public async Task SupportedFormulasArePreservedWithoutCachedValues(string formula)
    {
        using var input = new MemoryStream(OfficeFixtures.Excel(formula));
        using var output = new MemoryStream();
        await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output);
        output.Position = 0;
        using var doc = SpreadsheetDocument.Open(output, false);
        var cell = doc.WorkbookPart!.WorksheetParts.SelectMany(p => p.Worksheet!.Descendants<S.Cell>()).Single(c => c.CellFormula is not null);
        Assert.Equal(formula, cell.CellFormula!.Text);
        Assert.Null(cell.CellValue);
    }

    [Theory]
    [InlineData("HYPERLINK(\"https://example.com\",\"alice@example.com\")")]
    [InlineData("\"alice@example.com\"")]
    [InlineData("Sheet1!A1")]
    [InlineData("[Other.xlsx]Sheet1!A1")]
    [InlineData("INDIRECT(A1)")]
    [InlineData("A0+1")]
    [InlineData("XFE1+1")]
    [InlineData("SUM(A1")]
    [InlineData("A1 A2")]
    public async Task UnsupportedOrMalformedFormulasFailClosed(string formula)
    {
        using var input = new MemoryStream(OfficeFixtures.Excel(formula));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<RedactionException>(async () => await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task CustomNumberFormatsWithFragmentedDisplayTextFailClosed()
    {
        using var input = new MemoryStream();
        input.Write(OfficeFixtures.Excel());
        input.Position = 0;
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var styles = document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;
            styles.PrependChild(new S.NumberingFormats(new S.NumberingFormat
            {
                NumberFormatId = 164U,
                FormatCode = "\"alice\"\"@\"\"example.com\""
            })
            { Count = 1U });
            styles.CellFormats!.Elements<S.CellFormat>().First().NumberFormatId = 164U;
        }
        input.Position = 0;
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task SensitiveNumericCellIsRejectedRatherThanChangingCellType()
    {
        using var input = new MemoryStream(OfficeFixtures.Excel(numeric: "4111111111111111"));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new XlsxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Empty(output.ToArray());
    }
}
