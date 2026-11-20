namespace NuntivusRedact.Pdf.PdfPig;

internal sealed class BudgetedReadStream(byte[] input, PdfInspectionLimits limits, CancellationToken cancellationToken) : Stream
{
    private readonly MemoryStream source = new(input, writable: false);
    private long operations;
    private long bytesRead;
    internal bool LimitExceeded { get; private set; }

    private void Check()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++operations > limits.MaximumStreamOperations) FailLimit();
    }

    private void FailLimit()
    {
        LimitExceeded = true;
        throw new RedactionException(RedactionError.LimitExceeded);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get { Check(); return source.Length; } }
    public override long Position
    {
        get { Check(); return source.Position; }
        set
        {
            Check();
            if (value < 0 || value > source.Length) throw new RedactionException(RedactionError.InvalidDocument);
            source.Position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        Check();
        long remaining = limits.MaximumReadBytes - bytesRead;
        if (remaining == 0 && source.Position < source.Length && !buffer.IsEmpty) FailLimit();
        int count = source.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
        bytesRead += count;
        return count;
    }

    public override int ReadByte()
    {
        Check();
        if (bytesRead >= limits.MaximumReadBytes && source.Position < source.Length) FailLimit();
        int value = source.ReadByte();
        if (value >= 0) bytesRead++;
        return value;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Check();
        long target;
        try
        {
            target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(source.Position + offset),
                SeekOrigin.End => checked(source.Length + offset),
                _ => throw new RedactionException(RedactionError.InvalidDocument)
            };
        }
        catch (OverflowException) { throw new RedactionException(RedactionError.InvalidDocument); }
        if (target < 0 || target > source.Length) throw new RedactionException(RedactionError.InvalidDocument);
        source.Position = target;
        return target;
    }

    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}
