using System.Text;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.Regex;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Security.Tests;

public sealed class JsonBudgetTests
{
    [Theory]
    [InlineData("[true]", "true")]
    [InlineData("[false]", "false")]
    [InlineData("[null]", "null")]
    public async Task SensitivePrimitiveTokensCannotBeSilentlyRetained(string document, string keyword)
    {
        var engine = new RedactionEngine([new(new DictionaryDetector("keywords", "custom", [keyword]))], new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build());
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(document));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new JsonDocumentProcessor(engine).ProcessAsync(input, output));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Equal(0, output.Length);
    }
    [Fact]
    public async Task ManyEmptyContainersConsumeTheNodeBudget()
    {
        var engine = new RedactionEngine([], new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build());
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("[[],[],[],[]]"));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await new JsonDocumentProcessor(engine, new() { MaximumFields = 3 }).ProcessAsync(input, output));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Equal(0, output.Length);
    }
}
