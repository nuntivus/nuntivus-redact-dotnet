using System.Text;
using System.Xml.Linq;
using NuntivusRedact.Formats.Office;
using NuntivusRedact.Formats.Tests;
using Xunit;

namespace NuntivusRedact.Security.Tests;

public sealed class OfficeSecurityTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static async Task Fails(byte[] bytes, OfficeLimits? limits = null, RedactionError? expected = null, bool excel = false)
    {
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        IDocumentProcessor processor = excel ? new XlsxDocumentProcessor(OfficeFixtures.Engine(), limits) : new DocxDocumentProcessor(OfficeFixtures.Engine(), limits);
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await processor.ProcessAsync(input, output));
        if (expected is not null) Assert.Equal(expected, error.Error);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("alice", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(output.ToArray());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData("../document.xml")]
    [InlineData("/document.xml")]
    [InlineData("word\\document.xml")]
    [InlineData("word/%64ocument.xml")]
    [InlineData("word/./document.xml")]
    [InlineData("word/document.xml.")]
    public async Task AmbiguousArchiveNamesAreRejected(string name)
    {
        await Fails(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => entries.Add(name, [1, 2, 3])), expected: RedactionError.InvalidDocument);
    }

    [Fact]
    public async Task DuplicateAndCaseAliasedEntriesAreRejected()
    {
        var entries = OfficeFixtures.Entries(OfficeFixtures.Word()).ToList();
        entries.Add(entries.First(e => e.Key == "word/document.xml"));
        await Fails(OfficeFixtures.Zip(entries), expected: RedactionError.InvalidDocument);
        entries[^1] = new("WORD/DOCUMENT.XML", entries[^1].Value);
        await Fails(OfficeFixtures.Zip(entries), expected: RedactionError.InvalidDocument);
    }

    [Theory]
    [InlineData("External", "https://example.com/alice@example.com")]
    [InlineData("Internal", "../../outside.xml")]
    [InlineData("Internal", "missing.xml")]
    public async Task ExternalEscapingAndDanglingRelationshipsFail(string mode, string target)
    {
        await Fails(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "_rels/.rels", xml =>
        {
            var rel = xml.Root!.Elements().First();
            rel.SetAttributeValue("TargetMode", mode);
            rel.SetAttributeValue("Target", target);
        })));
    }

    [Fact]
    public async Task UnknownAndOrphanPartsAreNotCopied()
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries =>
        {
            entries.Add("word/orphan.xml", entries["word/document.xml"]);
        });
        await Fails(bytes, expected: RedactionError.UnsupportedFormat);
        await Fails(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => entries.Add("word/media/image.png", [1, 2, 3])));
    }

    [Theory]
    [InlineData("comment")]
    [InlineData("instruction")]
    [InlineData("dtd")]
    [InlineData("unknown-namespace")]
    public async Task AlternateXmlSurfacesAreRejected(string feature)
    {
        var bytes = OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries =>
        {
            var xml = XDocument.Parse(Encoding.UTF8.GetString(entries["word/document.xml"]));
            if (feature == "comment") xml.Root!.AddFirst(new XComment("alice@example.com"));
            if (feature == "instruction") xml.Root!.AddFirst(new XProcessingInstruction("private", "alice@example.com"));
            if (feature == "unknown-namespace") xml.Root!.SetAttributeValue(XNamespace.Xmlns + "private", "alice@example.com");
            var raw = xml.ToString(SaveOptions.DisableFormatting);
            if (feature == "dtd") raw = "<!DOCTYPE document [<!ENTITY private SYSTEM 'file:///private'>]>" + raw;
            entries["word/document.xml"] = Encoding.UTF8.GetBytes(raw);
        });
        await Fails(bytes);
    }

    [Theory]
    [InlineData("_rels/.rels")]
    [InlineData("[Content_Types].xml")]
    public async Task ManifestNamespaceMetadataCannotBypassInspection(string part)
    {
        await Fails(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, part,
            xml => xml.Root!.SetAttributeValue(XNamespace.Xmlns + "private", "alice@example.com"))), expected: RedactionError.UnsupportedFormat);
    }

    [Fact]
    public async Task SensitiveStructuralNamesAreRejected()
    {
        await Fails(OfficeFixtures.Rewrite(OfficeFixtures.Word(), entries => OfficeFixtures.EditXml(entries, "word/document.xml", xml =>
            xml.Descendants(W + "rPr").First().AddFirst(new XElement(W + "rStyle", new XAttribute(W + "val", "alice@example.com"))))), expected: RedactionError.UnsupportedFormat);
    }

    [Fact]
    public async Task EveryResourceBudgetFailsBeforeCommit()
    {
        var bytes = OfficeFixtures.Word();
        var limits = new OfficeLimits[]
        {
            new() { MaximumInputBytes = 1 }, new() { MaximumOutputBytes = 1 }, new() { MaximumEntryBytes = 1 },
            new() { MaximumInflatedBytes = 1 }, new() { MaximumEntries = 1 }, new() { MaximumCompressionRatio = 1 },
            new() { MaximumXmlDepth = 1 }, new() { MaximumXmlNodes = 1 }, new() { MaximumFields = 1 }
        };
        foreach (var limit in limits) await Fails(bytes, limit, RedactionError.LimitExceeded);
        await Fails(OfficeFixtures.Word(true), new() { MaximumDetections = 1 }, RedactionError.LimitExceeded);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocxDocumentProcessor(OfficeFixtures.Engine(), new() { MaximumXmlDepth = 257 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XlsxDocumentProcessor(OfficeFixtures.Engine(), new() { MaximumNestingDepth = -1 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedSupportedPackagesAreRedactedAndShareBudgets(bool excel)
    {
        byte[] child = excel ? OfficeFixtures.Excel() : OfficeFixtures.Word(true);
        var bytes = OfficeFixtures.Embed(OfficeFixtures.Word(), child, excel);
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var report = await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output);
        var outer = OfficeFixtures.Entries(output.ToArray());
        var embedded = outer.Single(e => e.Key.Contains("embeddings/", StringComparison.Ordinal));
        Assert.All(OfficeFixtures.Entries(embedded.Value).Values, value => Assert.DoesNotContain("alice", Encoding.UTF8.GetString(value), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Detections, d => d.Location.Count(c => c == '/') > 8);
        await Fails(bytes, new() { MaximumNestingDepth = 0 }, RedactionError.LimitExceeded);
        await Fails(bytes, new() { MaximumDetections = 1 }, RedactionError.LimitExceeded);
        await Fails(OfficeFixtures.Embed(OfficeFixtures.Word(), [1, 2, 3]));
    }

    [Fact]
    public async Task MalformedZipAndWrongFormatDoNotFallBackToText()
    {
        await Fails(Encoding.UTF8.GetBytes("alice@example.com"));
        await Fails(OfficeFixtures.Word()[..64]);
        await Fails(OfficeFixtures.Excel());
        await Fails(OfficeFixtures.Word(), excel: true);
    }

    [Fact]
    public async Task NonseekableInputOutputAndConcurrentReuseAreDeterministic()
    {
        var bytes = OfficeFixtures.Word(true);
        var processor = new DocxDocumentProcessor(OfficeFixtures.Engine());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var inputBytes = new MemoryStream(bytes);
            using var outputBytes = new MemoryStream();
            using var input = new Nonseekable(inputBytes);
            using var output = new Nonseekable(outputBytes);
            var report = await processor.ProcessAsync(input, output);
            Assert.True(inputBytes.CanRead);
            Assert.True(outputBytes.CanWrite);
            return (Bytes: outputBytes.ToArray(), Count: report.Detections.Count);
        }));
        Assert.All(results, result => { Assert.Equal(results[0].Bytes, result.Bytes); Assert.Equal(results[0].Count, result.Count); });
    }

    [Fact]
    public async Task PrecancelAndMidflightCancelLeaveNoOutput()
    {
        using var input = new MemoryStream(OfficeFixtures.Word());
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, output, new(true)));
        Assert.Empty(output.ToArray());
        using var source = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new DocxDocumentProcessor(new CancelRedactor(source)).ProcessAsync(input, output, source.Token));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task ExtensionAndOutputFailuresAreSanitizedAndNeverReportSuccess()
    {
        using var input = new MemoryStream(OfficeFixtures.Word());
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(new LeakyRedactor()).ProcessAsync(input, output));
        Assert.Equal(RedactionError.ExtensionFailed, error.Error);
        Assert.DoesNotContain("alice", error.ToString());
        Assert.Empty(output.ToArray());
        input.Position = 0;
        using var broken = new BrokenOutput();
        error = await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(OfficeFixtures.Engine()).ProcessAsync(input, broken));
        Assert.DoesNotContain("alice", error.ToString());
        Assert.Null(error.InnerException);
        Assert.NotEmpty(broken.ToArray());
    }

    [Fact]
    public async Task InvalidCustomEditOutputCannotSilentlyFlattenOrCorruptText()
    {
        using var input = new MemoryStream(OfficeFixtures.Word());
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new DocxDocumentProcessor(new InconsistentRedactor()).ProcessAsync(input, output));
        Assert.Equal(RedactionError.InvalidDetection, error.Error);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task NonemptyAndAliasedDestinationsAreRejectedWithoutModification()
    {
        using var input = new MemoryStream(OfficeFixtures.Word());
        using var output = new MemoryStream([1]);
        var processor = new DocxDocumentProcessor(OfficeFixtures.Engine());
        await Assert.ThrowsAsync<ArgumentException>(async () => await processor.ProcessAsync(input, output));
        Assert.Equal(new byte[] { 1 }, output.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(async () => await processor.ProcessAsync(input, input));
    }

    private sealed class LeakyRedactor : IEditAwareTextRedactor
    {
        public ValueTask<EditRedactionResult> RedactWithEditsAsync(string text, CancellationToken cancellationToken = default) => throw new InvalidOperationException("alice@example.com " + text);
        public ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class InconsistentRedactor : IEditAwareTextRedactor
    {
        public ValueTask<EditRedactionResult> RedactWithEditsAsync(string text, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EditRedactionResult("different", []));
        public ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class CancelRedactor(CancellationTokenSource source) : IEditAwareTextRedactor
    {
        public ValueTask<EditRedactionResult> RedactWithEditsAsync(string text, CancellationToken cancellationToken = default) { source.Cancel(); cancellationToken.ThrowIfCancellationRequested(); throw new InvalidOperationException(); }
        public ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class BrokenOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            base.Write(buffer.Span[..1]);
            throw new IOException("alice@example.com");
        }
    }
    private sealed class Nonseekable(Stream stream) : Stream
    {
        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => stream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => stream.ReadAsync(buffer, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => stream.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
