using System.Xml.Linq;

namespace NuntivusRedact.Formats.Office;

internal sealed class ExcelContent(IOfficeString strings) : IExcelContent
{
    private readonly XNamespace X = OfficePackage.ExcelNamespace;
    private readonly HashSet<string> UnsupportedElements = new(StringComparer.Ordinal)
    {
        "extLst", "rPh", "phoneticPr", "drawing", "legacyDrawing", "legacyDrawingHF", "oleObjects", "oleObject", "controls", "control",
        "definedNames", "definedName", "externalReferences", "externalReference", "pivotCaches", "pivotCache", "pivotTableParts",
        "connections", "queryTable", "dataConsolidate", "dataValidations", "conditionalFormatting", "headerFooter", "webPublishItems", "numFmts", "numFmt"
    };

    public async ValueTask TransformAsync(OfficePart part, OfficeContext context, string path, HashSet<XObject> handled)
    {
        var document = part.Xml!;
        if (document.Descendants().Any(e => e.Name.Namespace != X || (UnsupportedElements.Contains(e.Name.LocalName) && !(context.ImagesEnabled && e.Name == X + "drawing"))))
            throw new RedactionException(RedactionError.UnsupportedFormat);
        if (part.Kind == OfficeKind.CalculationChain) { part.Removed = true; return; }
        int field = 0;
        foreach (var item in document.Descendants().Where(e => e.Name == X + "si" || e.Name == X + "is" || e.Name == X + "text"))
            await context.RedactRunsAsync(item.Descendants(X + "t").ToArray(), $"{path}/fields/{field++}", handled, true).ConfigureAwait(false);
        foreach (var author in document.Descendants(X + "author"))
        {
            author.Value = await RedactStringAsync(author.Value, context, $"{path}/fields/{field++}").ConfigureAwait(false);
            handled.Add(author);
        }
        foreach (var cell in document.Descendants(X + "c"))
        {
            context.Token.ThrowIfCancellationRequested();
            var formula = cell.Element(X + "f");
            var value = cell.Element(X + "v");
            if (formula is not null)
            {
                if (formula.Value.Length > 8192 || !new OfficeFormula(formula.Value, context.Limits.MaximumXmlDepth).IsSupported() ||
                    formula.Attributes().Any(a => a.IsNamespaceDeclaration || a.Name != "t" || a.Value != "normal"))
                    throw new RedactionException(RedactionError.UnsupportedFormat);
                await context.AssertUnchangedAsync(formula.Value).ConfigureAwait(false);
                handled.Add(formula);
                if (value is not null)
                {
                    _ = await context.RedactAsync(strings.Decode(value.Value), $"{path}/fields/{field++}").ConfigureAwait(false);
                    value.Remove();
                }
                cell.Attribute("t")?.Remove();
            }
            else if ((string?)cell.Attribute("t") == "str" && value is not null)
            {
                value.Value = await RedactStringAsync(value.Value, context, $"{path}/fields/{field++}").ConfigureAwait(false);
                handled.Add(value);
            }
        }
        if (document.Descendants(X + "t").Any(t => !handled.Contains(t))) throw new RedactionException(RedactionError.UnsupportedFormat);
        if (part.Kind == OfficeKind.Workbook)
        {
            var root = document.Root!;
            var calculation = root.Element(X + "calcPr");
            if (calculation is null)
            {
                calculation = new XElement(X + "calcPr");
                var previous = root.Elements().LastOrDefault(e => e.Name.LocalName is "fileVersion" or "fileSharing" or "workbookPr" or "workbookProtection" or "bookViews" or "sheets" or "functionGroups");
                if (previous is null) root.AddFirst(calculation); else previous.AddAfterSelf(calculation);
            }
            calculation.SetAttributeValue("fullCalcOnLoad", "1");
            calculation.SetAttributeValue("forceFullCalc", "1");
            calculation.SetAttributeValue("calcMode", "auto");
        }
    }

    private async ValueTask<string> RedactStringAsync(string value, OfficeContext context, string path)
    {
        var decoded = strings.Decode(value);
        if (decoded.Length > 32767) throw new RedactionException(RedactionError.LimitExceeded);
        var result = await context.RedactAsync(decoded, path).ConfigureAwait(false);
        if (result.Text.Length > 32767) throw new RedactionException(RedactionError.LimitExceeded);
        return strings.Encode(result.Text);
    }
}
