namespace NuntivusRedact;

/// <summary>A validated UTF-16 range into the original input text.</summary>
public readonly record struct DetectionSpan
{
    /// <summary>Creates a nonempty range with a nonnegative start.</summary>
    public DetectionSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        if (start > int.MaxValue - length) throw new ArgumentOutOfRangeException(nameof(length));
        Start = start;
        Length = length;
    }

    /// <summary>Gets the UTF-16 starting offset.</summary>
    public int Start { get; }
    /// <summary>Gets the UTF-16 length.</summary>
    public int Length { get; }
    /// <summary>Gets the exclusive end offset.</summary>
    public int End => Start + Length;
    /// <summary>Tests whether ranges share any code units.</summary>
    public bool Overlaps(DetectionSpan other) => Start < other.End && other.Start < End;
}

/// <summary>A source-free candidate detection. Confidence is a heuristic, not a probability guarantee.</summary>
public sealed record Detection
{
    /// <summary>Creates a validated candidate without retaining the matched value.</summary>
    public Detection(DetectionSpan span, string category, double confidence, string detectorId)
    {
        if (span.Length < 1) throw new ArgumentException("A nonempty span is required.", nameof(span));
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectorId);
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        Span = span;
        Category = category;
        Confidence = confidence;
        DetectorId = detectorId;
    }

    /// <summary>Gets the original input range.</summary>
    public DetectionSpan Span { get; }
    /// <summary>Gets the semantic category identifier, not source content.</summary>
    public string Category { get; }
    /// <summary>Gets a score between zero and one.</summary>
    public double Confidence { get; }
    /// <summary>Gets the stable detector identifier.</summary>
    public string DetectorId { get; }
}

/// <summary>Common category identifiers; extensions may use their own identifiers.</summary>
public static class DetectionCategories
{
    /// <summary>Email candidate.</summary>
    public const string Email = "email";
    /// <summary>Phone candidate.</summary>
    public const string Phone = "phone";
    /// <summary>IP address.</summary>
    public const string IpAddress = "ip-address";
    /// <summary>IBAN with valid check digits.</summary>
    public const string Iban = "iban";
    /// <summary>URL candidate.</summary>
    public const string Url = "url";
    /// <summary>Card candidate with valid Luhn checksum.</summary>
    public const string CreditCard = "credit-card";
}
