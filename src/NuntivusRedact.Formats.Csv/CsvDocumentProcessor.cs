using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using NuntivusRedact.Formats.Text;

namespace NuntivusRedact.Formats.Csv;

/// <summary>Strict comma-separated UTF-8 CSV with quoted multiline fields, no dialect guessing.</summary>
public sealed class CsvDocumentProcessor : BufferedDocumentProcessor
{
    /// <summary>Creates a bounded CSV processor. Headers are processed as ordinary cells.</summary>
    public CsvDocumentProcessor(ITextRedactor redactor, DocumentLimits? limits = null) : base(redactor, limits) { }
    /// <inheritdoc />
    public override string Format => "csv";
    /// <inheritdoc />
    protected override async ValueTask<string> TransformAsync(string text, DocumentProcessingContext context, CancellationToken cancellationToken)
    {
        if (text.Length == 0) return "";
        var rows = Parse(text, cancellationToken);
        var output = new StringBuilder();
        using (var destination = new StringWriter(output, CultureInfo.InvariantCulture))
        using (var writer = new CsvWriter(destination, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            Delimiter = ",",
            NewLine = "\r\n",
            ExceptionMessagesContainRawData = false
        }))
        {
            for (int row = 0; row < rows.Count; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row > 0) writer.NextRecord();
                for (int cell = 0; cell < rows[row].Count; cell++)
                {
                    var value = await context.RedactAsync(rows[row][cell], $"/rows/{row}/cells/{cell}").ConfigureAwait(false);
                    writer.WriteField(value, true);
                    writer.Flush();
                    if (output.Length > Limits.MaximumOutputBytes) throw new RedactionException(RedactionError.LimitExceeded);
                }
            }
        }
        var result = output.ToString();
        _ = Parse(result, cancellationToken);
        return result;
    }
    private List<List<string>> Parse(string text, CancellationToken token)
    {
        using var source = new StringReader(text);
        using var parser = new CsvParser(source, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            Delimiter = ",",
            DetectDelimiter = false,
            Mode = CsvMode.RFC4180,
            IgnoreBlankLines = false,
            AllowComments = false,
            TrimOptions = TrimOptions.None,
            WhiteSpaceChars = [],
            ExceptionMessagesContainRawData = false,
            BadDataFound = _ => throw new RedactionException(RedactionError.InvalidDocument)
        });
        var rows = new List<List<string>>();
        int fields = 0;
        while (parser.Read())
        {
            token.ThrowIfCancellationRequested();
            // CsvHelper accepts an unfinished escaped quote at EOF; valid records always have paired quotes.
            if (parser.RawRecord.AsSpan().Count('"') % 2 != 0) throw new RedactionException(RedactionError.InvalidDocument);
            var row = new List<string>();
            for (int cell = 0; cell < parser.Count; cell++)
            {
                token.ThrowIfCancellationRequested();
                if (++fields > Limits.MaximumFields) throw new RedactionException(RedactionError.LimitExceeded);
                row.Add(parser[cell]);
            }
            rows.Add(row);
        }
        return rows;
    }
}
