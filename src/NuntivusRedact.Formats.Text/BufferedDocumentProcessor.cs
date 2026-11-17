using System.Text;

namespace NuntivusRedact.Formats.Text;

/// <summary>Shared bounded UTF-8 stream processing. No temporary files; output is staged in managed memory.</summary>
public abstract class BufferedDocumentProcessor : IDocumentProcessor
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly ITextRedactor redactor;
    /// <summary>Creates a processor with validated immutable limits.</summary>
    protected BufferedDocumentProcessor(ITextRedactor redactor, DocumentLimits? limits = null)
    {
        this.redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        Limits = limits ?? new();
        Limits.Validate();
    }
    /// <summary>Gets immutable processing limits.</summary>
    protected DocumentLimits Limits { get; }
    /// <inheritdoc />
    public abstract string Format { get; }
    /// <summary>Transforms a completely decoded UTF-8 document before any output is written.</summary>
    protected abstract ValueTask<string> TransformAsync(string text, DocumentProcessingContext context, CancellationToken cancellationToken);
    /// <inheritdoc />
    public async ValueTask<DocumentRedactionResult> ProcessAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (ReferenceEquals(input, output) || !input.CanRead || !output.CanWrite) throw new ArgumentException("Distinct readable input and writable output streams are required.");
        if (output.CanSeek && (output.Length != 0 || output.Position != 0)) throw new ArgumentException("The output stream must be empty and positioned at its start.", nameof(output));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16_384];
            int read;
            while ((read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (read > Limits.MaximumInputBytes - buffer.Length) throw new RedactionException(RedactionError.LimitExceeded);
                buffer.Write(chunk, 0, read);
            }
            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
            var text = Utf8.GetString(bytes);
            var context = new DocumentProcessingContext(redactor, Limits, cancellationToken);
            var transformed = await TransformAsync(text, context, cancellationToken).ConfigureAwait(false);
            if (Utf8.GetByteCount(transformed) > Limits.MaximumOutputBytes) throw new RedactionException(RedactionError.LimitExceeded);
            var encoded = Utf8.GetBytes(transformed);
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
            return context.Report();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.InvalidDocument); }
    }
}

/// <summary>Explicit UTF-8 plain-text processing, not a fallback for unknown/binary formats.</summary>
public sealed class TextDocumentProcessor : BufferedDocumentProcessor
{
    /// <summary>Creates a bounded plain-text processor.</summary>
    public TextDocumentProcessor(ITextRedactor redactor, DocumentLimits? limits = null) : base(redactor, limits) { }
    /// <inheritdoc />
    public override string Format => "text";
    /// <inheritdoc />
    protected override ValueTask<string> TransformAsync(string text, DocumentProcessingContext context, CancellationToken cancellationToken) => context.RedactAsync(text, "/");
}

/// <summary>Explicit extension-based selection. Extensions are hints, not content/security validation.</summary>
public sealed class DocumentProcessorRegistry
{
    private readonly IReadOnlyDictionary<string, IDocumentProcessor> processors;
    /// <summary>Copies processors and rejects duplicate format identifiers.</summary>
    public DocumentProcessorRegistry(IEnumerable<IDocumentProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        this.processors = processors.ToDictionary(p => p.Format, StringComparer.OrdinalIgnoreCase);
    }
    /// <summary>Selects only a registered format; there is no unknown-to-text fallback.</summary>
    public IDocumentProcessor Get(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return processors.TryGetValue(format, out var processor) ? processor : throw new RedactionException(RedactionError.UnsupportedFormat);
    }
}
