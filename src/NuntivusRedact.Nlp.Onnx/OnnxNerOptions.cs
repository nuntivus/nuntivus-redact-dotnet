namespace NuntivusRedact.Nlp.Onnx;

/// <summary>Immutable local assets, BIO labels and resource limits for the cased WordPiece profile.</summary>
public sealed class OnnxNerOptions
{
    /// <summary>Creates a configuration. SHA-256 digests must identify separately provisioned, trusted assets.</summary>
    /// <remarks>Labels must exactly match the model output axis, including O and B-/I- prefixes. No model is downloaded.</remarks>
    public OnnxNerOptions(string modelPath, string vocabularyPath, string modelSha256, string vocabularySha256,
        IEnumerable<string> labels, int maximumSequenceLength = 128, int overlapTokens = 32,
        int maximumInputCharacters = 100_000, int maximumTokens = 20_000, int maximumWindows = 256,
        int maximumEntities = 1_000, int maximumConcurrentRequests = 4,
        int maximumModelBytes = 1_000_000_000, int maximumVocabularyBytes = 8_000_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabularyPath);
        ArgumentNullException.ThrowIfNull(labels);
        ValidateDigest(modelSha256);
        ValidateDigest(vocabularySha256);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSequenceLength, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumSequenceLength, 512);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);
        if (overlapTokens >= maximumSequenceLength - 2) throw new ArgumentOutOfRangeException(nameof(overlapTokens));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumInputCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumWindows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntities, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrentRequests, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumModelBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumVocabularyBytes, 1);
        var snapshot = labels.Take(129).ToArray();
        if (snapshot.Length is < 3 or > 128 || snapshot.Any(label => !IsBioLabel(label)) ||
            snapshot.Count(label => label == "O") != 1 || snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            snapshot.Where(label => label.StartsWith("I-", StringComparison.Ordinal))
                .Any(label => !snapshot.Contains("B-" + label[2..], StringComparer.Ordinal)) ||
            snapshot.Where(label => label.StartsWith("B-", StringComparison.Ordinal))
                .Any(label => !snapshot.Contains("I-" + label[2..], StringComparer.Ordinal)))
            throw new ArgumentException("Invalid BIO label profile.", nameof(labels));
        ModelPath = modelPath;
        VocabularyPath = vocabularyPath;
        ModelSha256 = modelSha256;
        VocabularySha256 = vocabularySha256;
        Labels = Array.AsReadOnly(snapshot);
        MaximumSequenceLength = maximumSequenceLength;
        OverlapTokens = overlapTokens;
        MaximumInputCharacters = maximumInputCharacters;
        MaximumTokens = maximumTokens;
        MaximumWindows = maximumWindows;
        MaximumEntities = maximumEntities;
        MaximumConcurrentRequests = maximumConcurrentRequests;
        MaximumModelBytes = maximumModelBytes;
        MaximumVocabularyBytes = maximumVocabularyBytes;
    }

    /// <summary>Gets the local, self-contained ONNX graph path. External tensor files are unsupported.</summary>
    public string ModelPath { get; }
    /// <summary>Gets the local UTF-8 cased WordPiece vocabulary path.</summary>
    public string VocabularyPath { get; }
    /// <summary>Gets the expected model SHA-256 digest in hexadecimal.</summary>
    public string ModelSha256 { get; }
    /// <summary>Gets the expected vocabulary SHA-256 digest in hexadecimal.</summary>
    public string VocabularySha256 { get; }
    /// <summary>Gets the ordered output-axis labels.</summary>
    public IReadOnlyList<string> Labels { get; }
    /// <summary>Gets the padded sequence length, including CLS and SEP.</summary>
    public int MaximumSequenceLength { get; }
    /// <summary>Gets the overlap between neighboring content-token windows.</summary>
    public int OverlapTokens { get; }
    /// <summary>Gets the UTF-16 input limit.</summary>
    public int MaximumInputCharacters { get; }
    /// <summary>Gets the complete-input subword-token limit.</summary>
    public int MaximumTokens { get; }
    /// <summary>Gets the maximum windows per call; exceeding it fails before inference.</summary>
    public int MaximumWindows { get; }
    /// <summary>Gets the maximum emitted entities.</summary>
    public int MaximumEntities { get; }
    /// <summary>Gets the combined active and waiting admission limit. Each provider runs one inference call at a time.</summary>
    public int MaximumConcurrentRequests { get; }
    /// <summary>Gets the model-file byte limit, not a native memory limit.</summary>
    public int MaximumModelBytes { get; }
    /// <summary>Gets the vocabulary-file byte limit.</summary>
    public int MaximumVocabularyBytes { get; }

    private static void ValidateDigest(string digest)
    {
        if (digest is null || digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("A SHA-256 digest is required.");
    }

    private static bool IsBioLabel(string? label) => label == "O" || label is { Length: > 2 and <= 66 } &&
        (label.StartsWith("B-", StringComparison.Ordinal) || label.StartsWith("I-", StringComparison.Ordinal)) &&
        label.AsSpan(2).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-".AsSpan()) < 0;
}
