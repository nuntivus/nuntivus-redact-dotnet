namespace NuntivusRedact.Pdf.PdfPig;

internal sealed class ObjectStreamHeader
{
    internal long[] Numbers { get; }

    internal ObjectStreamHeader(ReadOnlySpan<byte> data, int count, int first, CancellationToken cancellationToken)
    {
        if (first < 1 || first >= data.Length) throw new RedactionException(RedactionError.InvalidDocument);
        Numbers = new long[count];
        var seen = new HashSet<long>();
        int cursor = 0;
        long previousOffset = -1;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long number = ReadInteger(data[..first], ref cursor);
            long offset = ReadInteger(data[..first], ref cursor);
            if (number < 1 || !seen.Add(number) || offset <= previousOffset || offset >= data.Length - first || i == 0 && offset != 0)
                throw new RedactionException(RedactionError.InvalidDocument);
            Numbers[i] = number;
            previousOffset = offset;
        }
        while (cursor < first && IsWhitespace(data[cursor])) cursor++;
        if (cursor != first) throw new RedactionException(RedactionError.InvalidDocument);
    }

    private long ReadInteger(ReadOnlySpan<byte> header, ref int cursor)
    {
        while (cursor < header.Length && IsWhitespace(header[cursor])) cursor++;
        int start = cursor;
        long result = 0;
        while (cursor < header.Length && header[cursor] is >= (byte)'0' and <= (byte)'9')
        {
            int digit = header[cursor++] - '0';
            if (result > (long.MaxValue - digit) / 10) throw new RedactionException(RedactionError.InvalidDocument);
            result = result * 10 + digit;
        }
        if (cursor == start || cursor < header.Length && !IsWhitespace(header[cursor]))
            throw new RedactionException(RedactionError.InvalidDocument);
        return result;
    }

    private bool IsWhitespace(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;
}
