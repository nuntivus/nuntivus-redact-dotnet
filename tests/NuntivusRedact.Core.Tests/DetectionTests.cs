using NuntivusRedact;
using Xunit;

namespace NuntivusRedact.Core.Tests;

public sealed class DetectionTests
{
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(int.MaxValue, 1)]
    public void InvalidRangesAreRejected(int start, int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectionSpan(start, length));

    [Fact]
    public void AdjacentSpansDoNotOverlap()
    {
        var span = new DetectionSpan(1, 2);
        Assert.False(span.Overlaps(new DetectionSpan(3, 1)));
        Assert.True(span.Overlaps(new DetectionSpan(2, 2)));
    }

    [Fact]
    public void DefaultSpanAndNanConfidenceAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Detection(default, "test", 1, "id"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Detection(new(0, 1), "test", double.NaN, "id"));
    }
}
