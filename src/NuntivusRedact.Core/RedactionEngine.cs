using System.Text;

namespace NuntivusRedact.Core;

/// <summary>Explicit detector registration with default overlap priority.</summary>
public sealed record DetectorRegistration
{
    /// <summary>Registers a thread-safe detector. Higher priority wins.</summary>
    public DetectorRegistration(ISensitiveDataDetector detector, int priority = 0)
    {
        Detector = detector ?? throw new ArgumentNullException(nameof(detector));
        ArgumentException.ThrowIfNullOrWhiteSpace(detector.Id);
        Priority = priority;
    }
    /// <summary>Gets the registered detector.</summary>
    public ISensitiveDataDetector Detector { get; }
    /// <summary>Gets its default priority.</summary>
    public int Priority { get; }
}

/// <summary>Bounded, deterministic, strict text processing with no logging or DI dependency.</summary>
public sealed class RedactionEngine : IEditAwareTextRedactor
{
    private readonly DetectorRegistration[] detectors;
    private readonly RedactionPolicy policy;

    /// <summary>Creates a reusable engine with a defensive registration snapshot.</summary>
    public RedactionEngine(IEnumerable<DetectorRegistration> detectors, RedactionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(detectors);
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        this.detectors = detectors.ToArray();
        if (this.detectors.Any(d => d is null) || this.detectors.Select(d => d.Detector.Id).Distinct(StringComparer.Ordinal).Count() != this.detectors.Length)
            throw new ArgumentException("Detector registrations must have unique identifiers.", nameof(detectors));
    }

    /// <inheritdoc />
    public async ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await RedactWithEditsAsync(text, cancellationToken).ConfigureAwait(false);
        return new RedactionResult(result.Text, result.Edits.Select(edit => edit.Detection));
    }

    /// <inheritdoc />
    public async ValueTask<EditRedactionResult> RedactWithEditsAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length > policy.MaximumInputCharacters) throw new RedactionException(RedactionError.LimitExceeded);
        try
        {
            var candidates = new List<(Detection Detection, int Priority)>();
            int candidateCount = 0;
            var context = new DetectionContext(text);
            foreach (var registration in detectors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var found = await registration.Detector.DetectAsync(context, cancellationToken).ConfigureAwait(false);
                if (found is null) throw new RedactionException(RedactionError.InvalidDetection);
                foreach (var detection in found)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++candidateCount > policy.MaximumDetections) throw new RedactionException(RedactionError.LimitExceeded);
                    if (detection is null || detection.Span.End > text.Length || detection.DetectorId != registration.Detector.Id ||
                        SplitsSurrogate(text, detection.Span.Start) || SplitsSurrogate(text, detection.Span.End))
                        throw new RedactionException(RedactionError.InvalidDetection);
                    if (policy.Accepts(detection))
                        candidates.Add((detection, policy.Priorities.GetValueOrDefault(detection.DetectorId, registration.Priority)));
                }
            }
            var accepted = new List<Detection>();
            foreach (var candidate in candidates.OrderByDescending(c => c.Priority)
                .ThenByDescending(c => c.Detection.Confidence).ThenByDescending(c => c.Detection.Span.Length)
                .ThenBy(c => c.Detection.Span.Start).ThenBy(c => c.Detection.DetectorId, StringComparer.Ordinal)
                .ThenBy(c => c.Detection.Category, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!accepted.Any(d => d.Span.Overlaps(candidate.Detection.Span))) accepted.Add(candidate.Detection);
            }
            accepted.Sort((left, right) => left.Span.Start.CompareTo(right.Span.Start));
            var output = new StringBuilder(Math.Min(text.Length, policy.MaximumOutputCharacters));
            var edits = new List<RedactionEdit>(accepted.Count);
            int position = 0;
            foreach (var detection in accepted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Append(output, text.AsSpan(position, detection.Span.Start - position));
                var strategy = policy.Strategies.GetValueOrDefault(detection.Category, policy.DefaultStrategy);
                var replacement = strategy.Redact(text.AsSpan(detection.Span.Start, detection.Span.Length), detection);
                if (replacement is null) throw new RedactionException(RedactionError.ExtensionFailed);
                Append(output, replacement);
                edits.Add(new RedactionEdit(detection, replacement));
                position = detection.Span.End;
            }
            Append(output, text.AsSpan(position));
            cancellationToken.ThrowIfCancellationRequested();
            return new EditRedactionResult(output.ToString(), edits);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
    }

    private void Append(StringBuilder output, ReadOnlySpan<char> value)
    {
        if (value.Length > policy.MaximumOutputCharacters - output.Length) throw new RedactionException(RedactionError.LimitExceeded);
        output.Append(value);
    }

    private static bool SplitsSurrogate(string text, int offset) => offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
}
