namespace NuntivusRedact.Formats.Office;

/// <summary>Immutable per-request budgets, shared by every nested Office package. Hosts must also bound concurrency.</summary>
public sealed record OfficeLimits
{
    /// <summary>Gets the maximum compressed input bytes.</summary>
    public int MaximumInputBytes { get; init; } = 8 * 1024 * 1024;
    /// <summary>Gets the maximum compressed output bytes, including ZIP directory records.</summary>
    public int MaximumOutputBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Gets the aggregate inflated bytes across all entries and nested packages.</summary>
    public int MaximumInflatedBytes { get; init; } = 32 * 1024 * 1024;
    /// <summary>Gets the maximum actual inflated bytes in one entry.</summary>
    public int MaximumEntryBytes { get; init; } = 8 * 1024 * 1024;
    /// <summary>Gets the aggregate archive entry count, including nested packages.</summary>
    public int MaximumEntries { get; init; } = 512;
    /// <summary>Gets the maximum declared inflated/compressed entry ratio.</summary>
    public int MaximumCompressionRatio { get; init; } = 200;
    /// <summary>Gets the maximum XML nesting depth.</summary>
    public int MaximumXmlDepth { get; init; } = 64;
    /// <summary>Gets the aggregate XML reader node budget.</summary>
    public int MaximumXmlNodes { get; init; } = 200_000;
    /// <summary>Gets the aggregate number of text/structural values inspected.</summary>
    public int MaximumFields { get; init; } = 50_000;
    /// <summary>Gets the aggregate number of accepted detections.</summary>
    public int MaximumDetections { get; init; } = 10_000;
    /// <summary>Gets the number of embedded package levels allowed below the outer package.</summary>
    public int MaximumNestingDepth { get; init; } = 3;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumInputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumOutputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumInflatedBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumEntryBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumCompressionRatio, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumXmlDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumXmlDepth, 256);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumXmlNodes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumFields, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumDetections, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumNestingDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumNestingDepth, 8);
    }
}
