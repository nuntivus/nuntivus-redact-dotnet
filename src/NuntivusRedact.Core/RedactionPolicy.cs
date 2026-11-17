using System.Collections.Frozen;

namespace NuntivusRedact.Core;

/// <summary>An immutable strict policy snapshot. Supplied callbacks and strategies must be thread safe.</summary>
public sealed class RedactionPolicy
{
    internal RedactionPolicy(RedactionPolicyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder.DefaultStrategy);
        if (!double.IsFinite(builder.MinimumConfidence) || builder.MinimumConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(builder));
        ArgumentOutOfRangeException.ThrowIfLessThan(builder.MaximumInputCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(builder.MaximumOutputCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(builder.MaximumDetections, 1);
        DefaultStrategy = builder.DefaultStrategy;
        MinimumConfidence = builder.MinimumConfidence;
        MaximumInputCharacters = builder.MaximumInputCharacters;
        MaximumOutputCharacters = builder.MaximumOutputCharacters;
        MaximumDetections = builder.MaximumDetections;
        Included = builder.Included.ToFrozenSet(StringComparer.Ordinal);
        Excluded = builder.Excluded.ToFrozenSet(StringComparer.Ordinal);
        Priorities = builder.Priorities.ToFrozenDictionary(StringComparer.Ordinal);
        Strategies = builder.Strategies.ToFrozenDictionary(StringComparer.Ordinal);
        Rules = builder.Rules.ToArray();
    }

    /// <summary>Gets the default strategy.</summary>
    public IRedactionStrategy DefaultStrategy { get; }
    /// <summary>Gets the minimum accepted confidence.</summary>
    public double MinimumConfidence { get; }
    /// <summary>Gets the maximum original text size.</summary>
    public int MaximumInputCharacters { get; }
    /// <summary>Gets the maximum output text size.</summary>
    public int MaximumOutputCharacters { get; }
    /// <summary>Gets the maximum number of candidates, before filtering.</summary>
    public int MaximumDetections { get; }
    internal FrozenSet<string> Included { get; }
    internal FrozenSet<string> Excluded { get; }
    internal FrozenDictionary<string, int> Priorities { get; }
    internal FrozenDictionary<string, IRedactionStrategy> Strategies { get; }
    internal Func<Detection, bool>[] Rules { get; }
    internal bool Accepts(Detection detection) => detection.Confidence >= MinimumConfidence &&
        (Included.Count == 0 || Included.Contains(detection.Category)) && !Excluded.Contains(detection.Category) &&
        Rules.All(rule => rule(detection));
}

/// <summary>Creates immutable strict policies. Builders are not thread safe; snapshots are reusable.</summary>
public sealed class RedactionPolicyBuilder
{
    /// <summary>Creates a builder with an explicitly chosen default strategy.</summary>
    public RedactionPolicyBuilder(IRedactionStrategy defaultStrategy) => DefaultStrategy = defaultStrategy ?? throw new ArgumentNullException(nameof(defaultStrategy));
    /// <summary>Gets or sets the fallback replacement strategy.</summary>
    public IRedactionStrategy DefaultStrategy { get; set; }
    /// <summary>Gets or sets minimum confidence from zero through one.</summary>
    public double MinimumConfidence { get; set; }
    /// <summary>Gets or sets the input character limit.</summary>
    public int MaximumInputCharacters { get; set; } = 1_000_000;
    /// <summary>Gets or sets the output character limit.</summary>
    public int MaximumOutputCharacters { get; set; } = 2_000_000;
    /// <summary>Gets or sets the candidate limit before filtering.</summary>
    public int MaximumDetections { get; set; } = 1_000;
    internal HashSet<string> Included { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> Excluded { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, int> Priorities { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, IRedactionStrategy> Strategies { get; } = new(StringComparer.Ordinal);
    internal List<Func<Detection, bool>> Rules { get; } = [];

    /// <summary>Restricts categories; an empty inclusion set permits all categories.</summary>
    public RedactionPolicyBuilder Include(string category) { ArgumentException.ThrowIfNullOrWhiteSpace(category); Included.Add(category); return this; }
    /// <summary>Excludes a category, overriding inclusion.</summary>
    public RedactionPolicyBuilder Exclude(string category) { ArgumentException.ThrowIfNullOrWhiteSpace(category); Excluded.Add(category); return this; }
    /// <summary>Overrides detector priority; larger priorities win overlaps.</summary>
    public RedactionPolicyBuilder WithPriority(string detectorId, int priority) { ArgumentException.ThrowIfNullOrWhiteSpace(detectorId); Priorities[detectorId] = priority; return this; }
    /// <summary>Selects a strategy for a category.</summary>
    public RedactionPolicyBuilder WithStrategy(string category, IRedactionStrategy strategy) { ArgumentException.ThrowIfNullOrWhiteSpace(category); ArgumentNullException.ThrowIfNull(strategy); Strategies[category] = strategy; return this; }
    /// <summary>Adds a trusted, source-free candidate predicate.</summary>
    public RedactionPolicyBuilder Where(Func<Detection, bool> rule) { ArgumentNullException.ThrowIfNull(rule); Rules.Add(rule); return this; }
    /// <summary>Validates settings and creates an independent immutable snapshot.</summary>
    public RedactionPolicy Build() => new(this);
}
