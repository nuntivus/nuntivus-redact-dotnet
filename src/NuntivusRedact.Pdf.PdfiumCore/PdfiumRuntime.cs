using PDFiumCore;

namespace NuntivusRedact.Pdf.PdfiumCore;

/// <summary>Caller-owned native runtime. Supply one shared instance to all adapters in a process; do not mix other PDFium users.
/// Native calls are serialized, but not sandboxed or hard-cancelable. Dispose sessions/writers before this runtime.</summary>
public sealed class PdfiumRuntime : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim admission;
    private bool disposed;
    private int documents;
    private readonly int maximumOpenDocuments;

    /// <summary>Initializes PDFium. Bounds active plus waiting calls; does not bound native memory or execution time.</summary>
    /// <param name="maximumOutstandingCalls">The maximum number of outstanding asynchronous calls to allow.</param>
    /// <param name="maximumOpenDocuments">The maximum number of open documents/writers to allow.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumOutstandingCalls"/> or <paramref name="maximumOpenDocuments"/> is less than 1.</exception>
    public PdfiumRuntime(int maximumOutstandingCalls = 8, int maximumOpenDocuments = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumOutstandingCalls, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumOpenDocuments, 1);
        admission = new SemaphoreSlim(maximumOutstandingCalls, maximumOutstandingCalls);
        this.maximumOpenDocuments = maximumOpenDocuments;
        try { fpdfview.FPDF_InitLibrary(); }
        catch { throw new RedactionException(RedactionError.ExtensionFailed); }
    }

    internal async ValueTask<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (!await admission.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new RedactionException(RedactionError.LimitExceeded);
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            }
            catch (RedactionException) { throw; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ObjectDisposedException) { throw; }
            catch (ArgumentException) { throw; }
            catch { throw new RedactionException(RedactionError.ExtensionFailed); }
            finally { gate.Release(); }
        }
        finally { admission.Release(); }
    }

    internal async ValueTask CleanupAsync(Action cleanup)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cleanup();
        }
        catch (RedactionException) { throw; }
        catch (ObjectDisposedException) { throw; }
        catch { throw new RedactionException(RedactionError.ExtensionFailed); }
        finally { gate.Release(); }
    }

    internal void AddDocument()
    {
        if (documents >= maximumOpenDocuments)
            throw new RedactionException(RedactionError.LimitExceeded);
        documents++;
    }
    internal void RemoveDocument() => documents--;

    /// <summary>Waits for the current native call. Rejects disposal while sessions/writers remain open; can be retried.</summary>
    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            if (documents != 0) throw new InvalidOperationException("Dispose PDF sessions and writers first.");
            fpdfview.FPDF_DestroyLibrary();
            disposed = true;
        }
        finally { gate.Release(); }
        // Waiting operations must be able to observe disposal; no semaphore wait handles are created.
    }
}
