using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.ML.Tokenizers;

namespace NuntivusRedact.Nlp.Onnx;

internal readonly record struct SourceToken(int Id, int Start, int End, int WordStart, int WordEnd);

internal sealed class CasedWordPieceTokenizer
{
    private readonly BertTokenizer tokenizer;
    private static readonly string[] specialTokens = ["[UNK]", "[SEP]", "[PAD]", "[CLS]", "[MASK]"];

    internal CasedWordPieceTokenizer(byte[] vocabulary)
    {
        using var stream = new MemoryStream(vocabulary, false);
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 1024, true))
        {
            int count = 0;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            while (reader.ReadLine() is { } line)
            {
                if (++count > 256_000 || line.Length is 0 or > 256 || !unique.Add(line))
                    throw new RedactionException(RedactionError.UnsupportedFormat);
            }
        }
        stream.Position = 0;
        tokenizer = BertTokenizer.Create(stream, new BertOptions
        {
            LowerCaseBeforeTokenization = false,
            RemoveNonSpacingMarks = false,
            IndividuallyTokenizeCjk = false,
            ApplyBasicTokenization = false,
            SplitOnSpecialTokens = false,
            MaxInputCharsPerWord = 100
        });
    }

    internal int ClassificationTokenId => tokenizer.ClassificationTokenId;
    internal int SeparatorTokenId => tokenizer.SeparatorTokenId;
    internal int PaddingTokenId => tokenizer.PaddingTokenId;

    internal IReadOnlyList<SourceToken> Encode(string text, int maximumTokens, CancellationToken cancellationToken)
    {
        var normalized = new StringBuilder(text.Length);
        var positions = new List<int>(text.Length);
        for (int i = 0; i < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out int consumed) != OperationStatus.Done)
                throw new RedactionException(RedactionError.InvalidDocument);
            var category = Rune.GetUnicodeCategory(rune);
            bool whitespace = IsWhitespace(rune);
            if (rune.Value is not (0 or 0xfffd) && (whitespace || category is not (UnicodeCategory.Control or UnicodeCategory.Format)))
            {
                if (whitespace)
                {
                    normalized.Append(' ');
                    positions.Add(i);
                }
                else
                {
                    normalized.Append(text.AsSpan(i, consumed));
                    for (int j = 0; j < consumed; j++) positions.Add(i + j);
                }
            }
            i += consumed;
        }

        string cleaned = normalized.ToString();
        var tokens = new List<SourceToken>();
        int wordStart = -1;
        void AddWord(int start, int end)
        {
            if (start < 0 || start == end) return;
            cancellationToken.ThrowIfCancellationRequested();
            var encoded = tokenizer.EncodeToTokens(cleaned.AsSpan(start, end - start), out _,
                considerPreTokenization: false, considerNormalization: false);
            if (encoded.Count > maximumTokens - tokens.Count) throw new RedactionException(RedactionError.LimitExceeded);
            foreach (var token in encoded)
            {
                var (offset, length) = token.Offset.GetOffsetAndLength(end - start);
                if (length == 0) throw new RedactionException(RedactionError.InvalidDetection);
                tokens.Add(new(token.Id, positions[start + offset], positions[start + offset + length - 1] + 1,
                    positions[start], positions[end - 1] + 1));
            }
        }

        for (int i = 0; i < cleaned.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var special = cleaned[i] == '[' ? specialTokens.FirstOrDefault(t => cleaned.AsSpan(i).StartsWith(t, StringComparison.Ordinal)) : null;
            if (special is not null)
            {
                AddWord(wordStart, i);
                wordStart = -1;
                AddWord(i, i + special.Length);
                i += special.Length;
                continue;
            }
            var rune = Rune.GetRuneAt(cleaned, i);
            int next = i + rune.Utf16SequenceLength;
            bool whitespace = IsWhitespace(rune);
            bool separate = IsPunctuation(rune) || IsCjk(rune.Value);
            if (whitespace || separate)
            {
                AddWord(wordStart, i);
                wordStart = -1;
                if (separate) AddWord(i, next);
            }
            else if (wordStart < 0) wordStart = i;
            i = next;
        }
        AddWord(wordStart, cleaned.Length);
        return tokens.AsReadOnly();
    }

    private static bool IsWhitespace(Rune rune) => rune.Value is ' ' or '\t' or '\n' or '\r' ||
        Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator;

    private static bool IsPunctuation(Rune rune) => rune.Value is >= 33 and <= 47 or >= 58 and <= 64 or >= 91 and <= 96 or >= 123 and <= 126 ||
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or
        UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or
        UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;

    private static bool IsCjk(int value) => value is >= 0x4e00 and <= 0x9fff or >= 0x3400 and <= 0x4dbf or
        >= 0x20000 and <= 0x2a6df or >= 0x2a700 and <= 0x2b73f or >= 0x2b740 and <= 0x2b81f or
        >= 0x2b820 and <= 0x2ceaf or >= 0xf900 and <= 0xfaff or >= 0x2f800 and <= 0x2fa1f;
}
