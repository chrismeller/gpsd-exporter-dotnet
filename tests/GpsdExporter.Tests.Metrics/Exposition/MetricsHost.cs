using System.Text;
using GpsdExporter.Configuration;
using GpsdExporter.Connection;
using GpsdExporter.Metrics;
using GpsdExporter.Protocol;
using GpsdExporter.State;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Prometheus;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GpsdExporter.Tests.Metrics.Exposition;

/// <summary>
/// The real host, in process, with <c>/metrics</c> scraped over the test server and time on a
/// <see cref="FakeTimeProvider"/>. Without a gpsd address there is no connection: lines are
/// parsed and applied to the host's <see cref="StateStore"/> exactly as the connection manager
/// would. With one (a FakeGpsd), the real connection manager runs against it.
/// </summary>
internal sealed class MetricsHost : IAsyncDisposable
{
    private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MetricsHost(
        MetricsOptions? metrics = null,
        string? gpsdAddress = null,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            web.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Time);
                services.RemoveAll<MetricsOptions>();
                services.AddSingleton(metrics ?? new MetricsOptions());

                if (gpsdAddress is null)
                {
                    // No gpsd connection: tests feed the state store directly.
                    services.Remove(services.Single(d => d.ImplementationType == typeof(GpsdConnectionManager)));
                }
                else
                {
                    services.RemoveAll<GpsdOptions>();
                    services.AddSingleton(new GpsdOptions { Address = gpsdAddress });
                }

                configureServices?.Invoke(services);
            });
        });

        // Settings go in as GPSD_EXPORTER_ environment variables while the host is built, the
        // way a deployment would set them. (WebApplicationFactory's UseSetting passes settings
        // as command-line arguments, including a bare "--Section=" for each section, which the
        // exporter's command line rejects.) Every host test runs in one serial collection, so
        // no other host sees them.
        var saved = new Dictionary<string, string?>();
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            var name = ExporterSettings.EnvironmentPrefix + key.Replace(":", "__", StringComparison.Ordinal);
            saved[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            _client = _factory.CreateClient();
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));

    /// <summary>A client for the in-process test server.</summary>
    public HttpClient Client => _client;

    public IServiceProvider Services => _factory.Services;

    public StateStore Store => _factory.Services.GetRequiredService<StateStore>();

    public ConnectionStats Stats => _factory.Services.GetRequiredService<ConnectionStats>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Starts a host connected to <paramref name="gpsd"/> and waits until VERSION is processed.</summary>
    public static async Task<(MetricsHost Host, FakeGpsdConnection Connection)> ConnectAsync(FakeGpsd gpsd)
    {
        ArgumentNullException.ThrowIfNull(gpsd);
        var host = new MetricsHost(gpsdAddress: gpsd.Address);
        try
        {
            var connection = await gpsd.AcceptAsync(Ct);
            await WaitUntil(() => host.Stats.Up, "VERSION is processed");
            return (host, connection);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Sends the lines, then a WATCH object as a marker, and waits until the marker has been
    /// processed, so every line before it has been too. The marker adds 1 to
    /// <c>gpsd_exporter_messages_total{class="WATCH"}</c>.
    /// </summary>
    public async Task SendAndWaitAsync(FakeGpsdConnection connection, IEnumerable<byte[]> lines)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(lines);
        // WATCH objects among the lines count too, so wait for all of them plus the marker.
        var expected = Stats.Messages(GpsdClass.Watch) + 1;
        foreach (var line in lines)
        {
            expected += line.AsSpan().IndexOf("\"class\":\"WATCH\""u8) >= 0 ? 1 : 0;
            await connection.SendLineAsync(line, Ct);
        }

        await connection.SendLineAsync(WatchMarker, Ct);
        await WaitUntil(() => Stats.Messages(GpsdClass.Watch) >= expected, "the lines are processed");
    }

    public Task SendAndWaitAsync(FakeGpsdConnection connection, IEnumerable<string> lines) =>
        SendAndWaitAsync(connection, lines.Select(Encoding.UTF8.GetBytes));

    public const string WatchMarker = """{"class":"WATCH","enable":true,"json":true}""";

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

    /// <summary>Advances fake time in steps, with a moment of real time after each, until <paramref name="condition"/> holds.</summary>
    public async Task AdvanceUntil(Func<bool> condition, TimeSpan step, TimeSpan max, string because)
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
    }

    /// <summary>Parses and applies each line; returns every parse result.</summary>
    public IReadOnlyList<ParseResult> Apply(params IEnumerable<string> lines)
    {
        var results = new List<ParseResult>();
        foreach (var line in lines)
        {
            var result = GpsdParser.Parse(Encoding.UTF8.GetBytes(line));
            if (result.Report is { } report)
            {
                Store.Apply(report);
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>Applies every line of a fixture.</summary>
    public IReadOnlyList<ParseResult> Replay(string fixture) => Apply(Fixtures.Load(fixture));

    public async Task<string> ScrapeTextAsync()
    {
        using var response = await _client.GetAsync(new Uri("/metrics", UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Ct);
    }

    public async Task<PrometheusText> ScrapeAsync() => PrometheusText.Parse(await ScrapeTextAsync());

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
