using System.Xml.Linq;

namespace NuntivusRedact.Formats.Office;

internal sealed class OfficeContent(IOfficeString strings, IExcelContent excel, IOfficeDrawing drawings) : IOfficeContent
{
    private readonly XNamespace W = OfficePackage.WordNamespace;
    private readonly HashSet<string> Namespaces = new(StringComparer.Ordinal)
    {
        OfficePackage.WordNamespace, OfficePackage.ExcelNamespace, OfficePackage.DrawingNamespace,
        OfficePackage.RelationshipNamespace, XNamespace.Xml.NamespaceName,
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties",
        "http://purl.org/dc/elements/1.1/", "http://purl.org/dc/terms/", "http://purl.org/dc/dcmitype/",
        "http://www.w3.org/2001/XMLSchema-instance", "http://www.w3.org/2001/XMLSchema",
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties",
        "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties",
        "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"
    };
    private readonly HashSet<string> UnsupportedWordElements = new(StringComparer.Ordinal)
    {
        "altChunk", "customXml", "customXmlPr", "dataBinding", "sdt", "fldSimple", "fldChar", "instrText", "delInstrText",
        "drawing", "pict", "object", "txbxContent", "subDoc", "contentPart", "sym", "softHyphen", "noBreakHyphen",
        "ins", "del", "delText", "moveFrom", "moveTo", "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
        "customXmlInsRangeStart", "customXmlInsRangeEnd", "customXmlDelRangeStart", "customXmlDelRangeEnd", "docVars", "mailMerge"
    };

    public async ValueTask TransformAsync(OfficePart part, OfficeContext context, string path)
    {
        var document = part.Xml ?? throw new RedactionException(RedactionError.InvalidDocument);
        var handled = new HashSet<XObject>();
        ValidateNamespaces(document, context.ImagesEnabled);
        if (part.Kind == OfficeKind.Word)
            await WordAsync(document, context, path, handled).ConfigureAwait(false);
        else if (part.Kind == OfficeKind.Properties)
            await PropertiesAsync(document, context, path, handled).ConfigureAwait(false);
        else if (part.Kind == OfficeKind.Drawing)
            await drawings.TransformAsync(document.Root!, context, path, handled, false).ConfigureAwait(false);
        else if (part.Kind is OfficeKind.Workbook or OfficeKind.Worksheet or OfficeKind.SharedStrings or OfficeKind.ExcelStyles or OfficeKind.ExcelComments or OfficeKind.CalculationChain)
            await excel.TransformAsync(part, context, path, handled).ConfigureAwait(false);
        else if (part.Kind != OfficeKind.Theme)
            throw new RedactionException(RedactionError.UnsupportedFormat);
        await InspectAsync(document, context, handled).ConfigureAwait(false);
    }

