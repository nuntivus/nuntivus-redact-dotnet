using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BenchmarkDotNet.Attributes;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Detectors.Regex;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Formats.Xml;
using NuntivusRedact.Strategies;

namespace NuntivusRedact.Benchmarks;
/// <summary>Deterministic text workloads with different registration and candidate counts.</summary>
public enum TextScenario
{
    /// <summary>A 256-character message with one email.</summary>
    Short,
    /// <summary>A 256-Ki-character document with 128 emails.</summary>
    Large,
    /// <summary>Thirty-two distinct dictionary detectors, each matching eight times.</summary>
    ManyDetectors,
    /// <summary>Thirty-two nested/identical detectors at thirty-two locations.</summary>
    Overlaps
}

/// <summary>Measures the real public engine, excluding reusable composition and fixture generation.</summary>
[MemoryDiagnoser]
public class TextBenchmarks
{
    private RedactionEngine engine = null!;
    private string input = null!;
    /// <summary>Gets or sets the deterministic workload.</summary>
    [ParamsAllValues]
    public TextScenario Scenario { get; set; }

    /// <summary>Builds and verifies the workload outside measured invocations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var fixture = SyntheticFixtures.Text(Scenario);
        engine = fixture.Engine;
        input = fixture.Input;
        var result = engine.RedactAsync(input).GetAwaiter().GetResult();
        if (result.Detections.Count != fixture.ExpectedDetections || result.Text != fixture.Expected)
            throw new InvalidOperationException("Synthetic text verification failed.");
    }

    /// <summary>Executes detection, overlap resolution, replacement and source-free reporting.</summary>
    [Benchmark]
    public ValueTask<RedactionResult> Redact() => engine.RedactAsync(input);
}

/// <summary>Measures bounded UTF-8 staging, parsing, redaction, validation and output in memory.</summary>
[MemoryDiagnoser]
public class DocumentBenchmarks
{
    private IDocumentProcessor processor = null!;
    private byte[] input = null!;
    /// <summary>Gets or sets the explicitly selected supported format.</summary>
    [Params("text", "json", "xml")]
    public string Format { get; set; } = "text";

    /// <summary>Gets or sets the bounded record count.</summary>
    [Params(8, 128)]
    public int Records { get; set; }

    /// <summary>Composes public APIs and verifies output structure and removal outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var fixture = SyntheticFixtures.Document(Format, Records);
        processor = fixture.Processor;
        input = fixture.Input;
        using var source = new MemoryStream(input, writable: false);
        using var destination = new MemoryStream();
        var result = processor.ProcessAsync(source, destination).GetAwaiter().GetResult();
        if (result.Detections.Count != Records || !source.CanRead || !destination.CanWrite)
            throw new InvalidOperationException("Synthetic document report verification failed.");
        SyntheticFixtures.VerifyDocument(Format, Records, destination.ToArray());
    }

    /// <summary>Includes fresh stream wrappers/output allocation, excluding fixture encoding and composition.</summary>
    [Benchmark]
    public async ValueTask<int> Process()
    {
        using var source = new MemoryStream(input, writable: false);
        using var destination = new MemoryStream();
        var result = await processor.ProcessAsync(source, destination).ConfigureAwait(false);
        return result.Detections.Count;
    }
}

