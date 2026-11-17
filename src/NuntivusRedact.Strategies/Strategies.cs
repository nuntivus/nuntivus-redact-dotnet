using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NuntivusRedact.Strategies;

/// <summary>Replaces a match with a constant; does not promise complete discovery.</summary>
public sealed class FixedReplacementStrategy : IRedactionStrategy
{
    private readonly string replacement;
    /// <summary>Creates a fixed replacement strategy.</summary>
    public FixedReplacementStrategy(string replacement = "[REDACTED]") => this.replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    /// <inheritdoc />
    public string Redact(ReadOnlySpan<char> source, Detection detection) => replacement;
}

/// <summary>Replaces matches with their configured category label.</summary>
public sealed class CategoryReplacementStrategy : IRedactionStrategy
{
    /// <inheritdoc />
    public string Redact(ReadOnlySpan<char> source, Detection detection) { ArgumentNullException.ThrowIfNull(detection); return $"[{detection.Category}]"; }
}

/// <summary>Masks each Unicode text element with one character, hiding source characters but revealing element count.</summary>
public sealed class FullMaskingStrategy : IRedactionStrategy
{
    private readonly char mask;
    /// <summary>Creates a mask; surrogate mask code units are not allowed.</summary>
    public FullMaskingStrategy(char mask = '*')
    {
        if (char.IsSurrogate(mask)) throw new ArgumentException("Mask must not be a surrogate.", nameof(mask));
        this.mask = mask;
    }
    /// <inheritdoc />
    public string Redact(ReadOnlySpan<char> source, Detection detection) => new(mask, StringInfo.ParseCombiningCharacters(source.ToString()).Length);
}

/// <summary>Deliberately preserves some text elements. This is masking, not anonymization.</summary>
public sealed class PartialMaskingStrategy : IRedactionStrategy
{
    private readonly int keepStart;
    private readonly int keepEnd;
    private readonly char mask;
    /// <summary>Creates a partial mask. Matches too short to hide at least one element are fully masked.</summary>
    public PartialMaskingStrategy(int keepStart = 0, int keepEnd = 4, char mask = '*')
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepStart);
        ArgumentOutOfRangeException.ThrowIfNegative(keepEnd);
        if (char.IsSurrogate(mask)) throw new ArgumentException("Mask must not be a surrogate.", nameof(mask));
        this.keepStart = keepStart;
        this.keepEnd = keepEnd;
        this.mask = mask;
    }
    /// <inheritdoc />
    public string Redact(ReadOnlySpan<char> source, Detection detection)
    {
        var text = source.ToString();
        var elements = StringInfo.ParseCombiningCharacters(text);
        if ((long)keepStart + keepEnd >= elements.Length) return new string(mask, elements.Length);
        int prefixEnd = elements[keepStart];
        int suffixStart = keepEnd == 0 ? text.Length : elements[elements.Length - keepEnd];
        return string.Concat(text.AsSpan(0, prefixEnd), new string(mask, elements.Length - keepStart - keepEnd), text.AsSpan(suffixStart));
    }
}

/// <summary>Keyed deterministic HMAC-SHA256 pseudonyms. Linkable, not anonymous; protect the application-owned key.</summary>
public sealed class HmacPseudonymizationStrategy : IRedactionStrategy
{
    private readonly byte[] key;
    /// <summary>Copies an application key of at least 32 bytes. Keep instances within the key's intended lifetime.</summary>
    public HmacPseudonymizationStrategy(ReadOnlySpan<byte> key)
    {
        if (key.Length < 32) throw new ArgumentException("At least 32 key bytes are required.", nameof(key));
        this.key = key.ToArray();
    }
    /// <inheritdoc />
    public string Redact(ReadOnlySpan<char> source, Detection detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        var category = Encoding.UTF8.GetBytes(detection.Category);
        var value = Encoding.UTF8.GetBytes(source.ToString());
        var payload = new byte[checked(4 + category.Length + value.Length)];
        try
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(payload, category.Length);
            category.CopyTo(payload, 4);
            value.CopyTo(payload, 4 + category.Length);
            return "[PSEUDO:" + Convert.ToHexString(HMACSHA256.HashData(key, payload)) + "]";
        }
        finally { CryptographicOperations.ZeroMemory(value); CryptographicOperations.ZeroMemory(payload); }
    }
}
