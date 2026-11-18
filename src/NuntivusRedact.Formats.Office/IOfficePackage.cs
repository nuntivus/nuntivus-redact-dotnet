namespace NuntivusRedact.Formats.Office;

internal interface IOfficePackage
{
    ValueTask<DocumentRedactionResult> ProcessAsync(Stream input, Stream output, string format,
        IEditAwareTextRedactor redactor, OfficeLimits limits, CancellationToken token);
}
