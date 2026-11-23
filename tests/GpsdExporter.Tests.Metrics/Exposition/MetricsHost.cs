using System.Text;
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
/// The real host, in process, with the gpsd connection replaced by direct calls: lines are
/// parsed and applied to the host's <see cref="StateStore"/> exactly as the connection manager
/// would, and <c>/metrics</c> is scraped over the test server. Time is a <see cref="FakeTimeProvider"/>.
/// </summary>
internal sealed class MetricsHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MetricsHost(MetricsOptions? metrics = null)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<MetricsOptions>();
            services.AddSingleton(metrics ?? new MetricsOptions());

            // No gpsd connection: tests feed the state store directly.
            var manager = services.Single(d => d.ImplementationType == typeof(GpsdConnectionManager));
            services.Remove(manager);
        }));
        _client = _factory.CreateClient();
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));

    public StateStore Store => _factory.Services.GetRequiredService<StateStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
