using NuntivusRedact.Core;
using Xunit;

namespace NuntivusRedact.Core.Tests;

public sealed class EngineTests
{
    private sealed class Replace : IRedactionStrategy
    {
        public string Redact(ReadOnlySpan<char> source, Detection detection) => "X";
    }
    private sealed class Detector(string id, params Detection[] matches) : ISensitiveDataDetector
    {
        public string Id => id;
        public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Detection>>(matches);
    }
    private sealed class LeakyDetector : ISensitiveDataDetector
    {
        public string Id => "bad";
        public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default) => throw new InvalidOperationException(context.Text);
    }
    private static Detection D(int start, int length, string id, string category = "test", double confidence = 1) => new(new(start, length), category, confidence, id);
    private static RedactionPolicyBuilder Policy() => new(new Replace());

    [Fact]
    public async Task PriorityWinsOverLengthAndTransformsOriginalOffsets()
    {
        var engine = new RedactionEngine([new(new Detector("wide", D(0, 5, "wide"))), new(new Detector("narrow", D(1, 1, "narrow"), D(5, 1, "narrow")), 1)], Policy().Build());
        var result = await engine.RedactAsync("abcdef");
        Assert.Equal("aXcdeX", result.Text);
        Assert.Equal(2, result.Detections.Count);
    }
    [Fact]
    public async Task FilteringPrecedesOverlapAndPolicyIsASnapshot()
    {
        var builder = Policy().Exclude("excluded");
        var policy = builder.Build();
        builder.Exclude("test");
        var engine = new RedactionEngine([new(new Detector("a", D(0, 3, "a", "excluded"), D(1, 1, "a")))], policy);
        Assert.Equal("aXc", (await engine.RedactAsync("abc")).Text);
    }
    [Fact]
    public async Task TiesAreDeterministicRegardlessOfRegistrationOrder()
    {
        var a = new DetectorRegistration(new Detector("a", D(0, 2, "a")));
        var b = new DetectorRegistration(new Detector("b", D(0, 2, "b")));
        foreach (var registrations in new[] { new[] { a, b }, new[] { b, a } })
            Assert.Equal("a", Assert.Single((await new RedactionEngine(registrations, Policy().Build()).RedactAsync("abc")).Detections).DetectorId);
    }
    [Fact]
    public async Task ExtensionFailuresNeverExposeSourceOrInnerException()
    {
        var engine = new RedactionEngine([new(new LeakyDetector())], Policy().Build());
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await engine.RedactAsync("PRIVATE VALUE"));
        Assert.DoesNotContain("PRIVATE VALUE", error.ToString());
        Assert.Null(error.InnerException);
    }
    [Fact]
    public async Task InvalidRangesAndSurrogateSplitsAreRejected()
    {
        foreach (var match in new[] { D(0, 10, "a"), D(1, 1, "a") })
        {
            var engine = new RedactionEngine([new(new Detector("a", match))], Policy().Build());
            var error = await Assert.ThrowsAsync<RedactionException>(async () => await engine.RedactAsync("\U0001F600"));
            Assert.Equal(RedactionError.InvalidDetection, error.Error);
        }
    }
    [Fact]
    public async Task InputOutputAndCandidateBudgetsFailClosed()
    {
        var builder = Policy();
        builder.MaximumInputCharacters = 1;
        await Assert.ThrowsAsync<RedactionException>(async () => await new RedactionEngine([], builder.Build()).RedactAsync("ab"));
        builder.MaximumInputCharacters = 10;
        builder.MaximumOutputCharacters = 1;
        await Assert.ThrowsAsync<RedactionException>(async () => await new RedactionEngine([], builder.Build()).RedactAsync("ab"));
        builder.MaximumOutputCharacters = 10;
        builder.MaximumDetections = 1;
        await Assert.ThrowsAsync<RedactionException>(async () => await new RedactionEngine([new(new Detector("a", D(0, 1, "a"), D(1, 1, "a")))], builder.Build()).RedactAsync("ab"));
    }
    [Fact]
    public async Task CancelledRequestsFailAndConcurrentReuseIsSafe()
    {
        var engine = new RedactionEngine([new(new Detector("a", D(0, 1, "a")))], Policy().Build());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.RedactAsync("abc", new CancellationToken(true)));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(async _ => await engine.RedactAsync("abc")));
        Assert.All(results, result => Assert.Equal("Xbc", result.Text));
    }
    [Fact]
    public void DuplicateIdsAndInvalidPolicyAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new RedactionEngine([new(new Detector("a")), new(new Detector("a"))], Policy().Build()));
        var builder = Policy();
        builder.MinimumConfidence = double.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
    }
    [Fact]
    public async Task CustomRulesConfidenceAndPriorityOverridesWork()
    {
        var builder = Policy().Where(d => d.Category != "ignored").WithPriority("b", 10);
        builder.MinimumConfidence = 0.5;
        var engine = new RedactionEngine([new(new Detector("a", D(0, 3, "a"), D(3, 1, "a", confidence: 0.4))), new(new Detector("b", D(1, 1, "b", confidence: 0.5)))], builder.Build());
        Assert.Equal("aXcd", (await engine.RedactAsync("abcd")).Text);
    }
}
