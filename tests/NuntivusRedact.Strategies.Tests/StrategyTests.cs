using NuntivusRedact.Core;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Strategies.Tests;

public sealed class StrategyTests
{
    private static Detection Match(string text, string category = "test") => new(new(0, text.Length), category, 1, "id");
    [Fact]
    public void ReplacementsAndMasksHaveExplicitSemantics()
    {
        Assert.Equal("[REDACTED]", new FixedReplacementStrategy().Redact("secret", Match("secret")));
        Assert.Equal("[test]", new CategoryReplacementStrategy().Redact("secret", Match("secret")));
        Assert.Equal("**", new FullMaskingStrategy().Redact("\U0001F600e\u0301", Match("\U0001F600e\u0301")));
        Assert.Equal("ab**ef", new PartialMaskingStrategy(2, 2).Redact("abcdef", Match("abcdef")));
        Assert.Equal("**", new PartialMaskingStrategy(2, 2).Redact("ab", Match("ab")));
        Assert.Equal("******", new PartialMaskingStrategy(0, 0).Redact("abcdef", Match("abcdef")));
    }
    [Fact]
    public void PseudonymsAreKeyedCategorySeparatedAndDefensivelyCopied()
    {
        var key = Enumerable.Repeat((byte)1, 32).ToArray();
        var strategy = new HmacPseudonymizationStrategy(key);
        var first = strategy.Redact("secret", Match("secret"));
        key[0] = 2;
        Assert.Equal(first, strategy.Redact("secret", Match("secret")));
        Assert.NotEqual(first, new HmacPseudonymizationStrategy(key).Redact("secret", Match("secret")));
        Assert.NotEqual(first, strategy.Redact("secret", Match("secret", "other")));
        Assert.DoesNotContain("secret", first);
        Assert.Throws<ArgumentException>(() => new HmacPseudonymizationStrategy(new byte[8]));
    }
    [Fact]
    public async Task PolicySelectsStrategiesAndDoesNotRescanGeneratedOutput()
    {
        var policy = new RedactionPolicyBuilder(new FixedReplacementStrategy("secret"))
            .WithStrategy("special", new CategoryReplacementStrategy()).Build();
        var engine = new RedactionEngine([new(new TestDetector())], policy);
        Assert.Equal("secret[special]", (await engine.RedactAsync("ab")).Text);
    }
    private sealed class TestDetector : ISensitiveDataDetector
    {
        public string Id => "id";
        public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Detection>>([new(new(0, 1), "test", 1, Id), new(new(1, 1), "special", 1, Id)]);
    }
}
