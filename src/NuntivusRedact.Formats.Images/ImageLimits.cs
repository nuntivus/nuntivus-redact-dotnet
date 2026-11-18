namespace NuntivusRedact.Formats.Images;

/// <summary>Immutable per-image limits. Hosts must additionally bound total documents and process memory.</summary>
public sealed record ImageLimits
{
    /// <summary>Maximum compressed source bytes.</summary>
    public int MaximumInputBytes { get; init; } = 8 * 1024 * 1024;
    /// <summary>Maximum canonical or output PNG bytes.</summary>
    public int MaximumOutputBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Maximum decoded raster pixels, before allocation.</summary>
    public long MaximumPixels { get; init; } = 10_000_000;
    /// <summary>Maximum width or height.</summary>
    public int MaximumDimension { get; init; } = 10_000;
    /// <summary>Maximum recognized UTF-16 characters or replacement characters.</summary>
    public int MaximumTextLength { get; init; } = 100_000;
    /// <summary>Maximum recognized word count.</summary>
    public int MaximumWords { get; init; } = 20_000;
    /// <summary>Maximum accepted detections per image.</summary>
    public int MaximumDetections { get; init; } = 10_000;
    /// <summary>Maximum simultaneous operations per image redactor instance.</summary>
    public int MaximumConcurrency { get; init; } = 2;
    /// <summary>Extra opaque black pixels around each matched word rectangle; entire words are always removed.</summary>
    public int PaddingPixels { get; init; } = 3;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumInputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumOutputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumPixels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPixels, 100_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumDimension, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumDimension, 30_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumTextLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumWords, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumDetections, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumConcurrency, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(PaddingPixels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(PaddingPixels, 64);
    }
}
