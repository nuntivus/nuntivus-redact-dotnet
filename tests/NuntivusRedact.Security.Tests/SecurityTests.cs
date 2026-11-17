using System.Text;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Formats.Xml;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Security.Tests;

public sealed class SecurityTests
{
    private static ITextRedactor Engine(IRedactionStrategy? strategy = null) => new RedactionEngine([new(PersonalDataDetectors.Email())], new RedactionPolicyBuilder(strategy ?? new FixedReplacementStrategy()).Build());
    private sealed class LeakyStrategy : IRedactionStrategy
    {
        public string Redact(ReadOnlySpan<char> source, Detection detection) => throw new Exception(source.ToString());
    }
    [Fact]
    public async Task StrategyExceptionsCannotLeakSourceOrWritePartialOutput()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"first\":\"safe\",\"secret\":\"alice@example.com\"}"));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new JsonDocumentProcessor(Engine(new LeakyStrategy())).ProcessAsync(input, output));
        Assert.Equal(0, output.Length);
        Assert.DoesNotContain("alice", error.ToString());
        Assert.Null(error.InnerException);
    }
    [Fact]
    public async Task OutputReparseCannotRecoverAcceptedMatchesFromSupportedJsonSurface()
    {
        const string inputText = "{\"a\\u006cice@example.com\":\"bob\\u0040example.com\"}";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(inputText));
        using var output = new MemoryStream();
        var report = await new JsonDocumentProcessor(Engine()).ProcessAsync(input, output);
        Assert.Equal(2, report.Detections.Count);
        using var parsed = System.Text.Json.JsonDocument.Parse(output.ToArray());
        var property = Assert.Single(parsed.RootElement.EnumerateObject());
        Assert.Equal("[REDACTED]", property.Name);
        Assert.Equal("[REDACTED]", property.Value.GetString());
        Assert.DoesNotContain("example.com", Encoding.UTF8.GetString(output.ToArray()));
    }
    [Fact]
    public async Task XmlEntityExpansionFailsWithoutOutput()
    {
        const string xml = "<!DOCTYPE r [<!ENTITY a 'alice@example.com'><!ENTITY b '&a;&a;&a;'>]><r>&b;</r>";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<RedactionException>(async () => await new XmlDocumentProcessor(Engine()).ProcessAsync(input, output));
        Assert.Equal(0, output.Length);
    }
    [Fact]
    public async Task SharedProcessorSupportsConcurrentRequestsAndBoundsReports()
    {
        var processor = new TextDocumentProcessor(Engine());
        await Task.WhenAll(Enumerable.Range(0, 64).Select(async _ =>
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("alice@example.com"));
            using var output = new MemoryStream();
            Assert.Single((await processor.ProcessAsync(input, output)).Detections);
            Assert.Equal("[REDACTED]", Encoding.UTF8.GetString(output.ToArray()));
        }));
        using var many = new MemoryStream(Encoding.UTF8.GetBytes("alice@example.com bob@example.com"));
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<RedactionException>(async () => await new TextDocumentProcessor(Engine(), new() { MaximumDetections = 1 }).ProcessAsync(many, destination));
        Assert.Equal(0, destination.Length);
    }
    private sealed class BrokenOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("sensitive I/O detail");
    }
    [Fact]
    public async Task OutputIoFailuresAreSanitizedAndNeverReportedAsSuccess()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("alice@example.com"));
        using var output = new BrokenOutput();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new TextDocumentProcessor(Engine()).ProcessAsync(input, output));
        Assert.DoesNotContain("sensitive I/O detail", error.ToString());
        Assert.Null(error.InnerException);
    }
}
