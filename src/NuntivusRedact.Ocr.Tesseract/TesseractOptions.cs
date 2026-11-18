namespace NuntivusRedact.Ocr.Tesseract;

/// <summary>Immutable configuration. Native binaries and trusted traineddata must be deployed by the host; no automatic downloads occur.</summary>
public sealed record TesseractOptions
{
    /// <summary>Directory containing trusted language.traineddata files.</summary>
    public required string DataPath { get; init; }
    /// <summary>Plus-separated Tesseract language identifiers.</summary>
    public string Languages { get; init; } = "eng";
    /// <summary>Maximum simultaneous native engines per provider.</summary>
    public int MaximumConcurrency { get; init; } = 1;
    /// <summary>Maximum PNG bytes accepted from the image pipeline.</summary>
    public int MaximumInputBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Maximum image pixels before loading into Leptonica.</summary>
    public long MaximumPixels { get; init; } = 10_000_000;
    /// <summary>Maximum recognized UTF-16 characters.</summary>
    public int MaximumTextLength { get; init; } = 100_000;
    /// <summary>Maximum recognized words.</summary>
    public int MaximumWords { get; init; } = 20_000;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DataPath);
        if (string.IsNullOrWhiteSpace(Languages) || Languages.Length > 128 ||
            Languages.Split('+').Any(s => s.Length == 0 || !s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("Use explicit Tesseract language identifiers.", nameof(Languages));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumConcurrency, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumInputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumPixels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPixels, 100_000_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumTextLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumWords, 1);
        if (!Directory.Exists(DataPath) || Languages.Split('+').Any(language => !File.Exists(Path.Combine(DataPath, language + ".traineddata"))))
            throw new ArgumentException("Trusted Tesseract language data is required.", nameof(DataPath));
    }
}
