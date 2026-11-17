namespace NuntivusRedact.Detectors.Regex;

/// <summary>Immutable ordinal-ignore-case keyword dictionary detector with optional word boundaries.</summary>
public sealed class DictionaryDetector : ISensitiveDataDetector
{
    private readonly string[] words;
    private readonly string category;
    private readonly bool wholeWords;
    private readonly int maximumInputCharacters;
    private readonly int maximumMatches;

    /// <summary>Copies up to 1000 nonempty keywords, each at most 256 UTF-16 code units.</summary>
    public DictionaryDetector(string id, string category, IEnumerable<string> keywords, bool wholeWords = true,
        int maximumInputCharacters = 1_000_000, int maximumMatches = 1_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumInputCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMatches, 1);
        words = keywords.Take(1001).ToArray();
        if (words.Length is 0 or > 1000 || words.Any(w => string.IsNullOrWhiteSpace(w) || w.Length > 256))
            throw new ArgumentException("Invalid keyword dictionary configuration.", nameof(keywords));
        words = words.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Id = id;
        this.category = category;
        this.wholeWords = wholeWords;
        this.maximumInputCharacters = maximumInputCharacters;
        this.maximumMatches = maximumMatches;
    }
    /// <inheritdoc />
    public string Id { get; }
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Text.Length > maximumInputCharacters) throw new RedactionException(RedactionError.LimitExceeded);
        var matches = new List<Detection>();
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int offset = 0;
            while (offset <= context.Text.Length - word.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int index = context.Text.IndexOf(word, offset, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                int end = index + word.Length;
                if (!wholeWords || ((index == 0 || !IsWord(context.Text[index - 1])) && (end == context.Text.Length || !IsWord(context.Text[end]))))
                {
                    if (matches.Count == maximumMatches) throw new RedactionException(RedactionError.LimitExceeded);
                    matches.Add(new(new(index, word.Length), category, 1, Id));
                }
                offset = index + 1;
            }
        }
        return ValueTask.FromResult<IReadOnlyList<Detection>>(matches.AsReadOnly());
    }
    private static bool IsWord(char value) => char.IsLetterOrDigit(value) || value == '_';
}
