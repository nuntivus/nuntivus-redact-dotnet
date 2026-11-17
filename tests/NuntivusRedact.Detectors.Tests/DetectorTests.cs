using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Detectors.Regex;
using Xunit;

namespace NuntivusRedact.Detectors.Tests;

public sealed class DetectorTests
{
    [Theory]
    [InlineData("email", "Write to alice@example.com.", "alice@example.com")]
    [InlineData("phone", "Call +1 (202) 555-0199!", "+1 (202) 555-0199")]
    [InlineData("ipv4", "Address 192.0.2.1 here", "192.0.2.1")]
    [InlineData("ipv6", "Address 2001:db8::1 here", "2001:db8::1")]
    [InlineData("ipv6", "Address ::1 here", "::1")]
    [InlineData("iban", "Pay GB82WEST12345698765432 now", "GB82WEST12345698765432")]
    [InlineData("url", "Visit https://example.com/a?q=b now", "https://example.com/a?q=b")]
    [InlineData("credit-card", "Card: 4111-1111-1111-1111!", "4111-1111-1111-1111")]
    [InlineData("credit-card", "Card 4111111111111111 now", "4111111111111111")]
    public async Task ValidCandidatesHaveOriginalCoordinates(string id, string input, string expected)
    {
        var detector = PersonalDataDetectors.CreateDefault().Single(d => d.Id == id);
        var detection = Assert.Single(await detector.DetectAsync(new(input)));
        Assert.Equal(expected, input.Substring(detection.Span.Start, detection.Span.Length));
    }
    [Theory]
    [InlineData("ipv4", "999.2.3.4")]
    [InlineData("ipv6", "abcd:xyz")]
    [InlineData("iban", "GB81WEST12345698765432")]
    [InlineData("credit-card", "4111111111111112")]
    [InlineData("credit-card", "0000000000000000")]
    [InlineData("email", "not an email")]
    public async Task InvalidCandidatesAreRejected(string id, string input) =>
        Assert.Empty(await PersonalDataDetectors.CreateDefault().Single(d => d.Id == id).DetectAsync(new(input)));

    [Fact]
    public async Task DictionaryCopiesConfigurationAndHonorsBoundaries()
    {
        string[] words = ["secret", "secret"];
        var detector = new DictionaryDetector("words", "custom", words);
        words[0] = "other";
        var results = await detector.DetectAsync(new("SECRET secretive secret"));
        Assert.Equal(2, results.Count);
    }
    [Fact]
    public async Task RegexTimeoutNeverLeaksInput()
    {
        var detector = new RegexDetector("evil", "custom", "(a+)+$", timeout: TimeSpan.FromMilliseconds(1));
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await detector.DetectAsync(new(new string('a', 100_000) + "!")));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("aaaa", error.ToString());
    }
    [Fact]
    public async Task LimitsZeroLengthAndCancellationFailClosed()
    {
        await Assert.ThrowsAsync<RedactionException>(async () => await new RegexDetector("r", "c", "x", maximumInputCharacters: 1).DetectAsync(new("xx")));
        await Assert.ThrowsAsync<RedactionException>(async () => await new RegexDetector("r", "c", "x", maximumMatches: 1).DetectAsync(new("xx")));
        await Assert.ThrowsAsync<RedactionException>(async () => await new RegexDetector("r", "c", "^").DetectAsync(new("x")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await PersonalDataDetectors.Email().DetectAsync(new("a@b.com"), new CancellationToken(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegexDetector("r", "c", "x", timeout: Timeout.InfiniteTimeSpan));
    }
    [Fact]
    public async Task ValidatorFailuresAreSanitized()
    {
        var detector = new RegexDetector("r", "c", "x", validator: value => throw new Exception(value));
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await detector.DetectAsync(new("x")));
        Assert.Equal(RedactionError.ExtensionFailed, error.Error);
        Assert.Null(error.InnerException);
    }
}
