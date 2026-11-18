using System.Buffers.Binary;
using System.Text;
using global::Tesseract;

namespace NuntivusRedact.Ocr.Tesseract;

/// <summary>Optional .NET Tesseract adapter. Native calls run in-process and are cancellable only between calls. Dispose after active calls finish.</summary>
public sealed class TesseractOcrProvider : IOcrProvider, IDisposable
{
    private readonly TesseractOptions options;
    private readonly SemaphoreSlim gate;
    private readonly string dataPath;

    /// <summary>Validates trusted language files. Each operation owns and disposes its own native engine; the host owns native deployment.</summary>
    public TesseractOcrProvider(TesseractOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        dataPath = Path.GetFullPath(options.DataPath);
        gate = new SemaphoreSlim(options.MaximumConcurrency);
    }

    /// <inheritdoc />
    public async ValueTask<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> png, int width, int height, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (png.Length > options.MaximumInputBytes || width < 1 || height < 1 || (long)width * height > options.MaximumPixels)
            throw new RedactionException(RedactionError.LimitExceeded);
        if (png.Length < 33 || !png.Span[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !png.Span.Slice(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)) != width ||
            BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)) != height)
            throw new RedactionException(RedactionError.InvalidDocument);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var engine = new TesseractEngine(dataPath, options.Languages, EngineMode.LstmOnly);
            if (!engine.SetVariable("debug_file", OperatingSystem.IsWindows() ? "NUL" : "/dev/null"))
                throw new RedactionException(RedactionError.ExtensionFailed);
            cancellationToken.ThrowIfCancellationRequested();
            using var pixels = Pix.LoadFromMemory(png.ToArray());
            if (pixels.Width != width || pixels.Height != height) throw new RedactionException(RedactionError.InvalidDocument);
            using var page = engine.Process(pixels, PageSegMode.Auto);
            using var iterator = page.GetIterator();
            var text = new StringBuilder();
            var words = new List<OcrWord>();
            iterator.Begin();
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = iterator.GetText(PageIteratorLevel.Word)?.Trim();
                if (string.IsNullOrEmpty(value)) continue;
                if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) throw new RedactionException(RedactionError.InvalidDetection);
                if (words.Count >= options.MaximumWords || value.Length + (long)text.Length + 1 > options.MaximumTextLength)
                    throw new RedactionException(RedactionError.LimitExceeded);
                if (text.Length != 0) text.Append(iterator.IsAtBeginningOf(PageIteratorLevel.TextLine) ? '\n' : ' ');
                int start = text.Length;
                text.Append(value);
                double confidence = iterator.GetConfidence(PageIteratorLevel.Word) / 100d;
                if (!double.IsFinite(confidence) || confidence is < 0 or > 1 || box.X1 < 0 || box.Y1 < 0 || box.X2 > width || box.Y2 > height || box.X1 >= box.X2 || box.Y1 >= box.Y2)
                    throw new RedactionException(RedactionError.InvalidDetection);
                words.Add(new OcrWord(new DetectionSpan(start, value.Length), new OcrBounds(box.X1, box.Y1, box.X2, box.Y2), confidence));
            } while (iterator.Next(PageIteratorLevel.Word));
            cancellationToken.ThrowIfCancellationRequested();
            return new OcrResult(text.ToString(), words);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
        finally { gate.Release(); }
    }

    /// <summary>Releases provider concurrency resources. No shared native engine or global mutable configuration is retained.</summary>
    public void Dispose() => gate.Dispose();
}
