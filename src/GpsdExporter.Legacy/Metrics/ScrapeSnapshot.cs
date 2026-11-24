using GpsdExporter.State;

namespace GpsdExporter.Metrics;

/// <summary>
/// The <see cref="StateSnapshot"/> every gpsd instrument callback reads during one scrape, so
/// the whole scrape comes from one state and one "now" (AC4.7). The <c>/metrics</c> pipeline
/// pins a snapshot before the exporter collects and releases it afterwards; scrapes are
/// serialised so one cannot replace another's snapshot. Outside a pinned scrape (a collection
/// not started from <c>/metrics</c>) each read takes a fresh snapshot.
/// </summary>
internal sealed class ScrapeSnapshot(StateStore store) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StateSnapshot? _pinned;

    public StateSnapshot Current => Volatile.Read(ref _pinned) ?? store.Snapshot();

    /// <summary>Waits for any other scrape to finish, then pins a snapshot until disposed.</summary>
    public async Task<IDisposable> PinAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _pinned, store.Snapshot());
        return new Pin(this);
    }

    public void Dispose() => _gate.Dispose();

    private void Release()
    {
        Volatile.Write(ref _pinned, null);
        _gate.Release();
    }

    private sealed class Pin(ScrapeSnapshot owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
