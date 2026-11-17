using Xunit;

namespace NuntivusRedact.Benchmarks.Tests;

public sealed class FixtureTests
{
    [Theory]
    [InlineData(TextScenario.Short, 1)]
    [InlineData(TextScenario.Large, 128)]
    [InlineData(TextScenario.ManyDetectors, 256)]
    [InlineData(TextScenario.Overlaps, 32)]
    public async Task TextFixturesValidateExactOutputAndRemainReusable(TextScenario scenario, int expectedDetections)
    {
        var benchmark = new TextBenchmarks { Scenario = scenario };
        benchmark.Setup();
        var first = await benchmark.Redact();
        var second = await benchmark.Redact();
        Assert.Equal(expectedDetections, first.Detections.Count);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Detections, second.Detections);
        Assert.DoesNotContain("alice@example.com", first.Text, StringComparison.Ordinal);
        if (scenario == TextScenario.ManyDetectors)
            Assert.DoesNotContain("TOKEN", first.Text, StringComparison.Ordinal);
        if (scenario == TextScenario.Overlaps)
            Assert.DoesNotContain("BRAVO", first.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("text", 8)]
    [InlineData("text", 128)]
    [InlineData("json", 8)]
    [InlineData("json", 128)]
    [InlineData("xml", 8)]
    [InlineData("xml", 128)]
    public async Task DocumentFixturesValidateRecoveryStructureAndRemainReusable(string format, int records)
    {
        var benchmark = new DocumentBenchmarks { Format = format, Records = records };
        benchmark.Setup();
        Assert.Equal(records, await benchmark.Process());
        Assert.Equal(records, await benchmark.Process());
    }
}
