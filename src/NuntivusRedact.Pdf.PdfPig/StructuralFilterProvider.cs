using System.IO.Compression;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace NuntivusRedact.Pdf.PdfPig;

internal sealed class StructuralFilterProvider(PdfInspectionLimits limits, CancellationToken cancellationToken) : IFilterProvider, IFilter
{
    private long decodedBytes;
    internal Dictionary<DictionaryToken, ObjectStreamHeader> ObjectHeaders { get; } = new(ReferenceEqualityComparer.Instance);
    internal bool Unsupported { get; private set; }
    internal bool LimitExceeded { get; private set; }
    internal bool InvalidDocument { get; private set; }
    internal bool Decoded { get; private set; }
    public bool IsSupported => true;

    private RedactionException Refuse()
    {
        Unsupported = true;
        return new RedactionException(RedactionError.UnsupportedFormat);
    }

    private RedactionException Invalid()
    {
        InvalidDocument = true;
        return new RedactionException(RedactionError.InvalidDocument);
    }

    private void Bound(bool exceeded)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!exceeded) return;
        LimitExceeded = true;
        throw new RedactionException(RedactionError.LimitExceeded);
    }

    private (long Bytes, int Width, int Predictor) ValidateXref(DictionaryToken dictionary)
    {
        if (!dictionary.Data.TryGetValue("Type", out var type) || type is not NameToken { Data: "XRef" }) throw Refuse();
        if (!dictionary.Data.TryGetValue("Size", out var size) || size is not NumericToken count || count.Long < 1 || count.Double != count.Long)
            throw Invalid();
        Bound(count.Long > (long)limits.MaximumObjects + 1);
        if (!dictionary.Data.TryGetValue("W", out var widths) || widths is not ArrayToken { Data.Count: 3 } array)
            throw Invalid();
        long width = 0;
        for (int i = 0; i < array.Data.Count; i++)
        {
            var item = array.Data[i];
            if (item is not NumericToken number || number.Double != number.Long || number.Long < 0 || number.Long > 8) throw Refuse();
            if (i == 0 && number.Long > 1 || i == 2 && number.Long > 4) throw Refuse();
            width += number.Long;
        }
        if (width == 0) throw Invalid();
        long rows = count.Long;
        if (dictionary.Data.TryGetValue("Index", out var index))
        {
            if (index is not ArrayToken entries || entries.Data.Count % 2 != 0) throw Invalid();
            Bound(entries.Data.Count > limits.MaximumTokens);
            rows = 0;
            for (int i = 0; i < entries.Data.Count; i += 2)
            {
                if (entries.Data[i] is not NumericToken first || entries.Data[i + 1] is not NumericToken length ||
                    first.Double != first.Long || length.Double != length.Long || first.Long < 0 || length.Long < 0 ||
                    first.Long > count.Long || length.Long > count.Long - first.Long)
                    throw Invalid();
                Bound(length.Long > (long)limits.MaximumObjects + 1 - rows);
                rows += length.Long;
            }
        }
        if (dictionary.Data.ContainsKey("DP")) throw Refuse();
        int predictor = 1;
        if (dictionary.Data.TryGetValue("DecodeParms", out var parameters))
        {
            if (parameters is ArrayToken { Data.Count: 1 } pipeline) parameters = pipeline.Data[0];
            if (parameters is not NullToken)
            {
                if (parameters is not DictionaryToken configuration) throw Refuse();
                predictor = ReadParameter(configuration, "Predictor", 1);
                if (predictor != 1 && (predictor < 10 || predictor > 15)) throw Refuse();
                if (ReadParameter(configuration, "Colors", 1) != 1 || ReadParameter(configuration, "BitsPerComponent", 8) != 8)
                    throw Refuse();
                if (predictor != 1 && ReadParameter(configuration, "Columns", 1) != width) throw Refuse();
                if (configuration.Data.Keys.Any(key => key is not ("Predictor" or "Colors" or "BitsPerComponent" or "Columns")))
                    throw Refuse();
            }
        }
        return (rows * width, (int)width, predictor);
    }

    private int ReadParameter(DictionaryToken dictionary, string name, int fallback)
    {
        if (!dictionary.Data.TryGetValue(name, out var token)) return fallback;
        if (token is not NumericToken number || number.Double != number.Long || number.Long < 1 || number.Long > int.MaxValue)
            throw Refuse();
        return (int)number.Long;
    }

    private (int Count, int First) ValidateObjectStream(DictionaryToken dictionary)
    {
        int count = ReadParameter(dictionary, "N", 0);
        int first = ReadParameter(dictionary, "First", 0);
        if (count < 1 || first < 1) throw Invalid();
        Bound(count > limits.MaximumObjects || (long)count * 2 > limits.MaximumTokens || first > limits.MaximumDecodedStructuralStreamBytes);
        if (dictionary.Data.ContainsKey("Extends") || dictionary.Data.ContainsKey("DP")) throw Refuse();
        if (dictionary.Data.TryGetValue("DecodeParms", out var parameters) && parameters is not NullToken) throw Refuse();
        return (count, first);
    }

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary)
    {
        dictionary.Data.TryGetValue("Type", out var type);
        bool objectStream = type is NameToken { Data: "ObjStm" };
        bool xref = type is NameToken { Data: "XRef" };
        if (xref) ValidateXref(dictionary);
        if (objectStream) ValidateObjectStream(dictionary);
        if (!dictionary.Data.TryGetValue("Filter", out var filter) && !dictionary.Data.TryGetValue("F", out filter))
        {
            if (xref || objectStream) throw Refuse();
            return [];
        }
        if (filter is NullToken || filter is ArrayToken { Data.Count: 0 })
        {
            if (xref || objectStream) throw Refuse();
            return [];
        }
        if (!xref && !objectStream) throw Refuse();
        if (filter is ArrayToken { Data.Count: 1 } pipeline) filter = pipeline.Data[0];
        if (filter is not NameToken { Data: "FlateDecode" or "Fl" }) throw Refuse();
        return [this];
    }

    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names)
    {
        if (names.Count == 0) return [];
        if (names.Count == 1 && names[0].Data is "FlateDecode" or "Fl") return [this];
        throw Refuse();
    }

    public IReadOnlyList<IFilter> GetAllFilters() => [];

    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
    {
        GetFilters(streamDictionary);
        bool objectStream = streamDictionary.Data.TryGetValue("Type", out var type) && type is NameToken { Data: "ObjStm" };
        var objectLayout = objectStream ? ValidateObjectStream(streamDictionary) : default;
        var layout = objectStream ? (Bytes: 0L, Width: 1, Predictor: 1) : ValidateXref(streamDictionary);
        long expectedBytes = layout.Bytes;
        long inflatedBytes = layout.Predictor == 1 ? expectedBytes : expectedBytes + expectedBytes / layout.Width;
        Bound(input.Length > limits.MaximumEncodedStreamBytes);
        using var source = new MemoryStream(input.ToArray(), writable: false);
        using var inflater = new ZLibStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = inflater.Read(buffer)) != 0)
        {
            Bound(read > limits.MaximumDecodedStructuralStreamBytes - output.Length ||
                read > limits.MaximumTotalDecodedStructuralStreamBytes - decodedBytes);
            decodedBytes += read;
            output.Write(buffer, 0, read);
        }
        if (!objectStream && output.Length != inflatedBytes) throw Invalid();
        var data = output.ToArray();
        if (objectStream)
        {
            try { ObjectHeaders[streamDictionary] = new ObjectStreamHeader(data, objectLayout.Count, objectLayout.First, cancellationToken); }
            catch (RedactionException) { InvalidDocument = true; throw; }
        }
        if (layout.Predictor != 1) data = ReconstructRows(data, layout.Width, layout.Predictor, expectedBytes);
        Decoded = true;
        return data;
    }

    private byte[] ReconstructRows(byte[] inflated, int width, int predictor, long expectedBytes)
    {
        Bound(expectedBytes > limits.MaximumDecodedStructuralStreamBytes ||
            expectedBytes > limits.MaximumTotalDecodedStructuralStreamBytes - decodedBytes);
        decodedBytes += expectedBytes;
        var result = new byte[(int)expectedBytes];
        for (int source = 0, target = 0; source < inflated.Length; source += width + 1, target += width)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int filter = inflated[source];
            if (filter > 4 || predictor != 15 && filter != predictor - 10) throw Invalid();
            for (int column = 0; column < width; column++)
            {
                int left = column == 0 ? 0 : result[target + column - 1];
                int above = target == 0 ? 0 : result[target + column - width];
                int upperLeft = target == 0 || column == 0 ? 0 : result[target + column - width - 1];
                int correction = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, upperLeft),
                    _ => throw Invalid()
                };
                result[target + column] = unchecked((byte)(inflated[source + column + 1] + correction));
            }
        }
        return result;
    }

    private int Paeth(int left, int above, int upperLeft)
    {
        int prediction = left + above - upperLeft;
        int leftDistance = Math.Abs(prediction - left);
        int aboveDistance = Math.Abs(prediction - above);
        int cornerDistance = Math.Abs(prediction - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= cornerDistance ? left : aboveDistance <= cornerDistance ? above : upperLeft;
    }
}
