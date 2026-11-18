using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using NuntivusRedact;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.Regex;
using NuntivusRedact.Extensions.DependencyInjection;
using NuntivusRedact.Formats.Office;
using NuntivusRedact.Strategies;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

var services = new ServiceCollection();
services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build()).AddDetector<ProjectKeywordDetector>()
    .AddDocumentProcessor<DocxDocumentProcessor>().AddDocumentProcessor<XlsxDocumentProcessor>();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
var result = await provider.GetRequiredService<ITextRedactor>().RedactAsync("Project codename is ORCHID.");
if (result.Text != "Project codename is [REDACTED].") throw new InvalidOperationException("Sample verification failed.");
Console.WriteLine($"Processed synthetic text; replacements: {result.Detections.Count}.");

foreach (var processor in provider.GetServices<IDocumentProcessor>())
{
    using var input = new MemoryStream(CreateOffice(processor.Format));
    using var output = new MemoryStream();
    var report = await processor.ProcessAsync(input, output);
    if (report.Detections.Count != 1) throw new InvalidOperationException("Office sample verification failed.");
    Console.WriteLine($"Processed synthetic {processor.Format}; replacements: {report.Detections.Count}.");
}

static byte[] CreateOffice(string format)
{
    using var buffer = new MemoryStream();
    if (format == "docx")
    {
        using var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true);
        document.AddMainDocumentPart().Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("ORCHID")))));
    }
    else
    {
        using var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook, true);
        var book = document.AddWorkbookPart();
        var sheet = book.AddNewPart<WorksheetPart>();
        sheet.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell(new S.InlineString(new S.Text("ORCHID"))) { CellReference = "A1", DataType = S.CellValues.InlineString }) { RowIndex = 1U }));
        book.Workbook = new S.Workbook(new S.Sheets(new S.Sheet { Id = book.GetIdOfPart(sheet), SheetId = 1U, Name = "Sheet1" }));
    }
    return buffer.ToArray();
}

internal sealed class ProjectKeywordDetector : ISensitiveDataDetector
{
    private readonly DictionaryDetector detector = new("project-code", "project", ["ORCHID"]);
    public string Id => detector.Id;
    public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default) => detector.DetectAsync(context, cancellationToken);
}
