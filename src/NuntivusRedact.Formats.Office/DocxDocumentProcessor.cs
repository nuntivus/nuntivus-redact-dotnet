using NuntivusRedact.Ocr;

namespace NuntivusRedact.Formats.Office;

/// <summary>Redacts the explicitly supported DOCX profile while preserving unaffected runs and document structure.</summary>
public sealed class DocxDocumentProcessor : IDocumentProcessor
{
    private readonly IEditAwareTextRedactor redactor;
    private readonly OfficeLimits limits;
    private readonly IOfficePackage package;

    /// <summary>Creates a reusable processor. Unsupported parts, revisions and external content fail closed. See the Office support matrix.</summary>
    public DocxDocumentProcessor(IEditAwareTextRedactor redactor, OfficeLimits? limits = null)
        : this(redactor, limits, null) { }

    /// <summary>Opts into restricted embedded PNG/JPEG pictures using an explicitly supplied image redactor. The caller owns its lifetime. Null keeps images unsupported.</summary>
    public DocxDocumentProcessor(IEditAwareTextRedactor redactor, OfficeLimits? limits, IImageRedactor? imageRedactor)
    {
        this.redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        this.limits = limits ?? new OfficeLimits();
        this.limits.Validate();
        IOfficeString strings = new OfficeString();
        IExcelContent excel = new ExcelContent(strings);
        IOfficeDrawing drawings = new OfficeDrawing(strings);
        IOfficeContent content = new OfficeContent(strings, excel, drawings);
        package = new OfficePackage(content, strings, imageRedactor);
    }
    /// <inheritdoc />
    public string Format => "docx";
    /// <inheritdoc />
    public ValueTask<DocumentRedactionResult> ProcessAsync(Stream input, Stream output, CancellationToken cancellationToken = default) =>
        package.ProcessAsync(input, output, Format, redactor, limits, cancellationToken);
}
