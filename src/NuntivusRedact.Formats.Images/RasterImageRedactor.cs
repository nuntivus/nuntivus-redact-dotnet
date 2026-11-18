using System.Buffers.Binary;
using System.Text;
using NuntivusRedact.Ocr;
using SkiaSharp;

namespace NuntivusRedact.Formats.Images;

/// <summary>Optional in-process PNG/JPEG redactor. OCR can miss content; native parsing is not sandboxed. Dispose only after all operations finish.</summary>
public sealed class RasterImageRedactor : IImageRedactor, IDisposable
{
    private readonly IOcrProvider provider;
    private readonly ImageLimits limits;
    private readonly SemaphoreSlim gate;

    /// <summary>Creates a reusable image pipeline. The caller owns the provider and must supply a trustworthy implementation.</summary>
    public RasterImageRedactor(IOcrProvider provider, ImageLimits? limits = null)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.limits = limits ?? new ImageLimits();
        this.limits.Validate();
        gate = new SemaphoreSlim(this.limits.MaximumConcurrency);
    }

    /// <inheritdoc />
    public async ValueTask<ImageRedactionResult> RedactAsync(ReadOnlyMemory<byte> image, string contentType,
        IEditAwareTextRedactor redactor, long maximumPixels, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumPixels < 1 || image.Length > limits.MaximumInputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        if (contentType is not "image/png" and not "image/jpeg") throw new RedactionException(RedactionError.UnsupportedFormat);
        if (contentType == "image/png") ValidatePng(image.Span);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var data = SKData.CreateCopy(image.Span);
            using var codec = SKCodec.Create(data);
            if (codec is null) throw new RedactionException(RedactionError.InvalidDocument);
            var expected = contentType == "image/png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
            if (codec.EncodedFormat != expected || codec.FrameCount > 1 || codec.EncodedOrigin != SKEncodedOrigin.TopLeft)
                throw new RedactionException(RedactionError.UnsupportedFormat);
            int width = codec.Info.Width, height = codec.Info.Height;
            if (width <= 0 || height <= 0 || width > limits.MaximumDimension || height > limits.MaximumDimension ||
                (long)width * height > Math.Min(maximumPixels, limits.MaximumPixels))
                throw new RedactionException(RedactionError.LimitExceeded);
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var decoded = new SKBitmap(info);
            if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success) throw new RedactionException(RedactionError.InvalidDocument);
            cancellationToken.ThrowIfCancellationRequested();
            using var raster = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(raster);
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(decoded, 0, 0);
            canvas.Flush();
            var canonical = Encode(raster);
            OcrResult recognized;
            try { recognized = await provider.RecognizeAsync(canonical, width, height, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (RedactionException error) { throw new RedactionException(error.Error); }
            catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
            ValidateRecognition(recognized, width, height, cancellationToken);
            EditRedactionResult edits;
            try { edits = await redactor.RedactWithEditsAsync(recognized.Text, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (RedactionException error) { throw new RedactionException(error.Error); }
            catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
            ValidateEdits(recognized.Text, edits, cancellationToken);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false, BlendMode = SKBlendMode.Src };
            int wordIndex = 0;
            foreach (var edit in edits.Edits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool covered = false;
                while (wordIndex < recognized.Words.Count && recognized.Words[wordIndex].Span.End <= edit.Detection.Span.Start) wordIndex++;
                for (int i = wordIndex; i < recognized.Words.Count && recognized.Words[i].Span.Start < edit.Detection.Span.End; i++)
                {
                    var word = recognized.Words[i];
                    var b = word.Bounds;
                    canvas.DrawRect(SKRect.Create(Math.Max(0, b.Left - limits.PaddingPixels), Math.Max(0, b.Top - limits.PaddingPixels),
                        Math.Min(width, b.Right + limits.PaddingPixels) - Math.Max(0, b.Left - limits.PaddingPixels),
                        Math.Min(height, b.Bottom + limits.PaddingPixels) - Math.Max(0, b.Top - limits.PaddingPixels)), paint);
                    covered = true;
                }
                if (!covered) throw new RedactionException(RedactionError.InvalidDetection);
            }
            canvas.Flush();
            cancellationToken.ThrowIfCancellationRequested();
            var png = Encode(raster);
            using var verification = SKBitmap.Decode(png);
            if (verification is null || verification.Width != width || verification.Height != height)
                throw new RedactionException(RedactionError.InvalidDocument);
            return new ImageRedactionResult(png, width, height, edits.Edits.Select(e => e.Detection));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.InvalidDocument); }
        finally { gate.Release(); }
    }

    private void ValidatePng(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new RedactionException(RedactionError.InvalidDocument);
        int offset = 8;
        while (offset <= png.Length - 12)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.Slice(offset, 4));
            if (length < 0 || length > png.Length - offset - 12) throw new RedactionException(RedactionError.InvalidDocument);
            var type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
                throw new RedactionException(RedactionError.UnsupportedFormat);
            offset += length + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || offset != png.Length) throw new RedactionException(RedactionError.InvalidDocument);
                return;
            }
        }
        throw new RedactionException(RedactionError.InvalidDocument);
    }

    private byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null) throw new RedactionException(RedactionError.InvalidDocument);
        if (encoded.Size > limits.MaximumOutputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        return encoded.ToArray();
    }

    private void ValidateRecognition(OcrResult result, int width, int height, CancellationToken token)
    {
        if (result is null) throw new RedactionException(RedactionError.InvalidDetection);
        if (result.Text.Length > limits.MaximumTextLength || result.Words.Count > limits.MaximumWords)
            throw new RedactionException(RedactionError.LimitExceeded);
        int end = 0;
        foreach (var word in result.Words)
        {
            token.ThrowIfCancellationRequested();
            if (word is null || word.Span.Length < 1 || word.Span.Start < end || word.Span.End > result.Text.Length ||
                SplitsSurrogate(result.Text, word.Span.Start) || SplitsSurrogate(result.Text, word.Span.End))
                throw new RedactionException(RedactionError.InvalidDetection);
            if (!WhiteSpace(result.Text, end, word.Span.Start)) throw new RedactionException(RedactionError.InvalidDetection);
            var b = word.Bounds;
            if (b.Left < 0 || b.Top < 0 || b.Right <= b.Left || b.Bottom <= b.Top || b.Right > width || b.Bottom > height ||
                (word.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1)))
                throw new RedactionException(RedactionError.InvalidDetection);
            end = word.Span.End;
        }
        if (!WhiteSpace(result.Text, end, result.Text.Length)) throw new RedactionException(RedactionError.InvalidDetection);
    }

    private void ValidateEdits(string text, EditRedactionResult result, CancellationToken token)
    {
        if (result is null) throw new RedactionException(RedactionError.InvalidDetection);
        if (result.Text.Length > limits.MaximumTextLength || result.Edits.Count > limits.MaximumDetections)
            throw new RedactionException(RedactionError.LimitExceeded);
        var rebuilt = new StringBuilder();
        int end = 0;
        foreach (var edit in result.Edits)
        {
            token.ThrowIfCancellationRequested();
            var span = edit.Detection.Span;
            if (span.Start < end || span.End > text.Length || SplitsSurrogate(text, span.Start) || SplitsSurrogate(text, span.End))
                throw new RedactionException(RedactionError.InvalidDetection);
            Append(rebuilt, text.AsSpan(end, span.Start - end));
            Append(rebuilt, edit.Replacement);
            end = span.End;
        }
        Append(rebuilt, text.AsSpan(end));
        if (!string.Equals(rebuilt.ToString(), result.Text, StringComparison.Ordinal)) throw new RedactionException(RedactionError.InvalidDetection);
    }

    private void Append(StringBuilder builder, ReadOnlySpan<char> value)
    {
        if (value.Length > limits.MaximumTextLength - builder.Length) throw new RedactionException(RedactionError.LimitExceeded);
        builder.Append(value);
    }

    private bool WhiteSpace(string text, int start, int end)
    {
        for (int i = start; i < end; i++) if (!char.IsWhiteSpace(text[i])) return false;
        return true;
    }

    private bool SplitsSurrogate(string value, int offset) => offset > 0 && offset < value.Length && char.IsHighSurrogate(value[offset - 1]) && char.IsLowSurrogate(value[offset]);

    /// <summary>Releases concurrency resources, not the caller-owned OCR provider.</summary>
    public void Dispose() => gate.Dispose();
}
