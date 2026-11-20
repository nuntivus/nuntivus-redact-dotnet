namespace NuntivusRedact.Pdf.Experimental;

/// <summary>Caller-selected rectangle in displayed PDF points; page indexes are zero-based.</summary>
public sealed record PdfManualRegion(int PageIndex, double Left, double Top, double Right, double Bottom);

/// <summary>Immutable experimental raster processing bounds. Not a supported-format admission policy.</summary>
public sealed record ExperimentalPdfOptions
{
    /// <summary>Gets explicit fidelity mode; unsupported modes never fall back.</summary>
    public required PdfOutputMode Mode { get; init; }
    /// <summary>Gets raster resolution in dots per inch.</summary>
    public int Dpi { get; init; } = 144;
    /// <summary>Gets maximum accepted native-text matches across pages.</summary>
    public int MaximumMatches { get; init; } = 10_000;
    /// <summary>Gets maximum manual rectangles.</summary>
    public int MaximumManualRegions { get; init; } = 10_000;
    /// <summary>Gets backend resource bounds.</summary>
    public PdfBackendLimits Limits { get; init; } = new();
}

/// <summary>Experimental trusted-input pipeline; not registered as supported PDF processing.
/// Rasterized removes matched native-text/manual pixels into fresh pages. OCR and complete preflight are not implemented.
/// NativePreserving uses optional experimental whole-object removal; not a supported secure format profile.</summary>
public sealed class ExperimentalPdfRedactor(IPdfBackend backend, IEditAwareTextRedactor redactor)
{
    private readonly IPdfBackend backend = backend ?? throw new ArgumentNullException(nameof(backend));
    private readonly IEditAwareTextRedactor redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));

    /// <summary>Returns sensitive staged experimental bytes only after re-open/text/pixel checks.
    /// Does not certify anonymization or complete discovery. Replacement strings are not painted; accepted spans are removed.</summary>
    public async ValueTask<ReadOnlyMemory<byte>> RedactAsync(ReadOnlyMemory<byte> input, ExperimentalPdfOptions options,
        IEnumerable<PdfManualRegion>? manualRegions = null, CancellationToken cancellationToken = default)
    {
        try { return await ProcessAsync(input, options, manualRegions, cancellationToken); }
        catch (RedactionException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ArgumentException) { throw; }
        catch { throw new RedactionException(RedactionError.ExtensionFailed); }
    }

    private async ValueTask<ReadOnlyMemory<byte>> ProcessAsync(ReadOnlyMemory<byte> input, ExperimentalPdfOptions options,
        IEnumerable<PdfManualRegion>? manualRegions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Limits);
        options.Limits.Validate();
        if (!Enum.IsDefined(options.Mode) || options.Dpi < 1 || options.Dpi > 600 || options.MaximumMatches < 1 || options.MaximumManualRegions < 1)
            throw new ArgumentException("Invalid experimental PDF options.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!backend.Capabilities.Rendering || !backend.Capabilities.RasterWriting ||
            !backend.Capabilities.NativeTextExtraction) throw new RedactionException(RedactionError.UnsupportedFormat);
        if (input.Length > options.Limits.MaximumInputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        var regions = new List<PdfManualRegion>();
        foreach (var region in manualRegions ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (regions.Count >= options.MaximumManualRegions) throw new RedactionException(RedactionError.LimitExceeded);
            if (region is null) throw new ArgumentException("Invalid manual region.");
            regions.Add(region);
        }
        if (options.Mode == PdfOutputMode.NativePreserving)
            return await ProcessNativeAsync(input, options, regions, cancellationToken);
        await using var document = await backend.OpenAsync(input, options.Limits, cancellationToken);
        if (document.PageCount < 1 || document.PageCount > options.Limits.MaximumPages) throw new RedactionException(RedactionError.LimitExceeded);
        if (regions.Any(region => region.PageIndex < 0 || region.PageIndex >= document.PageCount)) throw new ArgumentException("Invalid manual page.");
        await using var writer = await backend.CreateRasterWriterAsync(options.Limits, cancellationToken);
        var expected = new List<(PdfPageSize Size, PdfRaster Raster)>();
        long pixels = 0;
        int matches = 0;
        for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await document.ReadPageAsync(pageIndex, cancellationToken);
            ValidatePage(page, options.Limits);
            double scaledWidth = Math.Ceiling(page.Size.Width * options.Dpi / 72);
            double scaledHeight = Math.Ceiling(page.Size.Height * options.Dpi / 72);
            if (scaledWidth > int.MaxValue || scaledHeight > int.MaxValue) throw new RedactionException(RedactionError.LimitExceeded);
            int width = (int)scaledWidth, height = (int)scaledHeight;
            long pagePixels = (long)width * height;
            if (pagePixels > options.Limits.MaximumPixelsPerPage || pagePixels > options.Limits.MaximumTotalPixels - pixels)
                throw new RedactionException(RedactionError.LimitExceeded);
            pixels += pagePixels;
            EditRedactionResult edits;
            try { edits = await redactor.RedactWithEditsAsync(page.Text, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { throw new RedactionException(RedactionError.ExtensionFailed); }
            ValidateEdits(page.Text, edits);
            if (edits.Edits.Count > options.MaximumMatches - matches) throw new RedactionException(RedactionError.LimitExceeded);
            matches += edits.Edits.Count;
            var rectangles = new List<PdfManualRegion>();
            foreach (var edit in edits.Edits)
            {
                var span = edit.Detection.Span;
                if (span.End > page.Text.Length) throw new RedactionException(RedactionError.InvalidDocument);
                int covered = span.Start;
                foreach (var character in page.Characters.Where(character => character.Span.Start < span.End && character.Span.End > span.Start))
                {
                    if (character.Span.Start > covered || character.Bounds is not { } bounds) throw new RedactionException(RedactionError.InvalidDocument);
                    covered = Math.Min(span.End, character.Span.End);
                    PdfPoint[] points = [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
                    if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                        throw new RedactionException(RedactionError.InvalidDocument);
                    rectangles.Add(new(pageIndex, points.Min(point => point.X), points.Min(point => point.Y),
                        points.Max(point => point.X), points.Max(point => point.Y)));
                }
                if (covered != span.End) throw new RedactionException(RedactionError.InvalidDocument);
            }
            rectangles.AddRange(regions.Where(region => region.PageIndex == pageIndex));
            foreach (var rectangle in rectangles) ValidateRegion(rectangle, page.Size);
            var raster = await document.RenderPageAsync(pageIndex, width, height, cancellationToken);
            if (raster.Width != width || raster.Height != height) throw new RedactionException(RedactionError.InvalidDocument);
            var data = raster.Bgra.ToArray();
            foreach (var rectangle in rectangles)
            {
                int left = Math.Max(0, (int)Math.Floor(rectangle.Left * width / page.Size.Width) - 1);
                int top = Math.Max(0, (int)Math.Floor(rectangle.Top * height / page.Size.Height) - 1);
                int right = Math.Min(width, (int)Math.Ceiling(rectangle.Right * width / page.Size.Width) + 1);
                int bottom = Math.Min(height, (int)Math.Ceiling(rectangle.Bottom * height / page.Size.Height) + 1);
                for (int y = top; y < bottom; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = left; x < right; x++)
                    {
                        int offset = (y * width + x) * 4;
                        data[offset] = data[offset + 1] = data[offset + 2] = 0;
                        data[offset + 3] = 255;
                    }
                }
            }
            var cleaned = new PdfRaster(width, height, data);
            await writer.AddPageAsync(page.Size, cleaned, cancellationToken);
            expected.Add((page.Size, cleaned));
        }
        var output = await writer.CompleteAsync(cancellationToken);
        if (output.Length < 1 || output.Length > options.Limits.MaximumOutputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        var validationLimits = options.Limits with { MaximumInputBytes = options.Limits.MaximumOutputBytes };
        await using var reopened = await backend.OpenAsync(output, validationLimits, cancellationToken);
        if (reopened.PageCount != expected.Count) throw new RedactionException(RedactionError.InvalidDocument);
        for (int i = 0; i < expected.Count; i++)
        {
            var page = await reopened.ReadPageAsync(i, cancellationToken);
            if (page.Text.Length != 0 || page.Size != expected[i].Size) throw new RedactionException(RedactionError.InvalidDocument);
            var raster = await reopened.RenderPageAsync(i, expected[i].Raster.Width, expected[i].Raster.Height, cancellationToken);
            if (!raster.Bgra.Span.SequenceEqual(expected[i].Raster.Bgra.Span)) throw new RedactionException(RedactionError.InvalidDocument);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private async ValueTask<ReadOnlyMemory<byte>> ProcessNativeAsync(ReadOnlyMemory<byte> input, ExperimentalPdfOptions options,
        IReadOnlyList<PdfManualRegion> manual, CancellationToken cancellationToken)
    {
        if (backend is not IPdfNativeObjectBackend native) throw new RedactionException(RedactionError.UnsupportedFormat);
        var regions = manual.Select(region => new PdfObjectRemovalRegion(region.PageIndex, region.Left, region.Top, region.Right, region.Bottom)).ToList();
        var sizes = new List<PdfPageSize>();
        int matches = 0;
        await using (var document = await backend.OpenAsync(input, options.Limits, cancellationToken))
        {
            if (document.PageCount < 1 || document.PageCount > options.Limits.MaximumPages) throw new RedactionException(RedactionError.LimitExceeded);
            if (manual.Any(region => region.PageIndex < 0 || region.PageIndex >= document.PageCount)) throw new ArgumentException("Invalid manual page.");
            for (int index = 0; index < document.PageCount; index++)
            {
                var page = await document.ReadPageAsync(index, cancellationToken);
                ValidatePage(page, options.Limits);
                sizes.Add(page.Size);
                foreach (var region in manual.Where(region => region.PageIndex == index)) ValidateRegion(region, page.Size);
                EditRedactionResult edits;
                try { edits = await redactor.RedactWithEditsAsync(page.Text, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { throw new RedactionException(RedactionError.ExtensionFailed); }
                ValidateEdits(page.Text, edits);
                if (edits.Edits.Count > options.MaximumMatches - matches) throw new RedactionException(RedactionError.LimitExceeded);
                matches += edits.Edits.Count;
                foreach (var edit in edits.Edits)
                {
                    int covered = edit.Detection.Span.Start;
                    foreach (var character in page.Characters.Where(character => character.Span.Overlaps(edit.Detection.Span)))
                    {
                        if (character.Span.Start > covered || character.Bounds is not { } bounds) throw new RedactionException(RedactionError.InvalidDocument);
                        covered = Math.Min(edit.Detection.Span.End, character.Span.End);
                        PdfPoint[] points = [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
                        var region = new PdfManualRegion(index, points.Min(point => point.X), points.Min(point => point.Y),
                            points.Max(point => point.X), points.Max(point => point.Y));
                        ValidateRegion(region, page.Size);
                        if (regions.Count >= options.Limits.MaximumCharactersPerPage) throw new RedactionException(RedactionError.LimitExceeded);
                        regions.Add(new(index, region.Left, region.Top, region.Right, region.Bottom));
                    }
                    if (covered != edit.Detection.Span.End) throw new RedactionException(RedactionError.InvalidDocument);
                }
            }
        }
        var output = await native.RemoveObjectsAsync(input, regions, options.Limits, cancellationToken);
        if (output.IsEmpty || output.Length > options.Limits.MaximumOutputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        await using var reopened = await backend.OpenAsync(output, options.Limits with { MaximumInputBytes = options.Limits.MaximumOutputBytes }, cancellationToken);
        if (reopened.PageCount != sizes.Count) throw new RedactionException(RedactionError.InvalidDocument);
        for (int index = 0; index < sizes.Count; index++)
        {
            var page = await reopened.ReadPageAsync(index, cancellationToken);
            ValidatePage(page, options.Limits);
            if (page.Size != sizes[index]) throw new RedactionException(RedactionError.InvalidDocument);
            EditRedactionResult remaining;
            try { remaining = await redactor.RedactWithEditsAsync(page.Text, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { throw new RedactionException(RedactionError.ExtensionFailed); }
            ValidateEdits(page.Text, remaining);
            if (remaining.Edits.Count != 0) throw new RedactionException(RedactionError.InvalidDocument);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private void ValidatePage(PdfPageText page, PdfBackendLimits limits)
    {
        if (!double.IsFinite(page.Size.Width) || !double.IsFinite(page.Size.Height) || page.Size.Width <= 0 || page.Size.Height <= 0 ||
            page.Size.Width > limits.MaximumPageDimensionPoints || page.Size.Height > limits.MaximumPageDimensionPoints ||
            page.Text.Length > limits.MaximumCharactersPerPage || page.Characters.Count > limits.MaximumCharactersPerPage)
            throw new RedactionException(RedactionError.LimitExceeded);
        int end = 0;
        foreach (var character in page.Characters)
        {
            if (character.Span.Length < 1 || character.Span.Start != end || character.Span.End > page.Text.Length ||
                SplitsSurrogate(page.Text, character.Span.Start) || SplitsSurrogate(page.Text, character.Span.End))
                throw new RedactionException(RedactionError.InvalidDocument);
            end = character.Span.End;
        }
        if (end != page.Text.Length) throw new RedactionException(RedactionError.InvalidDocument);
    }

    private void ValidateEdits(string text, EditRedactionResult result)
    {
        if (result is null) throw new RedactionException(RedactionError.InvalidDetection);
        var rebuilt = new System.Text.StringBuilder();
        int end = 0;
        foreach (var edit in result.Edits)
        {
            var span = edit.Detection.Span;
            if (span.Start < end || span.End > text.Length || SplitsSurrogate(text, span.Start) || SplitsSurrogate(text, span.End))
                throw new RedactionException(RedactionError.InvalidDetection);
            rebuilt.Append(text.AsSpan(end, span.Start - end));
            rebuilt.Append(edit.Replacement);
            end = span.End;
        }
        rebuilt.Append(text.AsSpan(end));
        if (!string.Equals(rebuilt.ToString(), result.Text, StringComparison.Ordinal)) throw new RedactionException(RedactionError.InvalidDetection);
    }

    private bool SplitsSurrogate(string text, int offset) => offset > 0 && offset < text.Length &&
        char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);

    private void ValidateRegion(PdfManualRegion region, PdfPageSize size)
    {
        if (!double.IsFinite(region.Left) || !double.IsFinite(region.Top) || !double.IsFinite(region.Right) || !double.IsFinite(region.Bottom) ||
            region.Left < 0 || region.Top < 0 || region.Right > size.Width || region.Bottom > size.Height || region.Right <= region.Left || region.Bottom <= region.Top)
            throw new RedactionException(RedactionError.InvalidDocument);
    }
}
