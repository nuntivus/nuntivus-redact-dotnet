using NuntivusRedact.Core;
using NuntivusRedact.Detectors.Nlp;
using NuntivusRedact.Nlp.Onnx;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Nlp.Tests;

public sealed class NlpDetectorTests
{
    private sealed class Provider(Func<string, CancellationToken, ValueTask<IReadOnlyList<RecognizedEntity>>> recognize) : IEntityRecognitionProvider
    {
        public ValueTask<IReadOnlyList<RecognizedEntity>> RecognizeAsync(string text, CancellationToken cancellationToken = default) => recognize(text, cancellationToken);
    }

    private static Provider Returning(params RecognizedEntity[] entities) => new((_, _) => ValueTask.FromResult<IReadOnlyList<RecognizedEntity>>(entities));

    [Fact]
    public async Task MappingIsImmutableAndProviderIsExplicitlyReplaceable()
    {
        var mapping = new Dictionary<string, string> { ["CUSTOM"] = "custom-person" };
        var options = new NlpDetectorOptions(mapping);
        mapping["CUSTOM"] = "changed";
        var detector = new NlpDetector("local", Returning(new RecognizedEntity(new(0, 5), "CUSTOM", 0.8)), options);
        var detection = Assert.Single(await detector.DetectAsync(new("Alice")));
        Assert.Equal("custom-person", detection.Category);
        Assert.Equal("local", detection.DetectorId);
        Assert.Equal(0.8, detection.Confidence);
        Assert.DoesNotContain("Alice", detection.ToString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("null-entity")]
    [InlineData("range")]
    [InlineData("surrogate-start")]
    [InlineData("surrogate-end")]
    [InlineData("unknown-label")]
    public async Task InvalidProviderResultsFailClosed(string failure)
    {
        var entity = failure switch
        {
            "range" => new RecognizedEntity(new(0, 20), "PER", 1),
            "surrogate-start" => new RecognizedEntity(new(1, 1), "PER", 1),
            "surrogate-end" => new RecognizedEntity(new(0, 1), "PER", 1),
            "unknown-label" => new RecognizedEntity(new(2, 5), "UNKNOWN", 1),
            _ => null!
        };
        var provider = failure == "null" ? new Provider((_, _) => ValueTask.FromResult<IReadOnlyList<RecognizedEntity>>(null!)) : Returning(entity);
        var detector = new NlpDetector("nlp", provider);
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await detector.DetectAsync(new("😀Alice")));
        Assert.Equal(RedactionError.InvalidDetection, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task ProviderErrorsAreSanitizedIncludingForeignCancellation()
    {
        foreach (var exception in new Exception[] { new Exception("Alice"), new RedactionException(RedactionError.InvalidDocument), new OperationCanceledException("Alice") })
        {
            var detector = new NlpDetector("nlp", new Provider((_, _) => throw exception));
            var error = await Assert.ThrowsAsync<RedactionException>(async () => await detector.DetectAsync(new("Alice")));
            Assert.Equal(exception is RedactionException ? RedactionError.InvalidDocument : RedactionError.ExtensionFailed, error.Error);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain("Alice", error.ToString());
        }
    }

    [Fact]
    public async Task AdapterBoundsCandidatesBeforePolicyAndHonorsCancellation()
    {
        var provider = Returning(new(new(0, 1), "PER", 1), new(new(1, 1), "PER", 1));
        var detector = new NlpDetector("nlp", provider, new(maximumInputCharacters: 3, maximumEntities: 1));
        Assert.Equal(RedactionError.LimitExceeded, (await Assert.ThrowsAsync<RedactionException>(async () => await detector.DetectAsync(new("abcd")))).Error);
        Assert.Equal(RedactionError.LimitExceeded, (await Assert.ThrowsAsync<RedactionException>(async () => await detector.DetectAsync(new("ab")))).Error);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await detector.DetectAsync(new("ab"), new CancellationToken(true)));
    }

    [Fact]
    public async Task CancellationRequestedByProviderRetainsOnlyCallerToken()
    {
        using var cancellation = new CancellationTokenSource();
        var detector = new NlpDetector("nlp", new Provider((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException("Alice");
        }));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await detector.DetectAsync(new("Alice"), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.DoesNotContain("Alice", error.ToString());
    }

    [Fact]
    public async Task NativeProviderIntegratesWithEnginePolicyAndOriginalCoordinateEdits()
    {
        using var fixture = new OnnxFixture();
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options());
        var policy = new RedactionPolicyBuilder(new CategoryReplacementStrategy())
            .Include(NlpDetectionCategories.Person).Build();
        var engine = new RedactionEngine([new(new NlpDetector("nlp", provider))], policy);
        var result = await engine.RedactWithEditsAsync("😀 Alice Smith works at Contoso.");
        Assert.Equal("😀 [person] works at Contoso.", result.Text);
        var edit = Assert.Single(result.Edits);
        Assert.Equal(new DetectionSpan(3, 11), edit.Detection.Span);
        Assert.Equal("[person]", edit.Replacement);
        Assert.DoesNotContain("Alice", edit.Detection.ToString());
    }

    [Theory]
    [InlineData(0, "[REDACTED]")]
    [InlineData(10, "Alice [REDACTED]")]
    public async Task ExistingOverlapPriorityRemainsExplicitAndCoverageTradeoffIsVisible(int narrowPriority, string expected)
    {
        var broad = new NlpDetector("broad", Returning(new RecognizedEntity(new(0, 11), "PER", 0.9)));
        var narrow = new NlpDetector("narrow", Returning(new RecognizedEntity(new(6, 5), "PER", 0.8)));
        var policy = new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build();
        var engine = new RedactionEngine([new(broad), new(narrow, narrowPriority)], policy);
        Assert.Equal(expected, (await engine.RedactAsync("Alice Smith")).Text);
    }

    [Fact]
    public void EntityAndMappingRejectInvalidConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new RecognizedEntity(default, "PER", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecognizedEntity(new(0, 1), "PER", double.NaN));
        Assert.Throws<ArgumentException>(() => new NlpDetectorOptions(new Dictionary<string, string> { ["PER"] = "" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NlpDetectorOptions(maximumEntities: 0));
    }
}
