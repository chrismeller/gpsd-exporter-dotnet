using GpsdExporter.Connection;
using GpsdExporter.State;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Core.Connection;

/// <summary>
/// A <see cref="GpsdConnectionManager"/> on a <see cref="FakeTimeProvider"/>, pointed at a
/// FakeGpsd port. Fake time only moves when a test advances it; sockets run in real time.
/// </summary>
internal sealed class ConnectionHarness : IAsyncDisposable
{
    private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(10);
    private bool _stopped;

    public ConnectionHarness(string address, Func<GpsdOptions, GpsdOptions>? configure = null)
    {
        Options = new GpsdOptions { Address = address };
        Options = configure?.Invoke(Options) ?? Options;
        Store = new StateStore(Time);
        Manager = new GpsdConnectionManager(Microsoft.Extensions.Options.Options.Create(Options), Store, Stats, Time, Logger);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));

    public GpsdOptions Options { get; }

    public StateStore Store { get; }

    public ConnectionStats Stats { get; } = new();

    public FakeLogger<GpsdConnectionManager> Logger { get; } = new();

    public GpsdConnectionManager Manager { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task StartAsync() => Manager.StartAsync(Ct);

    public async Task StopAsync()
    {
        if (!_stopped)
        {
            _stopped = true;
            await Manager.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Waits in real time, without moving fake time, until the condition holds.</summary>
    /// <summary>
    /// Waits for a log entry. The manager logs each event just after updating its counters, so
    /// a test that asserts on the log waits for the log, not the counter.
    /// </summary>
    public Task WaitForLog(Func<FakeLogRecord, bool> match, string because) =>
        WaitUntil(() => Logger.Collector.GetSnapshot().Any(match), because);

    public static async Task WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + RealTimeLimit;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            await Task.Delay(5, Ct);
        }
    }

    /// <summary>
    /// Moves fake time forward in steps, letting the manager react between them, until the
    /// condition holds. Fails if it does not hold within <paramref name="max"/> of fake time.
    /// Returns the fake time it took.
    /// </summary>
    public async Task<TimeSpan> AdvanceUntil(Func<bool> condition, TimeSpan step, TimeSpan max, string because)
    {
        var advanced = TimeSpan.Zero;
        while (!condition())
        {
            if (advanced >= max)
            {
                throw new TimeoutException($"{because} did not happen within {max} of fake time.");
            }

            Time.Advance(step);
            advanced += step;
            await Task.Delay(20, Ct);
        }

        return advanced;
    }

    /// <summary>Gives the manager a moment of real time to react, without moving fake time.</summary>
    public static Task Settle() => Task.Delay(150, Ct);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Manager.Dispose();
    }
}
