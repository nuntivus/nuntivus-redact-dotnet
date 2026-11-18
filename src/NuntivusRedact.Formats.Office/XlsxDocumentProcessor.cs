namespace NuntivusRedact.Formats.Office;

/// <summary>Redacts the explicitly supported XLSX profile, preserving unaffected rich text, styles and workbook structure.</summary>
public sealed class XlsxDocumentProcessor : IDocumentProcessor
{
    private readonly IEditAwareTextRedactor redactor;
    private readonly OfficeLimits limits;
    private readonly IOfficePackage package;

    /// <summary>Creates a reusable processor. Unsupported formulas, caches, drawings and external content fail closed. Formulas are not evaluated.</summary>
    public XlsxDocumentProcessor(IEditAwareTextRedactor redactor, OfficeLimits? limits = null)
    {
        this.redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        this.limits = limits ?? new OfficeLimits();
        this.limits.Validate();
        IOfficeString strings = new OfficeString();
        IExcelContent excel = new ExcelContent(strings);
        IOfficeContent content = new OfficeContent(strings, excel);
        package = new OfficePackage(content, strings);
    }
    /// <inheritdoc />
    public string Format => "xlsx";
    /// <inheritdoc />
    public ValueTask<DocumentRedactionResult> ProcessAsync(Stream input, Stream output, CancellationToken cancellationToken = default) =>
        package.ProcessAsync(input, output, Format, redactor, limits, cancellationToken);
}
