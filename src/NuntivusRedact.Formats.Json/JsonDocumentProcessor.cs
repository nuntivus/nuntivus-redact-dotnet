using System.Text;
using System.Text.Json;
using NuntivusRedact.Formats.Text;

namespace NuntivusRedact.Formats.Json;

/// <summary>Processes UTF-8 JSON string keys and values. Duplicate original or resulting keys are rejected.</summary>
public sealed class JsonDocumentProcessor : BufferedDocumentProcessor
{
    /// <summary>Creates a bounded JSON processor.</summary>
    public JsonDocumentProcessor(ITextRedactor redactor, DocumentLimits? limits = null) : base(redactor, limits) { }
    /// <inheritdoc />
    public override string Format => "json";
    /// <inheritdoc />
    protected override async ValueTask<string> TransformAsync(string text, DocumentProcessingContext context, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = Limits.MaximumDepth });
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            await WriteAsync(document.RootElement, writer, context, "", cancellationToken).ConfigureAwait(false);
            writer.Flush();
        }
        if (output.Length > Limits.MaximumOutputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        var result = Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        using var validated = JsonDocument.Parse(result, new JsonDocumentOptions { MaxDepth = Limits.MaximumDepth });
        return result;
    }
    private async ValueTask WriteAsync(JsonElement element, Utf8JsonWriter writer, DocumentProcessingContext context, string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        context.InspectNode();
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var original = new HashSet<string>(StringComparer.Ordinal);
                var transformed = new HashSet<string>(StringComparer.Ordinal);
                int propertyIndex = 0;
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPath = $"{path}/properties/{propertyIndex++}";
                    if (!original.Add(property.Name)) throw new RedactionException(RedactionError.InvalidDocument);
                    var name = await context.RedactAsync(property.Name, propertyPath + "/name").ConfigureAwait(false);
                    if (!transformed.Add(name)) throw new RedactionException(RedactionError.InvalidDocument);
                    writer.WritePropertyName(name);
                    await WriteAsync(property.Value, writer, context, propertyPath + "/value", token).ConfigureAwait(false);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                int index = 0;
                foreach (var item in element.EnumerateArray()) await WriteAsync(item, writer, context, $"{path}/items/{index++}", token).ConfigureAwait(false);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(await context.RedactAsync(element.GetString()!, path).ConfigureAwait(false));
                break;
            case JsonValueKind.Number:
                await context.AssertUnchangedAsync(element.GetRawText()).ConfigureAwait(false);
                element.WriteTo(writer);
                break;
            default:
                await context.AssertUnchangedAsync(element.GetRawText()).ConfigureAwait(false);
                element.WriteTo(writer);
                break;
        }
        if (writer.BytesCommitted + writer.BytesPending > Limits.MaximumOutputBytes) throw new RedactionException(RedactionError.LimitExceeded);
    }
}
