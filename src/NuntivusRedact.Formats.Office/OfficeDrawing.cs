using System.Xml.Linq;

namespace NuntivusRedact.Formats.Office;

internal sealed class OfficeDrawing(IOfficeString strings) : IOfficeDrawing
{
    private readonly Dictionary<string, HashSet<string>> allowed = new(StringComparer.Ordinal)
    {
        [OfficePackage.WordDrawingNamespace] = new(StringComparer.Ordinal) { "inline", "anchor", "simplePos", "positionH", "positionV", "posOffset", "align", "extent", "effectExtent", "wrapNone", "docPr", "cNvGraphicFramePr" },
        [OfficePackage.PictureNamespace] = new(StringComparer.Ordinal) { "pic", "nvPicPr", "cNvPr", "cNvPicPr", "blipFill", "spPr" },
        [OfficePackage.DrawingNamespace] = new(StringComparer.Ordinal) { "graphic", "graphicData", "graphicFrameLocks", "picLocks", "blip", "stretch", "fillRect", "xfrm", "off", "ext", "prstGeom", "avLst", "srcRect" },
        [OfficePackage.ExcelDrawingNamespace] = new(StringComparer.Ordinal) { "wsDr", "twoCellAnchor", "oneCellAnchor", "absoluteAnchor", "from", "to", "col", "colOff", "row", "rowOff", "pos", "ext", "pic", "nvPicPr", "cNvPr", "cNvPicPr", "blipFill", "spPr", "clientData" }
    };

    public async ValueTask TransformAsync(XElement root, OfficeContext context, string path, HashSet<XObject> handled, bool word)
    {
        if (!context.ImagesEnabled) throw new RedactionException(RedactionError.UnsupportedFormat);
        foreach (var element in root.DescendantsAndSelf())
        {
            context.Token.ThrowIfCancellationRequested();
            if (word && ReferenceEquals(element, root) && element.Name == XName.Get("drawing", OfficePackage.WordNamespace)) continue;
            if (!allowed.TryGetValue(element.Name.NamespaceName, out var names) || !names.Contains(element.Name.LocalName) ||
                (word ? element.Name.NamespaceName == OfficePackage.ExcelDrawingNamespace : element.Name.NamespaceName is OfficePackage.WordDrawingNamespace or OfficePackage.PictureNamespace))
                throw new RedactionException(RedactionError.UnsupportedFormat);
            if (element.Name == XName.Get("graphicData", OfficePackage.DrawingNamespace) && (string?)element.Attribute("uri") != OfficePackage.PictureNamespace)
                throw new RedactionException(RedactionError.UnsupportedFormat);
            if (element.Name == XName.Get("blip", OfficePackage.DrawingNamespace) &&
                (element.Attribute(XName.Get("embed", OfficePackage.RelationshipNamespace)) is null ||
                 element.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != XName.Get("embed", OfficePackage.RelationshipNamespace))))
                throw new RedactionException(RedactionError.UnsupportedFormat);
            if (element.Name.LocalName == "prstGeom" && (string?)element.Attribute("prst") != "rect")
                throw new RedactionException(RedactionError.UnsupportedFormat);
        }
        if (word && root.Descendants(XName.Get("pic", OfficePackage.PictureNamespace)).Count() != 1)
            throw new RedactionException(RedactionError.UnsupportedFormat);
        int field = 0;
        foreach (var attribute in root.DescendantsAndSelf().Where(e => e.Name.LocalName is "docPr" or "cNvPr")
            .Attributes().Where(a => a.Name.Namespace == XNamespace.None && a.Name.LocalName is "name" or "descr" or "title"))
        {
            strings.RejectEscapes(attribute.Value);
            attribute.Value = (await context.RedactAsync(attribute.Value, $"{path}/metadata/{field++}").ConfigureAwait(false)).Text;
            strings.RejectEscapes(attribute.Value);
            handled.Add(attribute);
        }
    }
}
