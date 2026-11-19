using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace NuntivusRedact.Nlp.Onnx;

/// <summary>Offline CPU token classification with bounded admission, cased WordPiece and BIO decoding.</summary>
/// <remarks>Owns native resources. Dispose after use. Native execution is in-process, not sandboxed; cancellation is best effort.</remarks>
public sealed class OnnxEntityRecognitionProvider : IEntityRecognitionProvider, IDisposable
{
    private readonly OnnxNerOptions options;
    private readonly CasedWordPieceTokenizer tokenizer;
    private readonly InferenceSession session;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int admitted;
    private int disposed;

    private OnnxEntityRecognitionProvider(OnnxNerOptions options, CasedWordPieceTokenizer tokenizer, InferenceSession session)
    {
        this.options = options;
        this.tokenizer = tokenizer;
        this.session = session;
    }

    /// <summary>Loads hash-verified, bounded local assets and validates the model interface. No network access occurs.</summary>
    public static async Task<OnnxEntityRecognitionProvider> CreateAsync(OnnxNerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        InferenceSession? session = null;
        try
        {
            var vocabulary = await ReadAssetAsync(options.VocabularyPath, options.VocabularySha256,
                options.MaximumVocabularyBytes, cancellationToken).ConfigureAwait(false);
            var tokenizer = new CasedWordPieceTokenizer(vocabulary);
            var model = await ReadAssetAsync(options.ModelPath, options.ModelSha256,
                options.MaximumModelBytes, cancellationToken).ConfigureAwait(false);
            using var settings = new SessionOptions
            {
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL
            };
            cancellationToken.ThrowIfCancellationRequested();
            session = new InferenceSession(model, settings);
            ValidateModel(session, options);
            cancellationToken.ThrowIfCancellationRequested();
            return new(options, tokenizer, session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session?.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }
        catch (RedactionException error) { session?.Dispose(); throw new RedactionException(error.Error); }
        catch (Exception) { session?.Dispose(); throw new RedactionException(RedactionError.ExtensionFailed); }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RecognizedEntity>> RecognizeAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length > options.MaximumInputCharacters) throw new RedactionException(RedactionError.LimitExceeded);
        if (Interlocked.Increment(ref admitted) > options.MaximumConcurrentRequests)
        {
            Interlocked.Decrement(ref admitted);
            throw new RedactionException(RedactionError.LimitExceeded);
        }
        bool entered = false;
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            if (Volatile.Read(ref disposed) != 0) throw new RedactionException(RedactionError.ExtensionFailed);
            return await Task.Run(() => Recognize(text, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (RedactionException error) { throw new RedactionException(error.Error); }
        catch (Exception) { throw new RedactionException(RedactionError.ExtensionFailed); }
        finally
        {
            if (entered) gate.Release();
            Interlocked.Decrement(ref admitted);
        }
    }

    private IReadOnlyList<RecognizedEntity> Recognize(string text, CancellationToken cancellationToken)
    {
        var tokens = tokenizer.Encode(text, options.MaximumTokens, cancellationToken);
        if (tokens.Count == 0) return Array.Empty<RecognizedEntity>();
        int capacity = options.MaximumSequenceLength - 2;
        int stride = capacity - options.OverlapTokens;
        long windows = tokens.Count <= capacity ? 1 : 1 + ((long)tokens.Count - capacity + stride - 1) / stride;
        if (windows > options.MaximumWindows) throw new RedactionException(RedactionError.LimitExceeded);
        var predictions = new int[tokens.Count];
        var confidence = new double[tokens.Count];
        var margins = Enumerable.Repeat(-1, tokens.Count).ToArray();
        using var runOptions = new RunOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL };
        using var registration = cancellationToken.Register(() => runOptions.Terminate = true);
        for (int start = 0; start < tokens.Count; start += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(capacity, tokens.Count - start);
            RunWindow(tokens, start, count, predictions, confidence, margins, runOptions, cancellationToken);
            if (start + count == tokens.Count) break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (margins.Any(margin => margin < 0)) throw new RedactionException(RedactionError.InvalidDetection);
        return Decode(tokens, predictions, confidence, options.Labels, options.MaximumEntities, cancellationToken);
    }

    private void RunWindow(IReadOnlyList<SourceToken> tokens, int start, int count, int[] predictions,
        double[] confidence, int[] margins, RunOptions runOptions, CancellationToken cancellationToken)
    {
        int size = options.MaximumSequenceLength;
        var ids = new DenseTensor<long>(new[] { 1, size });
        var attention = new DenseTensor<long>(new[] { 1, size });
        for (int i = 0; i < size; i++) ids[0, i] = tokenizer.PaddingTokenId;
        ids[0, 0] = tokenizer.ClassificationTokenId;
        attention[0, 0] = 1;
        for (int i = 0; i < count; i++)
        {
            ids[0, i + 1] = tokens[start + i].Id;
            attention[0, i + 1] = 1;
        }
        ids[0, count + 1] = tokenizer.SeparatorTokenId;
        attention[0, count + 1] = 1;
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", attention)
        };
        if (session.InputMetadata.ContainsKey("token_type_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new[] { 1, size })));
        using var output = session.Run(inputs, new[] { "logits" }, runOptions);
        cancellationToken.ThrowIfCancellationRequested();
        var logits = output.Single().AsTensor<float>();
        if (logits.Rank != 3 || logits.Dimensions[0] != 1 || logits.Dimensions[1] != size ||
            logits.Dimensions[2] != options.Labels.Count)
            throw new RedactionException(RedactionError.InvalidDetection);
        for (int position = 0; position < size; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int best = 0;
            double maximum = double.NegativeInfinity;
            for (int label = 0; label < options.Labels.Count; label++)
            {
                float value = logits[0, position, label];
                if (!float.IsFinite(value)) throw new RedactionException(RedactionError.InvalidDetection);
                if (value > maximum) { maximum = value; best = label; }
            }
            if (position == 0 || position > count) continue;
            int local = position - 1;
            int margin = Math.Min(local, count - local - 1);
            if (margin <= margins[start + local]) continue;
            double sum = 0;
            for (int label = 0; label < options.Labels.Count; label++) sum += Math.Exp(logits[0, position, label] - maximum);
            predictions[start + local] = best;
            confidence[start + local] = 1 / sum;
            margins[start + local] = margin;
        }
    }

    internal static IReadOnlyList<RecognizedEntity> Decode(IReadOnlyList<SourceToken> tokens, int[] predictions,
        double[] confidence, IReadOnlyList<string> labels, int maximumEntities, CancellationToken cancellationToken)
    {
        var entities = new List<RecognizedEntity>();
        var unique = new HashSet<(int Start, int End, string Label)>();
        int start = -1, end = -1;
        string? activeLabel = null;
        double score = 1;
        void Flush()
        {
            if (activeLabel is null) return;
            if (unique.Add((start, end, activeLabel)))
            {
                if (entities.Count == maximumEntities) throw new RedactionException(RedactionError.LimitExceeded);
                entities.Add(new(new(start, end - start), activeLabel, score));
            }
            activeLabel = null;
        }
        for (int i = 0; i < tokens.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string bio = labels[predictions[i]];
            if (bio == "O") { Flush(); continue; }
            string label = bio[2..];
            var token = tokens[i];
            bool sameWord = activeLabel == label && token.WordStart < end;
            if (activeLabel != label || bio[0] == 'B' && !sameWord) Flush();
            if (activeLabel is null)
            {
                start = token.WordStart;
                score = confidence[i];
                activeLabel = label;
            }
            else score = Math.Min(score, confidence[i]);
            end = token.WordEnd;
        }
        Flush();
        return entities.OrderBy(entity => entity.Span.Start).ThenBy(entity => entity.Span.End)
            .ThenBy(entity => entity.Label, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    private static void ValidateModel(InferenceSession session, OnnxNerOptions options)
    {
        if (session.InputMetadata.Count is < 2 or > 3 || !session.InputMetadata.ContainsKey("input_ids") ||
            !session.InputMetadata.ContainsKey("attention_mask") || session.InputMetadata.Keys.Any(name =>
                name is not ("input_ids" or "attention_mask" or "token_type_ids")) ||
            session.OutputMetadata.Count != 1 || !session.OutputMetadata.TryGetValue("logits", out var output) ||
            !output.IsTensor || output.ElementType != typeof(float) || output.Dimensions.Length != 3 ||
            !Matches(output.Dimensions[0], 1) || !Matches(output.Dimensions[1], options.MaximumSequenceLength) ||
            !Matches(output.Dimensions[2], options.Labels.Count))
            throw new RedactionException(RedactionError.UnsupportedFormat);
        foreach (var input in session.InputMetadata.Values)
            if (!input.IsTensor || input.ElementType != typeof(long) || input.Dimensions.Length != 2 ||
                !Matches(input.Dimensions[0], 1) || !Matches(input.Dimensions[1], options.MaximumSequenceLength))
                throw new RedactionException(RedactionError.UnsupportedFormat);
    }

    private static bool Matches(int actual, int expected) => actual == -1 || actual == expected;

    private static async Task<byte[]> ReadAssetAsync(string path, string expectedHash, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 1 || stream.Length > maximumBytes) throw new RedactionException(RedactionError.LimitExceeded);
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (await stream.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0)
            throw new RedactionException(RedactionError.LimitExceeded);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedHash)))
            throw new RedactionException(RedactionError.InvalidDocument);
        return bytes;
    }

    /// <summary>Rejects new work and waits for active inference before releasing native resources. Do not call from provider callbacks.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        gate.Wait();
        try { session.Dispose(); }
        finally { gate.Release(); }
    }
}