    private void ValidateNamespaces(XDocument document, bool imagesEnabled)
    {
        foreach (var element in document.Descendants())
        {
            if ((element.Name.LocalName is "ext" or "extLst" && !(imagesEnabled && element.Name.LocalName == "ext" &&
                element.Name.NamespaceName is OfficePackage.DrawingNamespace or OfficePackage.ExcelDrawingNamespace)) ||
                (element.Name.NamespaceName == "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes" && element.Name.LocalName is "blob" or "oblob" or "stream" or "storage" or "vstream" or "ostorage" or "cf"))
                throw new RedactionException(RedactionError.UnsupportedFormat);
            if (!AllowedNamespace(element.Name.NamespaceName, imagesEnabled)) throw new RedactionException(RedactionError.UnsupportedFormat);
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    if (!AllowedNamespace(attribute.Value, imagesEnabled)) throw new RedactionException(RedactionError.UnsupportedFormat);
                }
                else if (attribute.Name.Namespace != XNamespace.None && !AllowedNamespace(attribute.Name.NamespaceName, imagesEnabled))
                    throw new RedactionException(RedactionError.UnsupportedFormat);
            }
        }
    }

    private async ValueTask WordAsync(XDocument document, OfficeContext context, string path, HashSet<XObject> handled)
    {
        if (document.Descendants().Any(e => (e.Name.Namespace != W && !(context.ImagesEnabled && e.Ancestors(W + "drawing").Any())) ||
            (UnsupportedWordElements.Contains(e.Name.LocalName) && !(context.ImagesEnabled && e.Name == W + "drawing")) || e.Name.LocalName.EndsWith("Change", StringComparison.Ordinal)))
            throw new RedactionException(RedactionError.UnsupportedFormat);
        int picture = 0;
        foreach (var drawing in document.Descendants(W + "drawing"))
            await drawings.TransformAsync(drawing, context, $"{path}/pictures/{picture++}", handled, true).ConfigureAwait(false);
        int field = 0;
        foreach (var paragraph in document.Descendants(W + "p"))
        {
            var group = new List<XElement>();
            foreach (var element in paragraph.Descendants())
            {
                context.Token.ThrowIfCancellationRequested();
                if (element.Name == W + "t") group.Add(element);
                else if (element.Name == W + "tab" || element.Name == W + "br" || element.Name == W + "cr" || element.Name == W + "drawing")
                {
                    await context.RedactRunsAsync(group, $"{path}/fields/{field++}", handled).ConfigureAwait(false);
                    group.Clear();
                }
            }
            await context.RedactRunsAsync(group, $"{path}/fields/{field++}", handled).ConfigureAwait(false);
        }
        if (document.Descendants(W + "t").Any(e => !handled.Contains(e))) throw new RedactionException(RedactionError.UnsupportedFormat);
        foreach (var attribute in document.Descendants().Attributes().Where(a => a.Name == W + "author" || a.Name == W + "initials"))
        {
            strings.RejectEscapes(attribute.Value);
            attribute.Value = (await context.RedactAsync(attribute.Value, $"{path}/fields/{field++}").ConfigureAwait(false)).Text;
            strings.RejectEscapes(attribute.Value);
            handled.Add(attribute);
        }
    }

    private bool AllowedNamespace(string value, bool imagesEnabled) => Namespaces.Contains(value) ||
        (imagesEnabled && value is OfficePackage.WordDrawingNamespace or OfficePackage.ExcelDrawingNamespace or OfficePackage.PictureNamespace);

    private async ValueTask PropertiesAsync(XDocument document, OfficeContext context, string path, HashSet<XObject> handled)
    {
        int field = 0;
        foreach (var element in document.Descendants().Where(e => !e.HasElements))
        {
            var ns = element.Name.NamespaceName;
            bool text = ns == "http://purl.org/dc/elements/1.1/" ||
                (ns == "http://schemas.openxmlformats.org/package/2006/metadata/core-properties" && element.Name.LocalName is "keywords" or "lastModifiedBy" or "category" or "contentStatus" or "version" or "revision") ||
                (ns == "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" && element.Name.LocalName is "Template" or "Manager" or "Company" or "Application" or "HyperlinkBase") ||
                (ns == "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes" && element.Name.LocalName is "lpstr" or "lpwstr" or "bstr");
            if (!text) continue;
            strings.RejectEscapes(element.Value);
            element.Value = (await context.RedactAsync(element.Value, $"{path}/fields/{field++}").ConfigureAwait(false)).Text;
            strings.RejectEscapes(element.Value);
            handled.Add(element);
        }
    }

    private async ValueTask InspectAsync(XDocument document, OfficeContext context, HashSet<XObject> handled)
    {
        foreach (var element in document.Descendants())
        {
            context.Token.ThrowIfCancellationRequested();
            foreach (var declaration in element.Attributes().Where(a => a.IsNamespaceDeclaration))
                await context.AssertUnchangedAsync(declaration.Name.LocalName).ConfigureAwait(false);
            foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration && !handled.Contains(a)))
                if (attribute.Name != XNamespace.Xml + "space") await context.AssertUnchangedAsync(attribute.Value).ConfigureAwait(false);
            if (handled.Contains(element)) continue;
            foreach (var text in element.Nodes().OfType<XText>().Where(t => !string.IsNullOrWhiteSpace(t.Value)))
                await context.AssertUnchangedAsync(text.Value).ConfigureAwait(false);
        }
    }
}
