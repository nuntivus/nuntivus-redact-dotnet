using System.Collections.Frozen;
using NuntivusRedact.Nlp;

namespace NuntivusRedact.Detectors.Nlp;

/// <summary>Optional NLP category identifiers. Recognition alone does not establish sensitivity.</summary>
public static class NlpDetectionCategories
{
    /// <summary>A person's name.</summary>
    public const string Person = "person";
    /// <summary>An organization name.</summary>
    public const string Organization = "organization";
    /// <summary>A named location, not a complete postal-address detector.</summary>
    public const string Location = "location";
    /// <summary>A date expression, not necessarily a date of birth.</summary>
    public const string Date = "date";
    /// <summary>A model-specific miscellaneous entity.</summary>
    public const string Miscellaneous = "miscellaneous";
}

/// <summary>Immutable provider-label mapping and adapter budgets.</summary>
public sealed class NlpDetectorOptions
{
    /// <summary>Creates a defensive mapping snapshot; every returned entity label must be mapped.</summary>
    public NlpDetectorOptions(IEnumerable<KeyValuePair<string, string>>? labelCategories = null,
        int maximumInputCharacters = 100_000, int maximumEntities = 1_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumInputCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntities, 1);
        var mapping = (labelCategories ?? new Dictionary<string, string>
        {
            ["PER"] = NlpDetectionCategories.Person,
            ["ORG"] = NlpDetectionCategories.Organization,
            ["LOC"] = NlpDetectionCategories.Location,
            ["DATE"] = NlpDetectionCategories.Date,
            ["MISC"] = NlpDetectionCategories.Miscellaneous
        }).Take(65).ToArray();
        if (mapping.Length is 0 or > 64 || mapping.Any(p => string.IsNullOrWhiteSpace(p.Key) ||
            p.Key.Length > 64 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 64) ||
            mapping.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != mapping.Length)
            throw new ArgumentException("Invalid entity label mapping.", nameof(labelCategories));
        LabelCategories = mapping.ToFrozenDictionary(StringComparer.Ordinal);
        MaximumInputCharacters = maximumInputCharacters;
        MaximumEntities = maximumEntities;
    }

    /// <summary>Gets the fixed provider-label to detection-category mapping.</summary>
    public IReadOnlyDictionary<string, string> LabelCategories { get; }
    /// <summary>Gets the maximum input size in UTF-16 code units.</summary>
    public int MaximumInputCharacters { get; }
    /// <summary>Gets the maximum provider candidate count, before policy filtering.</summary>
    public int MaximumEntities { get; }
}

/// <summary>Adapts a caller-owned entity provider without depending on an inference SDK or DI.</summary>
public sealed class NlpDetector : ISensitiveDataDetector
{
    private readonly IEntityRecognitionProvider provider;
    private readonly NlpDetectorOptions options;

    /// <summary>Creates an explicitly registered detector. The caller retains provider lifetime ownership.</summary>
    public NlpDetector(string id, IEntityRecognitionProvider provider, NlpDetectorOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.options = options ?? new();
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Text.Length > options.MaximumInputCharacters) throw new RedactionException(RedactionError.LimitExceeded);
        try
        {
            var entities = await provider.RecognizeAsync(context.Text, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (entities is null) throw new RedactionException(RedactionError.InvalidDetection);
            if (entities.Count > options.MaximumEntities) throw new RedactionException(RedactionError.LimitExceeded);
            var detections = new List<Detection>(entities.Count);
            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entity is null || entity.Span.End > context.Text.Length ||
                    SplitsSurrogate(context.Text, entity.Span.Start) || SplitsSurrogate(context.Text, entity.Span.End) ||
                    !options.LabelCategories.TryGetValue(entity.Label, out var category))
                    throw new RedactionException(RedactionError.InvalidDetection);
                detections.Add(new(entity.Span, category, entity.Confidence, Id));
            }
            return detections.AsReadOnly();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
    }

    private static bool SplitsSurrogate(string text, int offset) => offset > 0 && offset < text.Length &&
        char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
}