internal static class SyntheticFixtures
{
    private const string Email = "alice@example.com";
    private const string Replacement = "[REDACTED]";
    private const string Phrase = "ALPHA BRAVO CHARLIE";
    internal static RedactionEngine Engine(IEnumerable<DetectorRegistration> registrations) => new(registrations, new RedactionPolicyBuilder(new FixedReplacementStrategy(Replacement)) { MaximumInputCharacters = 1_000_000, MaximumOutputCharacters = 2_000_000, MaximumDetections = 10_000 }.Build());
    internal static RedactionEngine DefaultEngine() => Engine(PersonalDataDetectors.CreateDefault().Select(detector => new DetectorRegistration(detector)));
    internal static (RedactionEngine Engine, string Input, string Expected, int ExpectedDetections) Text(TextScenario scenario)
    {
        if (scenario is TextScenario.Short or TextScenario.Large)
        {
            int blockLength = scenario == TextScenario.Short ? 256 : 2048;
            int repetitions = scenario == TextScenario.Short ? 1 : 128;
            string block = "Contact " + Email + ". " + new string('x', blockLength - Email.Length - 11) + "\n";
            string text = string.Concat(Enumerable.Repeat(block, repetitions));
            return (DefaultEngine(), text, text.Replace(Email, Replacement, StringComparison.Ordinal), repetitions);
        }

        if (scenario == TextScenario.ManyDetectors)
        {
            var keywords = Enumerable.Range(0, 32).Select(i => $"TOKEN{i:D2}").ToArray();
            var registrations = keywords.Select((keyword, i) => new DetectorRegistration(new DictionaryDetector($"dictionary-{i:D2}", "synthetic", [keyword])));
            string text = string.Concat(Enumerable.Repeat(string.Join(' ', keywords) + "\n", 8));
            string expected = string.Concat(Enumerable.Repeat(string.Join(' ', Enumerable.Repeat(Replacement, 32)) + "\n", 8));
            return (Engine(registrations), text, expected, 256);
        }

        if (scenario == TextScenario.Overlaps)
        {
            string[] nested = [Phrase, "BRAVO CHARLIE", "BRAVO"];
            var registrations = Enumerable.Range(0, 32).Select(i => new DetectorRegistration(new DictionaryDetector($"overlap-{i:D2}", "synthetic", [nested[i % nested.Length]]), 32 - i));
            return (Engine(registrations), string.Concat(Enumerable.Repeat(Phrase + ".\n", 32)), string.Concat(Enumerable.Repeat(Replacement + ".\n", 32)), 32);
        }

        throw new ArgumentOutOfRangeException(nameof(scenario));
    }

    internal static (IDocumentProcessor Processor, byte[] Input) Document(string format, int records)
    {
        if (records is not (8 or 128))
            throw new ArgumentOutOfRangeException(nameof(records));
        var engine = DefaultEngine();
        return format switch
        {
            "text" => (new TextDocumentProcessor(engine), Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Contact " + Email + ".\n", records)))),
            "json" => (new JsonDocumentProcessor(engine), JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, records).Select(_ => new { contact = Email, count = 42, active = true }))),
            "xml" => (new XmlDocumentProcessor(engine), Encoding.UTF8.GetBytes(new XElement("records", Enumerable.Range(0, records).Select(_ => new XElement("record", new XAttribute("kind", "synthetic"), new XElement("contact", Email), new XElement("count", 42)))).ToString(SaveOptions.DisableFormatting))),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    internal static void VerifyDocument(string format, int records, byte[] output)
    {
        string text = Encoding.UTF8.GetString(output);
        bool valid = !text.Contains(Email, StringComparison.Ordinal);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            valid &= document.RootElement.GetArrayLength() == records && document.RootElement.EnumerateArray().All(item => item.GetProperty("contact").GetString() == Replacement && item.GetProperty("count").GetInt32() == 42 && item.GetProperty("active").GetBoolean());
        }
        else if (format == "xml")
        {
            var root = XDocument.Parse(text).Root!;
            valid &= root.Name == "records" && root.Elements().Count() == records && root.Elements().All(item => item.Name == "record" && (string?)item.Attribute("kind") == "synthetic" && (string?)item.Element("contact") == Replacement && (int?)item.Element("count") == 42);
        }
        else
            valid &= text == string.Concat(Enumerable.Repeat("Contact " + Replacement + ".\n", records));
        if (!valid)
            throw new InvalidOperationException("Synthetic document recovery verification failed.");
    }
}
