using System.Text.RegularExpressions;
using Pattern = System.Text.RegularExpressions.Regex;

namespace NuntivusRedact.Detectors.Regex;

/// <summary>Bounded regex detector. Patterns and validators are trusted configuration; source is never included in failures.</summary>
public sealed class RegexDetector : ISensitiveDataDetector
{
    private readonly Pattern pattern;
    private readonly string category;
    private readonly double confidence;
    private readonly int maximumInputCharacters;
    private readonly int maximumMatches;
    private readonly Func<string, bool>? validator;

    /// <summary>Creates a detector with finite regex timeout, input and candidate limits.</summary>
    public RegexDetector(string id, string category, string pattern, double confidence = 0.8,
        TimeSpan? timeout = null, int maximumInputCharacters = 1_000_000, int maximumMatches = 1_000,
        Func<string, bool>? validator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length is 0 or > 4096) throw new ArgumentException("Pattern length is outside the supported range.", nameof(pattern));
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumInputCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMatches, 1);
        var budget = timeout ?? TimeSpan.FromMilliseconds(100);
        if (budget <= TimeSpan.Zero || budget > TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        try { this.pattern = new Pattern(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, budget); }
        catch (ArgumentException) { throw new ArgumentException("Invalid regex configuration.", nameof(pattern)); }
        Id = id;
        this.category = category;
        this.confidence = confidence;
        this.maximumInputCharacters = maximumInputCharacters;
        this.maximumMatches = maximumMatches;
        this.validator = validator;
    }

    /// <inheritdoc />
    public string Id { get; }
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Text.Length > maximumInputCharacters) throw new RedactionException(RedactionError.LimitExceeded);
        try
        {
            var detections = new List<Detection>();
            int candidateCount = 0;
            foreach (Match match in pattern.Matches(context.Text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++candidateCount > maximumMatches) throw new RedactionException(RedactionError.LimitExceeded);
                if (match.Length == 0) throw new RedactionException(RedactionError.InvalidDetection);
                if (validator is null || validator(match.Value)) detections.Add(new(new(match.Index, match.Length), category, confidence, Id));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<Detection>>(detections.AsReadOnly());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (RegexMatchTimeoutException) { throw new RedactionException(RedactionError.LimitExceeded); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
    }
}
