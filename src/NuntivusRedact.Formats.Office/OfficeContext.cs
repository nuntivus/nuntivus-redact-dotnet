using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NuntivusRedact.Formats.Office;

internal sealed class OfficeContext(IEditAwareTextRedactor redactor, OfficeLimits limits, CancellationToken token, IOfficeString strings)
{
    internal OfficeLimits Limits { get; } = limits;
    internal CancellationToken Token { get; } = token;
    internal List<DocumentDetection> Report { get; } = [];
    internal long InflatedBytes { get; private set; }
    private int entries;
    private int nodes;
    private int fields;
    private int detections;
    private long growth;
    private long serialized;

    internal void Entry()
    {
        Token.ThrowIfCancellationRequested();
        if (++entries > Limits.MaximumEntries) throw new RedactionException(RedactionError.LimitExceeded);
    }

    internal void Inflate(int bytes)
    {
        InflatedBytes += bytes;
        if (InflatedBytes > Limits.MaximumInflatedBytes) throw new RedactionException(RedactionError.LimitExceeded);
    }

    internal XDocument Parse(byte[] bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = Limits.MaximumEntryBytes,
            MaxCharactersFromEntities = 1,
            IgnoreWhitespace = false
        };
        using (var stream = new MemoryStream(bytes, false))
        using (var reader = XmlReader.Create(stream, settings))
        {
            while (reader.Read())
            {
                Token.ThrowIfCancellationRequested();
                if (++nodes > Limits.MaximumXmlNodes || reader.Depth > Limits.MaximumXmlDepth)
                    throw new RedactionException(RedactionError.LimitExceeded);
                if (reader.NodeType is XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
                    throw new RedactionException(RedactionError.UnsupportedFormat);
            }
        }
        using var source = new MemoryStream(bytes, false);
        using var safeReader = XmlReader.Create(source, settings);
        return XDocument.Load(safeReader, LoadOptions.PreserveWhitespace);
    }

    internal async ValueTask<EditRedactionResult> RedactAsync(string text, string? location = null)
    {
        Token.ThrowIfCancellationRequested();
        if (++fields > Limits.MaximumFields || text.Length > Limits.MaximumEntryBytes)
            throw new RedactionException(RedactionError.LimitExceeded);
        EditRedactionResult result;
        try { result = await redactor.RedactWithEditsAsync(text, Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (Token.IsCancellationRequested) { throw; }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
        if (result is null || result.Text is null) throw new RedactionException(RedactionError.InvalidDetection);
        if (result.Text.Length > Limits.MaximumEntryBytes) throw new RedactionException(RedactionError.LimitExceeded);
        growth += Math.Max(0L, (long)(result.Text.Length - text.Length) * 4);
        if (growth + InflatedBytes > Limits.MaximumInflatedBytes) throw new RedactionException(RedactionError.LimitExceeded);
        var rebuilt = new StringBuilder();
        int offset = 0;
        foreach (var edit in result.Edits)
        {
            Token.ThrowIfCancellationRequested();
            var span = edit.Detection.Span;
            if (span.Start < offset || span.End > text.Length || SplitsSurrogate(text, span.Start) || SplitsSurrogate(text, span.End))
                throw new RedactionException(RedactionError.InvalidDetection);
            if (++detections > Limits.MaximumDetections) throw new RedactionException(RedactionError.LimitExceeded);
            Append(rebuilt, text.AsSpan(offset, span.Start - offset));
            Append(rebuilt, edit.Replacement);
            offset = span.End;
        }
        Append(rebuilt, text.AsSpan(offset));
        if (!string.Equals(rebuilt.ToString(), result.Text, StringComparison.Ordinal))
            throw new RedactionException(RedactionError.InvalidDetection);
        XmlConvert.VerifyXmlChars(result.Text);
        if (location is not null)
            Report.AddRange(result.Edits.Select(edit => new DocumentDetection(location, edit.Detection)));
        return result;
    }

    private void Append(StringBuilder builder, ReadOnlySpan<char> text)
    {
        if (text.Length > Limits.MaximumEntryBytes - builder.Length) throw new RedactionException(RedactionError.LimitExceeded);
        builder.Append(text);
    }

    internal async ValueTask AssertUnchangedAsync(string value)
    {
        strings.RejectEscapes(value);
        var result = await RedactAsync(value).ConfigureAwait(false);
        if (result.Edits.Count != 0 || !string.Equals(value, result.Text, StringComparison.Ordinal))
            throw new RedactionException(RedactionError.UnsupportedFormat);
    }

    internal void Serialize(long bytes)
    {
        serialized += bytes;
        if (serialized > Limits.MaximumInflatedBytes) throw new RedactionException(RedactionError.LimitExceeded);
    }

    internal async ValueTask RedactRunsAsync(IReadOnlyList<XElement> runs, string location, HashSet<XObject> handled, bool excel = false)
    {
        if (runs.Count == 0) return;
        var starts = new int[runs.Count];
        var lengths = new int[runs.Count];
        var values = new string[runs.Count];
        var source = new StringBuilder();
        for (int i = 0; i < runs.Count; i++)
        {
            if (!excel) strings.RejectEscapes(runs[i].Value);
            values[i] = excel ? strings.Decode(runs[i].Value) : runs[i].Value;
            starts[i] = source.Length;
            lengths[i] = values[i].Length;
            Append(source, values[i]);
            handled.Add(runs[i]);
        }
        if (excel && source.Length > 32767) throw new RedactionException(RedactionError.LimitExceeded);
        var result = await RedactAsync(source.ToString(), location).ConfigureAwait(false);
        if (!excel) strings.RejectEscapes(result.Text);
        if (excel && result.Text.Length > 32767) throw new RedactionException(RedactionError.LimitExceeded);
        if (result.Edits.Count == 0) return;
        foreach (var edit in result.Edits.Reverse())
        {
            var span = edit.Detection.Span;
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                int start = starts[i];
                int end = start + lengths[i];
                if (span.Start >= end || span.End <= start) continue;
                int from = Math.Max(span.Start, start) - start;
                int count = Math.Min(span.End, end) - start - from;
                var value = values[i].Remove(from, count);
                if (span.Start >= start) value = value.Insert(from, edit.Replacement);
                values[i] = value;
            }
        }
        if (!string.Equals(string.Concat(values), result.Text, StringComparison.Ordinal)) throw new RedactionException(RedactionError.InvalidDetection);
        for (int i = 0; i < runs.Count; i++)
        {
            runs[i].Value = excel ? strings.Encode(values[i]) : values[i];
            runs[i].SetAttributeValue(XNamespace.Xml + "space", "preserve");
        }
    }

    private bool SplitsSurrogate(string value, int offset) => offset > 0 && offset < value.Length && char.IsHighSurrogate(value[offset - 1]) && char.IsLowSurrogate(value[offset]);
}

internal sealed class OfficeBuffer(int maximum) : MemoryStream
{
    private void Check(long end)
    {
        if (end > maximum) throw new RedactionException(RedactionError.LimitExceeded);
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(Position + count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(Position + buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(Position + 1); base.WriteByte(value); }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }
    public override void SetLength(long value) { Check(value); base.SetLength(value); }
}
