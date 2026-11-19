using System.Reflection;
using System.Text;
using NuntivusRedact.Nlp.Onnx;
using Xunit;

namespace NuntivusRedact.Nlp.Tests;

public sealed class OnnxProviderTests
{
    [Theory]
    [InlineData("Alice Smith works at Contoso.", "Alice Smith", "PER")]
    [InlineData("Mario Rossi works", "Mario Rossi", "PER")]
    [InlineData("😀 José works", "José", "PER")]
    [InlineData("Ирина works", "Ирина", "PER")]
    [InlineData("東京", "東京", "LOC")]
    [InlineData("张伟", "张伟", "PER")]
    [InlineData("Johnson works", "Johnson", "PER")]
    [InlineData("\tAli\0ce\nSmith", "Ali\0ce\nSmith", "PER")]
    public async Task SyntheticNativeModelPreservesMultilingualOriginalOffsets(string input, string expected, string label)
    {
        using var fixture = new OnnxFixture();
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options());
        var entity = Assert.Single(await provider.RecognizeAsync(input), entity => entity.Label == label);
        Assert.Equal(expected, input.Substring(entity.Span.Start, entity.Span.Length));
        Assert.InRange(entity.Confidence, 0.99, 1);
        Assert.DoesNotContain(expected, entity.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelSupportsOptionalTokenTypesAndRepeatedDeterministicReuse(bool tokenTypes)
    {
        using var fixture = new OnnxFixture(tokenTypes: tokenTypes);
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options());
        var expected = await provider.RecognizeAsync("Alice Smith at Contoso.");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => await provider.RecognizeAsync("Alice Smith at Contoso.")));
        foreach (var result in results) Assert.Equal(expected, result);
    }

    [Fact]
    public async Task WindowsCoverTailAndReconcileEntityAcrossBoundary()
    {
        using var fixture = new OnnxFixture();
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options(sequence: 6, overlap: 2));
        string input = "works works works Alice Smith works works works works Mario Rossi";
        var entities = await provider.RecognizeAsync(input);
        Assert.Equal(new[] { "Alice Smith", "Mario Rossi" }, entities.Select(entity => input.Substring(entity.Span.Start, entity.Span.Length)));
        Assert.Equal(entities, await provider.RecognizeAsync(input));
    }

    [Fact]
    public async Task EmptyAndWhitespaceInputsProduceNoEntities()
    {
        using var fixture = new OnnxFixture();
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options());
        Assert.Empty(await provider.RecognizeAsync(""));
        Assert.Empty(await provider.RecognizeAsync("\t \r\n\0\u200b"));
    }

    [Theory]
    [InlineData("characters")]
    [InlineData("tokens")]
    [InlineData("windows")]
    [InlineData("entities")]
    public async Task ResourceLimitsFailRatherThanReturnPartialEntities(string limit)
    {
        using var fixture = new OnnxFixture();
        var options = fixture.Options(sequence: 6, overlap: 1, characters: limit == "characters" ? 2 : 1000,
            tokens: limit == "tokens" ? 1 : 1000, windows: limit == "windows" ? 1 : 100, entities: limit == "entities" ? 1 : 100);
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(options);
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await provider.RecognizeAsync("Alice works works works works Mario"));
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Null(error.InnerException);
        Assert.Empty(await provider.RecognizeAsync(""));
    }

    [Fact]
    public async Task BoundedAdmissionCancellationAndRecoveryAreDeterministic()
    {
        using var fixture = new OnnxFixture();
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options(requests: 1));
        var gate = (SemaphoreSlim)typeof(OnnxEntityRecognitionProvider).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provider)!;
        await gate.WaitAsync();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var waiting = provider.RecognizeAsync("Alice", cancellation.Token).AsTask();
            var error = await Assert.ThrowsAsync<RedactionException>(async () => await provider.RecognizeAsync("Mario"));
            Assert.Equal(RedactionError.LimitExceeded, error.Error);
            cancellation.Cancel();
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.DoesNotContain("Alice", canceled.ToString());
        }
        finally { gate.Release(); }
        Assert.Single(await provider.RecognizeAsync("Alice"));
    }

    [Fact]
    public async Task CancellationDisposalAndMalformedUtf16FailSafely()
    {
        using var fixture = new OnnxFixture();
        var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.RecognizeAsync("Alice", new CancellationToken(true)));
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await provider.RecognizeAsync("Alice\ud800"));
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
        Assert.Single(await provider.RecognizeAsync("Alice"));
        provider.Dispose();
        provider.Dispose();
        await Assert.ThrowsAsync<RedactionException>(async () => await provider.RecognizeAsync("Alice"));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("vocabulary")]
    public async Task InvalidAssetsAreSanitized(string failure)
    {
        using var fixture = new OnnxFixture();
        if (failure == "missing") File.Delete(fixture.ModelPath);
        if (failure == "corrupt") File.WriteAllBytes(fixture.ModelPath, [1, 2, 3]);
        if (failure == "vocabulary") File.WriteAllText(fixture.VocabularyPath, "sensitive-invalid-vocabulary");
        var options = fixture.Options(modelHash: failure == "hash" ? new string('0', 64) : null, modelBytes: failure == "size" ? 1 : 1_000_000);
        var error = await Assert.ThrowsAsync<RedactionException>(() => OnnxEntityRecognitionProvider.CreateAsync(options));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(fixture.ModelPath, error.ToString());
        Assert.DoesNotContain("sensitive-invalid", error.ToString());
    }

    [Fact]
    public async Task MalformedGraphAndIncompatibleOutputProfileAreRejected()
    {
        using var malformed = new OnnxFixture();
        File.WriteAllBytes(malformed.ModelPath, [1, 2, 3]);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(malformed.ModelPath)));
        var error = await Assert.ThrowsAsync<RedactionException>(() => OnnxEntityRecognitionProvider.CreateAsync(malformed.Options(modelHash: hash)));
        Assert.Equal(RedactionError.ExtensionFailed, error.Error);
        Assert.Null(error.InnerException);
        using var incompatible = new OnnxFixture(outputName: "unexpected");
        error = await Assert.ThrowsAsync<RedactionException>(() => OnnxEntityRecognitionProvider.CreateAsync(incompatible.Options()));
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
    }

    [Fact]
    public async Task NonfiniteLogitsAreRejected()
    {
        using var fixture = new OnnxFixture(nonfinite: true);
        using var provider = await OnnxEntityRecognitionProvider.CreateAsync(fixture.Options());
        var error = await Assert.ThrowsAsync<RedactionException>(async () => await provider.RecognizeAsync("Alice"));
        Assert.Equal(RedactionError.InvalidDetection, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void ConfigurationCopiesLabelsAndRejectsInvalidProfiles()
    {
        using var fixture = new OnnxFixture();
        string[] labels = ["O", "B-PER", "I-PER"];
        var options = new OnnxNerOptions(fixture.ModelPath, fixture.VocabularyPath, fixture.ModelHash, fixture.VocabularyHash, labels);
        labels[1] = "B-SECRET";
        Assert.Equal("B-PER", options.Labels[1]);
        Assert.Throws<ArgumentException>(() => new OnnxNerOptions("m", "v", "wrong", fixture.VocabularyHash, labels));
        Assert.Throws<ArgumentException>(() => new OnnxNerOptions("m", "v", fixture.ModelHash, fixture.VocabularyHash, ["O", "B-PER", "I-LOC"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Options(sequence: 4, overlap: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Options(sequence: 513));
    }

    [Fact]
    public void CasedTokenizerUsesBasicWordBoundariesAndCompleteSubwordCoordinates()
    {
        var tokenizer = new CasedWordPieceTokenizer(Encoding.UTF8.GetBytes(string.Join('\n', OnnxFixture.Vocabulary) + "\n"));
        string input = "😀\tJosé\u00a0東京 Johnson [MASK]";
        var tokens = tokenizer.Encode(input, 100, default);
        Assert.Equal(new[] { "😀", "José", "東", "京", "John", "son", "[MASK]" },
            tokens.Select(token => input.Substring(token.Start, token.End - token.Start)));
        Assert.All(tokens.Where(token => token.WordStart == input.IndexOf("Johnson", StringComparison.Ordinal)),
            token => Assert.Equal("Johnson", input.Substring(token.WordStart, token.WordEnd - token.WordStart)));
    }

    [Fact]
    public void BioRecoveryExpandsWholeWordsAndKeepsOrphanInsideEntities()
    {
        SourceToken[] tokens = [new(1, 0, 3, 0, 7), new(2, 3, 7, 0, 7), new(3, 8, 13, 8, 13)];
        var entities = OnnxEntityRecognitionProvider.Decode(tokens, [2, 0, 2], [0.8, 1, 0.7], OnnxFixture.Labels, 10, default);
        Assert.Equal(new DetectionSpan(0, 7), entities[0].Span);
        Assert.Equal(new DetectionSpan(8, 5), entities[1].Span);
    }
}
