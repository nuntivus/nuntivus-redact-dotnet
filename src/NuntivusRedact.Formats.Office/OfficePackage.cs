using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NuntivusRedact.Ocr;

namespace NuntivusRedact.Formats.Office;

internal enum OfficeKind { Word, Workbook, Worksheet, SharedStrings, ExcelStyles, ExcelComments, CalculationChain, Theme, Properties, EmbeddedWord, EmbeddedExcel, Image, Drawing }

internal sealed class OfficePart(string name, string contentType, OfficeKind kind, byte[] bytes, XDocument? xml)
{
    internal string Name { get; } = name;
    internal string ContentType { get; } = contentType;
    internal OfficeKind Kind { get; } = kind;
    internal byte[] Bytes { get; set; } = bytes;
    internal XDocument? Xml { get; } = xml;
    internal bool Removed { get; set; }
}

internal sealed class OfficePackage(IOfficeContent content, IOfficeString strings, IImageRedactor? images = null) : IOfficePackage
{
    internal const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string ExcelNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    internal const string WordDrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    internal const string ExcelDrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    internal const string PictureNamespace = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    internal const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Prefix = "application/vnd.openxmlformats-officedocument.";

    public async ValueTask<DocumentRedactionResult> ProcessAsync(Stream input, Stream output, string format,
        IEditAwareTextRedactor redactor, OfficeLimits limits, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (ReferenceEquals(input, output) || !input.CanRead || !output.CanWrite || (output.CanSeek && (output.Length != 0 || output.Position != 0)))
            throw new ArgumentException("Use distinct readable input and empty writable output streams.");
        token.ThrowIfCancellationRequested();
        try
        {
            using var source = new OfficeBuffer(limits.MaximumInputBytes);
            await input.CopyToAsync(source, token).ConfigureAwait(false);
            var context = new OfficeContext(redactor, limits, token, strings, images is not null);
            var result = await TransformAsync(source.ToArray(), format, context, "/packages/0", 0).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await output.WriteAsync(result, token).ConfigureAwait(false);
            return new DocumentRedactionResult(context.Report);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.InvalidDocument); }
    }

    private async ValueTask<byte[]> TransformAsync(byte[] input, string format, OfficeContext context, string location, int depth)
    {
        if (depth > context.Limits.MaximumNestingDepth) throw new RedactionException(RedactionError.LimitExceeded);
        var entries = await ReadArchiveAsync(input, context).ConfigureAwait(false);
        if (!entries.TryGetValue("[Content_Types].xml", out var typeBytes)) throw Invalid();
        var contentTypes = context.Parse(typeBytes);
        var parts = Inventory(entries, contentTypes, context);
        foreach (var attribute in contentTypes.Descendants().Attributes().Where(a => a.Name.LocalName == "Extension"))
            await context.AssertUnchangedAsync(attribute.Value).ConfigureAwait(false);
        var relationships = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => e.Key.EndsWith(".rels", StringComparison.Ordinal)))
            relationships.Add(entry.Key, context.Parse(entry.Value));
        foreach (var document in relationships.Values.Prepend(contentTypes))
            foreach (var declaration in document.Descendants().Attributes().Where(a => a.IsNamespaceDeclaration))
                await context.AssertUnchangedAsync(declaration.Name.LocalName).ConfigureAwait(false);
        ValidateRelationships(parts, relationships, format);
        ValidateSharedStrings(parts);
        foreach (var entry in entries.Keys)
            await context.AssertUnchangedAsync(entry).ConfigureAwait(false);
        foreach (var rel in relationships.Values)
            foreach (var attribute in rel.Descendants().Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName is "Id" or "Target"))
                await context.AssertUnchangedAsync(attribute.Value).ConfigureAwait(false);
        ValidateSdk(input, format, context);
        int index = 0;
        foreach (var part in parts.Values)
        {
            context.Token.ThrowIfCancellationRequested();
            var path = $"{location}/parts/{index++}";
            if (part.Kind is OfficeKind.EmbeddedWord or OfficeKind.EmbeddedExcel)
            {
                part.Bytes = await TransformAsync(part.Bytes, part.Kind == OfficeKind.EmbeddedWord ? "docx" : "xlsx", context, path + "/packages/0", depth + 1).ConfigureAwait(false);
            }
            else if (part.Kind == OfficeKind.Image)
            {
                context.BeginImage();
                ImageRedactionResult image;
                try
                {
                    image = await images!.RedactAsync(part.Bytes, part.ContentType, context.Redactor,
                        context.Limits.MaximumImagePixels - context.ImagePixels, context.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.Token.IsCancellationRequested) { throw; }
                catch (RedactionException error) { throw new RedactionException(error.Error); }
                catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
                context.CompleteImage(image, path + "/image/0");
                part.Bytes = image.Png.ToArray();
                var declaration = contentTypes.Root!.Elements(Types + "Override").SingleOrDefault(e => (string?)e.Attribute("PartName") == "/" + part.Name);
                if (declaration is null) contentTypes.Root.Add(new XElement(Types + "Override", new XAttribute("PartName", "/" + part.Name), new XAttribute("ContentType", "image/png")));
                else declaration.SetAttributeValue("ContentType", "image/png");
            }
            else await content.TransformAsync(part, context, path).ConfigureAwait(false);
        }
        foreach (var removed in parts.Values.Where(p => p.Removed))
        {
            foreach (var element in contentTypes.Root!.Elements(Types + "Override").Where(e => (string?)e.Attribute("PartName") == "/" + removed.Name).ToArray()) element.Remove();
            foreach (var (name, document) in relationships)
            {
                var source = RelationshipSource(name);
                foreach (var rel in document.Root!.Elements().Where(e => Resolve(source, (string)e.Attribute("Target")!) == removed.Name).ToArray()) rel.Remove();
            }
        }
        using var buffer = new OfficeBuffer(context.Limits.MaximumOutputBytes);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            WriteXml(zip, "[Content_Types].xml", contentTypes, context);
            foreach (var (name, xml) in relationships) WriteXml(zip, name, xml, context);
            foreach (var part in parts.Values.Where(p => !p.Removed))
            {
                context.Token.ThrowIfCancellationRequested();
                if (part.Xml is not null) WriteXml(zip, part.Name, part.Xml, context);
                else
                {
                    if (part.Bytes.Length > context.Limits.MaximumEntryBytes) throw new RedactionException(RedactionError.LimitExceeded);
                    context.Serialize(part.Bytes.Length);
                    using var target = CreateEntry(zip, part.Name).Open();
                    await target.WriteAsync(part.Bytes, context.Token).ConfigureAwait(false);
                }
            }
        }
        var output = buffer.ToArray();
        ValidateSdk(output, format, context);
        return output;
    }

    private async ValueTask<Dictionary<string, byte[]>> ReadArchiveAsync(byte[] bytes, OfficeContext context)
    {
        using var stream = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > context.Limits.MaximumEntries) throw new RedactionException(RedactionError.LimitExceeded);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            context.Entry();
            if (!ValidName(entry.FullName) || !names.Add(entry.FullName)) throw Invalid();
            if (entry.Length > context.Limits.MaximumEntryBytes || entry.Length > context.Limits.MaximumInflatedBytes - context.InflatedBytes ||
                entry.Length > Math.Max(1L, entry.CompressedLength) * context.Limits.MaximumCompressionRatio)
                throw new RedactionException(RedactionError.LimitExceeded);
            using var output = new OfficeBuffer(context.Limits.MaximumEntryBytes);
            using var source = entry.Open();
            var buffer = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(buffer, context.Token).ConfigureAwait(false)) != 0)
            {
                context.Inflate(read);
                await output.WriteAsync(buffer.AsMemory(0, read), context.Token).ConfigureAwait(false);
            }
            if (output.Length != entry.Length) throw Invalid();
            result.Add(entry.FullName, output.ToArray());
        }
        return result;
    }

    private bool ValidName(string name) => name == "[Content_Types].xml" ||
        (name.Length is > 0 and <= 240 && !name.StartsWith('/') &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.') &&
        name.Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".." && !segment.EndsWith('.')));

    private Dictionary<string, OfficePart> Inventory(Dictionary<string, byte[]> entries, XDocument types, OfficeContext context)
    {
        if (types.Root?.Name != Types + "Types") throw Invalid();
        AssertAttributes(types.Root, []);
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in types.Root.Elements())
        {
            if (element.Name == Types + "Default")
            {
                AssertAttributes(element, ["Extension", "ContentType"]);
                var extension = Required(element, "Extension");
                if (extension.Length > 16 || !extension.All(char.IsAsciiLetterOrDigit)) throw Unsupported();
                if (!defaults.TryAdd(extension, Required(element, "ContentType"))) throw Invalid();
            }
            else if (element.Name == Types + "Override")
            {
                AssertAttributes(element, ["PartName", "ContentType"]);
                var name = Required(element, "PartName");
                var type = Required(element, "ContentType");
                if (name == "/[Content_Types].xml" || (KnownKind(type) is null && type != "application/vnd.openxmlformats-package.relationships+xml")) throw Unsupported();
                if (!name.StartsWith('/') || !entries.ContainsKey(name[1..]) || !overrides.TryAdd(name[1..], Required(element, "ContentType"))) throw Invalid();
            }
            else throw Unsupported();
            if (element.HasElements || !string.IsNullOrWhiteSpace(element.Value)) throw Invalid();
        }
        foreach (var value in defaults)
            if (!((value.Key == "rels" && value.Value == "application/vnd.openxmlformats-package.relationships+xml") ||
                (value.Key == "xml" && value.Value == "application/xml") || KnownKind(value.Value) is not null)) throw Unsupported();
        var result = new Dictionary<string, OfficePart>(StringComparer.Ordinal);
        foreach (var (name, data) in entries)
        {
            if (name == "[Content_Types].xml") continue;
            var extension = name[(name.LastIndexOf('.') + 1)..];
            var type = overrides.GetValueOrDefault(name) ?? defaults.GetValueOrDefault(extension) ?? throw Invalid();
            if (name.EndsWith(".rels", StringComparison.Ordinal))
            {
                if (type != "application/vnd.openxmlformats-package.relationships+xml") throw Unsupported();
                continue;
            }
            var kind = KnownKind(type) ?? throw Unsupported();
            var xml = kind is OfficeKind.EmbeddedWord or OfficeKind.EmbeddedExcel or OfficeKind.Image ? null : context.Parse(data);
            result.Add(name, new OfficePart(name, type, kind, data, xml));
        }
        return result;
    }

    private OfficeKind? KnownKind(string type) => type switch
    {
        Prefix + "wordprocessingml.document.main+xml" or Prefix + "wordprocessingml.header+xml" or
        Prefix + "wordprocessingml.footer+xml" or Prefix + "wordprocessingml.footnotes+xml" or
        Prefix + "wordprocessingml.endnotes+xml" or Prefix + "wordprocessingml.comments+xml" or
        Prefix + "wordprocessingml.styles+xml" or Prefix + "wordprocessingml.numbering+xml" or
        Prefix + "wordprocessingml.settings+xml" or Prefix + "wordprocessingml.fontTable+xml" or
        Prefix + "wordprocessingml.webSettings+xml" => OfficeKind.Word,
        Prefix + "spreadsheetml.sheet.main+xml" => OfficeKind.Workbook,
        Prefix + "spreadsheetml.worksheet+xml" => OfficeKind.Worksheet,
        Prefix + "spreadsheetml.sharedStrings+xml" => OfficeKind.SharedStrings,
        Prefix + "spreadsheetml.styles+xml" => OfficeKind.ExcelStyles,
        Prefix + "spreadsheetml.comments+xml" => OfficeKind.ExcelComments,
        Prefix + "spreadsheetml.calcChain+xml" => OfficeKind.CalculationChain,
        Prefix + "theme+xml" => OfficeKind.Theme,
        "application/vnd.openxmlformats-package.core-properties+xml" or Prefix + "extended-properties+xml" or Prefix + "custom-properties+xml" => OfficeKind.Properties,
        Prefix + "wordprocessingml.document" => OfficeKind.EmbeddedWord,
        Prefix + "spreadsheetml.sheet" => OfficeKind.EmbeddedExcel,
        "image/png" or "image/jpeg" when images is not null => OfficeKind.Image,
        Prefix + "drawing+xml" when images is not null => OfficeKind.Drawing,
        _ => null
    };

    private void ValidateRelationships(Dictionary<string, OfficePart> parts, Dictionary<string, XDocument> documents, string format)
    {
        if (!documents.ContainsKey("_rels/.rels")) throw Invalid();
        var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int mainCount = 0;
        foreach (var (name, xml) in documents)
        {
            string source = RelationshipSource(name);
            if (source.Length != 0 && !parts.ContainsKey(source)) throw Invalid();
            if (xml.Root?.Name != Relationships + "Relationships") throw Invalid();
            AssertAttributes(xml.Root, []);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var edges = new List<string>();
            foreach (var rel in xml.Root.Elements())
            {
                if (rel.Name != Relationships + "Relationship" || rel.HasElements || !string.IsNullOrWhiteSpace(rel.Value)) throw Invalid();
                AssertAttributes(rel, ["Id", "Type", "Target", "TargetMode"]);
                if (!ids.Add(Required(rel, "Id"))) throw Invalid();
                if (rel.Attribute("TargetMode") is { Value: not "Internal" }) throw Unsupported();
                var target = Resolve(source, Required(rel, "Target"));
                if (!parts.TryGetValue(target, out var part)) throw Invalid();
                var type = Required(rel, "Type");
                if (!AllowedRelationship(type, part)) throw Unsupported();
                if (type == RelationshipNamespace + "/image" && (source.Length == 0 || parts[source].Kind is not OfficeKind.Word and not OfficeKind.Drawing)) throw Unsupported();
                if (type == RelationshipNamespace + "/drawing" && (source.Length == 0 || parts[source].Kind != OfficeKind.Worksheet)) throw Unsupported();
                if (type == RelationshipNamespace + "/officeDocument")
                {
                    if (source.Length != 0 || ++mainCount != 1 || part.ContentType != Prefix + (format == "docx" ? "wordprocessingml.document.main+xml" : "spreadsheetml.sheet.main+xml")) throw Invalid();
                }
                edges.Add(target);
            }
            graph.Add(source, edges);
        }
        if (mainCount != 1) throw Invalid();
        ValidatePictureReferences(parts, documents);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue("");
        while (queue.TryDequeue(out var name))
        {
            if (!visited.Add(name)) continue;
            if (graph.TryGetValue(name, out var edges)) foreach (var edge in edges) queue.Enqueue(edge);
        }
        if (parts.Keys.Any(name => !visited.Contains(name))) throw Unsupported();
    }

    private bool AllowedRelationship(string type, OfficePart target)
    {
        if (type == "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties")
            return target.ContentType == "application/vnd.openxmlformats-package.core-properties+xml";
        if (!type.StartsWith(RelationshipNamespace + "/", StringComparison.Ordinal)) return false;
        return type[(RelationshipNamespace.Length + 1)..] switch
        {
            "officeDocument" => target.Kind is OfficeKind.Word or OfficeKind.Workbook,
            "header" or "footer" or "footnotes" or "endnotes" or "comments" or "styles" or "numbering" or "settings" or "fontTable" or "webSettings" =>
                target.ContentType == Prefix + "wordprocessingml." + type[(type.LastIndexOf('/') + 1)..] + "+xml" ||
                (type.EndsWith("/styles", StringComparison.Ordinal) && target.Kind == OfficeKind.ExcelStyles) ||
                (type.EndsWith("/comments", StringComparison.Ordinal) && target.Kind == OfficeKind.ExcelComments),
            "worksheet" => target.Kind == OfficeKind.Worksheet,
            "sharedStrings" => target.Kind == OfficeKind.SharedStrings,
            "calcChain" => target.Kind == OfficeKind.CalculationChain,
            "theme" => target.Kind == OfficeKind.Theme,
            "extended-properties" => target.ContentType == Prefix + "extended-properties+xml",
            "custom-properties" => target.ContentType == Prefix + "custom-properties+xml",
            "package" => target.Kind is OfficeKind.EmbeddedWord or OfficeKind.EmbeddedExcel,
            "image" => images is not null && target.Kind == OfficeKind.Image,
            "drawing" => images is not null && target.Kind == OfficeKind.Drawing,
            _ => false
        };
    }

    private void ValidatePictureReferences(Dictionary<string, OfficePart> parts, Dictionary<string, XDocument> documents)
    {
        foreach (var part in parts.Values.Where(p => p.Xml is not null))
        {
            var rels = documents.FirstOrDefault(d => RelationshipSource(d.Key) == part.Name).Value;
            foreach (var element in part.Xml!.Descendants().Where(e => e.Name == XName.Get("blip", DrawingNamespace) || e.Name == XName.Get("drawing", ExcelNamespace)))
            {
                bool image = element.Name.LocalName == "blip";
                var id = (string?)element.Attribute(XName.Get(image ? "embed" : "id", RelationshipNamespace));
                var rel = rels?.Root?.Elements().SingleOrDefault(e => (string?)e.Attribute("Id") == id);
                if (id is null || rel is null || (string?)rel.Attribute("Type") != RelationshipNamespace + (image ? "/image" : "/drawing")) throw Invalid();
            }
        }
    }

    private void ValidateSharedStrings(Dictionary<string, OfficePart> parts)
    {
        XNamespace x = ExcelNamespace;
        var strings = parts.Values.Where(p => p.Kind == OfficeKind.SharedStrings).ToArray();
        if (strings.Length > 1) throw Invalid();
        int count = strings.Length == 0 ? 0 : strings[0].Xml!.Root!.Elements(x + "si").Count();
        foreach (var cell in parts.Values.Where(p => p.Kind == OfficeKind.Worksheet).SelectMany(p => p.Xml!.Descendants(x + "c")))
            if ((string?)cell.Attribute("t") == "s" && (!int.TryParse(cell.Element(x + "v")?.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index) || index < 0 || index >= count))
                throw Invalid();
    }

    private string RelationshipSource(string name)
    {
        if (name == "_rels/.rels") return "";
        int marker = name.LastIndexOf("/_rels/", StringComparison.Ordinal);
        if (marker < 0 || !name.EndsWith(".rels", StringComparison.Ordinal)) throw Invalid();
        return name[..(marker + 1)] + name[(marker + 7)..^5];
    }

    private string Resolve(string source, string target)
    {
        if (target.Length == 0 || target.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.'))) throw Unsupported();
        var segments = new List<string>();
        if (!target.StartsWith('/') && source.Contains('/')) segments.AddRange(source[..source.LastIndexOf('/')].Split('/'));
        foreach (var segment in target.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw Invalid();
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        var name = string.Join('/', segments);
        if (!ValidName(name)) throw Invalid();
        return name;
    }

    private void AssertAttributes(XElement element, string[] allowed)
    {
        if (element.Attributes().Any(a => a.IsNamespaceDeclaration && a.Value != element.Name.NamespaceName)) throw Unsupported();
        if (element.Attributes().Any(a => !a.IsNamespaceDeclaration && (a.Name.Namespace != XNamespace.None || !allowed.Contains(a.Name.LocalName, StringComparer.Ordinal)))) throw Unsupported();
        if (element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) throw Invalid();
    }

    private string Required(XElement element, string attribute) => (string?)element.Attribute(attribute) is { Length: > 0 } value ? value : throw Invalid();

    private void WriteXml(ZipArchive zip, string name, XDocument xml, OfficeContext context)
    {
        context.Token.ThrowIfCancellationRequested();
        using var buffer = new OfficeBuffer(context.Limits.MaximumEntryBytes);
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), CloseOutput = false, NewLineHandling = NewLineHandling.Entitize })) xml.Save(writer);
        context.Serialize(buffer.Length);
        using var destination = CreateEntry(zip, name).Open();
        buffer.Position = 0;
        buffer.CopyTo(destination);
    }

    private ZipArchiveEntry CreateEntry(ZipArchive zip, string name)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return entry;
    }

    private void ValidateSdk(byte[] bytes, string format, OfficeContext context)
    {
        context.Token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes, false);
        var settings = new OpenSettings { AutoSave = false, MaxCharactersInPart = context.Limits.MaximumEntryBytes };
        using OpenXmlPackage package = format == "docx" ? WordprocessingDocument.Open(stream, false, settings) : SpreadsheetDocument.Open(stream, false, settings);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019) { MaxNumberOfErrors = 1 };
        if (validator.Validate(package, context.Token).Any()) throw Invalid();
    }

    private RedactionException Invalid() => new(RedactionError.InvalidDocument);
    private RedactionException Unsupported() => new(RedactionError.UnsupportedFormat);
}
