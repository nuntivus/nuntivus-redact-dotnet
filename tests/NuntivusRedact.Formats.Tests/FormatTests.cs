using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Formats.Csv;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Formats.Xml;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Formats.Tests;

public sealed class FormatTests
{
    private static ITextRedactor Engine() => new RedactionEngine([new(PersonalDataDetectors.Email()), new(PersonalDataDetectors.CreditCard())], new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build());
    private static async Task<(string Text, DocumentRedactionResult Report)> Process(IDocumentProcessor processor, string text)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var output = new MemoryStream();
        var report = await processor.ProcessAsync(input, output);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        return (Encoding.UTF8.GetString(output.ToArray()), report);
    }
    [Fact]
    public async Task TextReportsOriginalOffsetsWithoutSensitiveValues()
    {
        var result = await Process(new TextDocumentProcessor(Engine()), "Hi alice@example.com!");
        Assert.Equal("Hi [REDACTED]!", result.Text);
        Assert.Equal("/", Assert.Single(result.Report.Detections).Location);
        Assert.DoesNotContain("alice", result.Report.Detections[0].ToString());
    }
    [Fact]
    public async Task CsvHandlesQuotesMultilineHeadersAndEmptyCells()
    {
        var result = await Process(new CsvDocumentProcessor(Engine()), "email,note\r\nalice@example.com,\"hello,\n\"\"world\"\"\"\r\n,");
        Assert.Equal("\"email\",\"note\"\r\n\"[REDACTED]\",\"hello,\n\"\"world\"\"\"\r\n\"\",\"\"", result.Text);
        Assert.Equal("/rows/1/cells/0", Assert.Single(result.Report.Detections).Location);
    }
    [Theory]
    [InlineData("", "")]
    [InlineData("\n", "\"\"")]
    [InlineData("\r\n\r\n", "\"\"\r\n\"\"")]
    [InlineData(",", "\"\",\"\"")]
    [InlineData("a,\r\n", "\"a\",\"\"")]
    [InlineData("a\nb\rc\r\nd", "\"a\"\r\n\"b\"\r\n\"c\"\r\n\"d\"")]
    [InlineData("a,b\n\nc\nd,e,f\n", "\"a\",\"b\"\r\n\"\"\r\n\"c\"\r\n\"d\",\"e\",\"f\"")]
    [InlineData("  a ,\" b \"", "\"  a \",\" b \"")]
    [InlineData("#data,=1+1", "\"#data\",\"=1+1\"")]
    [InlineData("\"a\r\nb\rc\nd\",\"\"\"\"", "\"a\r\nb\rc\nd\",\"\"\"\"")]
    [InlineData("雪,😀;value", "\"雪\",\"😀;value\"")]
    public async Task CsvPreservesStrictProfile(string input, string expected)
    {
        var result = await Process(new CsvDocumentProcessor(Engine()), input);
        Assert.Equal(expected, result.Text);
        Assert.Empty(result.Report.Detections);
    }
    [Fact]
    public async Task CsvEscapesReplacementValuesAndDoesNotCreateAdditionalCells()
    {
        const string replacement = "hidden,\"value\"\r\nnext";
        var engine = new RedactionEngine([new(PersonalDataDetectors.Email())], new RedactionPolicyBuilder(new FixedReplacementStrategy(replacement)).Build());
        var result = await Process(new CsvDocumentProcessor(engine), "alice@example.com,safe");
        Assert.Equal("\"hidden,\"\"value\"\"\r\nnext\",\"safe\"", result.Text);
        Assert.Equal("/rows/0/cells/0", Assert.Single(result.Report.Detections).Location);
        var repeated = await Process(new CsvDocumentProcessor(Engine()), result.Text);
        Assert.Equal(result.Text, repeated.Text);
        Assert.Empty(repeated.Report.Detections);
    }
    [Fact]
    public async Task CsvEnforcesFieldAndEncodedOutputBudgets()
    {
        var exact = await Process(new CsvDocumentProcessor(Engine(), new() { MaximumFields = 2, MaximumOutputBytes = 7 }), "a,b");
        Assert.Equal("\"a\",\"b\"", exact.Text);
        await FailsWithoutOutput(new CsvDocumentProcessor(Engine(), new() { MaximumFields = 1 }), "\n\n");
        await FailsWithoutOutput(new CsvDocumentProcessor(Engine(), new() { MaximumOutputBytes = 6 }), "a,b");
        await FailsWithoutOutput(new CsvDocumentProcessor(Engine(), new() { MaximumOutputBytes = 4 }), "雪");
        var unicode = await Process(new CsvDocumentProcessor(Engine(), new() { MaximumOutputBytes = 5 }), "雪");
        Assert.Equal("\"雪\"", unicode.Text);
    }
    [Fact]
    public async Task CsvCanBeReusedConcurrentlyAndAfterFailure()
    {
        var processor = new CsvDocumentProcessor(Engine());
        await FailsWithoutOutput(processor, "alice@example.com,\"unterminated");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Process(processor, "\nalice@example.com,\"safe\"")));
        Assert.All(results, result =>
        {
            Assert.Equal("\"\"\r\n\"[REDACTED]\",\"safe\"", result.Text);
            Assert.Equal("/rows/1/cells/0", Assert.Single(result.Report.Detections).Location);
        });
    }
    [Fact]
    public async Task CsvHonorsCancellationWithoutCommittingOutput()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("alice@example.com,safe"));
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new CsvDocumentProcessor(Engine()).ProcessAsync(input, output, new CancellationToken(true)));
        Assert.Equal(0, output.Length);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }
    [Fact]
    public async Task JsonProcessesKeysAndValuesAndPreservesNumbersAndBooleans()
    {
        var result = await Process(new JsonDocumentProcessor(Engine()), "{\"alice@example.com\":[\"bob@example.com\",42,true,null]}");
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal("[REDACTED]", doc.RootElement.GetProperty("[REDACTED]")[0].GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("[REDACTED]")[1].GetInt32());
        Assert.Equal(2, result.Report.Detections.Count);
        Assert.All(result.Report.Detections, d => Assert.DoesNotContain("alice", d.Location));
    }
    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"a@example.com\":1,\"b@example.com\":2}")]
    [InlineData("{\"card\":4111111111111111}")]
    [InlineData("{bad}")]
    public async Task JsonRejectsAmbiguousAndUnsafeNumericValues(string text) => await FailsWithoutOutput(new JsonDocumentProcessor(Engine()), text);
    [Fact]
    public async Task XmlProcessesTextAttributesCdataAndComments()
    {
        var result = await Process(new XmlDocumentProcessor(Engine()), "<root a=\"alice@example.com\"><value><![CDATA[bob@example.com]]></value><!--c@example.com--><child>safe</child></root>");
        var doc = XDocument.Parse(result.Text);
        Assert.Equal("[REDACTED]", doc.Root!.Attribute("a")!.Value);
        Assert.Equal(3, result.Report.Detections.Count);
        Assert.DoesNotContain("example.com", result.Text);
    }
    [Theory]
    [InlineData("<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///secret'>]><root>&x;</root>")]
    [InlineData("<root>alice@<b>example.com</b></root>")]
    [InlineData("<?secret alice@example.com?><root/>")]
    [InlineData("<root xmlns=\"https://alice@example.com\"/>")]
    [InlineData("<root>")]
    public async Task XmlRejectsEntitiesProcessingInstructionsAndSensitiveNamespaces(string text) => await FailsWithoutOutput(new XmlDocumentProcessor(Engine()), text);
    [Theory]
    [InlineData("a,\"unterminated")]
    [InlineData("a,ba\"d")]
    [InlineData("\"closed\"junk")]
    [InlineData("\"closed\" ")]
    [InlineData(" \"quoted\"")]
    [InlineData("\"odd\"\"")]
    [InlineData("\"")]
    [InlineData("alice@example.com,\"unterminated")]
    public async Task CsvRejectsMalformedInput(string text) => await FailsWithoutOutput(new CsvDocumentProcessor(Engine()), text);
    [Fact]
    public async Task BudgetsDepthCancellationAndInvalidUtf8FailClosed()
    {
        await FailsWithoutOutput(new TextDocumentProcessor(Engine(), new() { MaximumInputBytes = 2 }), "long");
        await FailsWithoutOutput(new TextDocumentProcessor(Engine(), new() { MaximumOutputBytes = 2 }), "long");
        await FailsWithoutOutput(new JsonDocumentProcessor(Engine(), new() { MaximumDepth = 2 }), "[[[1]]]");
        await FailsWithoutOutput(new XmlDocumentProcessor(Engine(), new() { MaximumDepth = 1 }), "<a><b><c/></b></a>");
        await FailsWithoutOutput(new CsvDocumentProcessor(Engine(), new() { MaximumFields = 1 }), "a,b");
        using var input = new MemoryStream([0xff, 0xfe, 0xff]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<RedactionException>(async () => await new TextDocumentProcessor(Engine()).ProcessAsync(input, output));
        Assert.Equal(0, output.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new TextDocumentProcessor(Engine()).ProcessAsync(input, output, new CancellationToken(true)));
    }
    [Fact]
    public void RegistryRejectsUnsupportedFormats()
    {
        var registry = new DocumentProcessorRegistry([new TextDocumentProcessor(Engine())]);
        Assert.Equal("text", registry.Get("TEXT").Format);
        Assert.Throws<RedactionException>(() => registry.Get("pdf"));
    }
    [Fact]
    public async Task XmlCoalescesTextAndCdataBeforeDetection()
    {
        var result = await Process(new XmlDocumentProcessor(Engine()), "<root>alice@<![CDATA[example.com]]></root>");
        Assert.Equal("[REDACTED]", XDocument.Parse(result.Text).Root!.Value);
        Assert.Single(result.Report.Detections);
    }
    [Fact]
    public async Task NonemptyOutputIsRejectedRatherThanLeavingSensitiveTrailingBytes()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("safe"));
        using var output = new MemoryStream(Encoding.UTF8.GetBytes("old sensitive content"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await new TextDocumentProcessor(Engine()).ProcessAsync(input, output));
    }
    private static async Task FailsWithoutOutput(IDocumentProcessor processor, string text)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await processor.ProcessAsync(input, output));
        Assert.Equal(0, output.Length);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("example.com", error.ToString());
    }
}
