using System.Xml;
using System.Xml.Linq;
using NuntivusRedact.Formats.Text;

namespace NuntivusRedact.Formats.Xml;

/// <summary>Processes XML text, CDATA, comments and ordinary attributes. DTDs/PIs and sensitive structural names are rejected.</summary>
public sealed class XmlDocumentProcessor : BufferedDocumentProcessor
{
    /// <summary>Creates a bounded XML processor with DTDs and external resolution prohibited.</summary>
    public XmlDocumentProcessor(ITextRedactor redactor, DocumentLimits? limits = null) : base(redactor, limits) { }
    /// <inheritdoc />
    public override string Format => "xml";
    private XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = Limits.MaximumInputBytes,
        MaxCharactersFromEntities = 1024,
        IgnoreComments = false,
        IgnoreWhitespace = false
    };
    private XDocument Parse(string text, CancellationToken token)
    {
        using (var preflight = XmlReader.Create(new StringReader(text), Settings()))
        {
            while (preflight.Read())
            {
                token.ThrowIfCancellationRequested();
                if (preflight.Depth > Limits.MaximumDepth) throw new RedactionException(RedactionError.LimitExceeded);
                if (preflight.NodeType == XmlNodeType.ProcessingInstruction) throw new RedactionException(RedactionError.UnsupportedFormat);
            }
        }
        using var reader = XmlReader.Create(new StringReader(text), Settings());
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    /// <inheritdoc />
    protected override async ValueTask<string> TransformAsync(string text, DocumentProcessingContext context, CancellationToken cancellationToken)
    {
        var document = Parse(text, cancellationToken);
        int index = 0;
        foreach (var element in document.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = $"/nodes/{index++}";
            await context.AssertUnchangedAsync(element.Name.LocalName).ConfigureAwait(false);
            await context.AssertUnchangedAsync(element.Name.NamespaceName).ConfigureAwait(false);
            int attributeIndex = 0;
            foreach (var attribute in element.Attributes())
            {
                await context.AssertUnchangedAsync(attribute.Name.LocalName).ConfigureAwait(false);
                await context.AssertUnchangedAsync(attribute.Name.NamespaceName).ConfigureAwait(false);
                if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace == XNamespace.Xml)
                    await context.AssertUnchangedAsync(attribute.Value).ConfigureAwait(false);
                else attribute.Value = await context.RedactAsync(attribute.Value, $"{path}/attributes/{attributeIndex}").ConfigureAwait(false);
                attributeIndex++;
            }
            var textNodes = element.Nodes().OfType<XText>().ToArray();
            if (element.HasElements && textNodes.Any(t => !string.IsNullOrWhiteSpace(t.Value)))
                throw new RedactionException(RedactionError.UnsupportedFormat);
            if (!element.HasElements && textNodes.Length > 0)
            {
                textNodes[0].Value = await context.RedactAsync(string.Concat(textNodes.Select(t => t.Value)), path + "/text").ConfigureAwait(false);
                foreach (var extra in textNodes.Skip(1)) extra.Remove();
            }
        }
        foreach (var comment in document.DescendantNodes().OfType<XComment>())
            comment.Value = await context.RedactAsync(comment.Value, $"/comments/{index++}").ConfigureAwait(false);
        document.Declaration = null;
        var result = document.ToString(SaveOptions.DisableFormatting);
        _ = Parse(result, cancellationToken);
        return result;
    }
}
