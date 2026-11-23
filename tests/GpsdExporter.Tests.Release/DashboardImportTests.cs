using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using GpsdExporter.Testing;
using GpsdExporter.Testing.Grafana;

namespace GpsdExporter.Tests.Release;

/// <summary>
/// EXT-904: <c>grafana-dashboard.json</c> imports cleanly into current Grafana and every panel
/// shows data from a FakeGpsd replay. The published binary scrapes FakeGpsd; Prometheus scrapes
/// the binary every second; Grafana, with Prometheus as its data source, imports the dashboard
/// and runs every panel's queries. All three share the runner's network (host networking).
/// Runs only with <c>GPSD_EXPORTER_BINARY</c> set, in the binaries workflow.
/// </summary>
public sealed partial class DashboardImportTests
{
    // Current releases, by tag; the Prometheus version matches the CI promtool.
    private const string GrafanaImage = "grafana/grafana:13.3.1";
    private const string PrometheusImage = "prom/prometheus:v3.5.0";
    private const string AdminPassword = "gpsd-exporter-test";

    private static readonly TimeSpan StartLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DataLimit = TimeSpan.FromMinutes(2);

    // A replay that touches every panel: a 3D fix with full TPV and SKY, multi-GNSS satellites
    // on several signals, PPS and TOFF, and a line the exporter cannot parse.
    private static readonly string[] ReplayFixtures = ["session-3d-fix.jsonl", "session-multi-gnss.jsonl", "session-timing.jsonl"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_dashboard_imports_into_Grafana_and_every_panel_shows_data()
    {
        var binary = PublishedBinary.Path;
        var dashboard = GrafanaDashboard.Load();

        await using var gpsd = FakeGpsd.CreateStarted();
        var exporterPort = Scraping.FreePort();
        var prometheusPort = Scraping.FreePort();
        var grafanaPort = Scraping.FreePort();

        using var exporter = PublishedBinary.Start("-d", gpsd.Address, "-l", $"127.0.0.1:{exporterPort}");
        var output = new OutputCollector(exporter);
        using var replayStop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task? replay = null;
        try
        {
            var connection = await gpsd.AcceptAsync(Ct);
            await connection.WaitForLineAsync(l => l.StartsWith("?WATCH=", StringComparison.Ordinal), Ct);
            replay = ReplayAsync(connection, replayStop.Token);

            await using var prometheus = Prometheus(exporterPort, prometheusPort);
            await using var grafana = Grafana(prometheusPort, grafanaPort);
            await Task.WhenAll(prometheus.StartAsync(Ct), grafana.StartAsync(Ct));

            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{grafanaPort}") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"admin:{AdminPassword}")));
            await WaitForGrafanaAsync(http, grafana);

            await ImportAsync(http, dashboard);
            await WaitForEveryPanelAsync(http, dashboard);
        }
        catch (Exception e) when (e is TimeoutException or ShouldAssertException)
        {
            throw new InvalidOperationException($"{e.Message}\nExporter output:\n{output.Text}", e);
        }
        finally
        {
            await replayStop.CancelAsync();
            if (replay is not null)
            {
                await replay.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }

            exporter.Kill(entireProcessTree: true);
            await exporter.WaitForExitAsync(CancellationToken.None);
        }
    }

    /// <summary>Replays the fixtures over and over, so no series goes stale while Grafana looks.</summary>
    private static async Task ReplayAsync(FakeGpsdConnection connection, CancellationToken cancellationToken)
    {
        var lines = ReplayFixtures
            .SelectMany(Fixtures.Load)
            .Where(l => !SessionSetup().IsMatch(l))
            .Append("this is not JSON")
            .ToList();

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var line in lines)
            {
                await connection.SendLineAsync(line, cancellationToken);
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private static IContainer Prometheus(int exporterPort, int prometheusPort)
    {
        var config = $"""
            global:
              scrape_interval: 1s
              evaluation_interval: 1s
            scrape_configs:
              - job_name: gpsd
                static_configs:
                  - targets: ["127.0.0.1:{exporterPort}"]
            """;
        return new ContainerBuilder(PrometheusImage)
            .WithResourceMapping(Encoding.UTF8.GetBytes(config), FilePath.Of("/etc/prometheus/prometheus.yml"), fileMode: ReadableByAll)
            .WithCommand(
                "--config.file=/etc/prometheus/prometheus.yml",
                "--storage.tsdb.path=/prometheus",
                $"--web.listen-address=127.0.0.1:{prometheusPort}")
            .WithCreateParameterModifier(HostNetwork)
            .Build();
    }

    private static IContainer Grafana(int prometheusPort, int grafanaPort)
    {
        var datasource = $"""
            apiVersion: 1
            datasources:
              - name: Prometheus
                type: prometheus
                uid: prometheus
                access: proxy
                url: http://127.0.0.1:{prometheusPort}
                isDefault: true
                jsonData:
                  timeInterval: 1s
            """;
        return new ContainerBuilder(GrafanaImage)
            .WithResourceMapping(Encoding.UTF8.GetBytes(datasource), FilePath.Of("/etc/grafana/provisioning/datasources/prometheus.yaml"), fileMode: ReadableByAll)
            .WithEnvironment("GF_SERVER_HTTP_ADDR", "127.0.0.1")
            .WithEnvironment("GF_SERVER_HTTP_PORT", grafanaPort.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", AdminPassword)
            .WithEnvironment("GF_ANALYTICS_REPORTING_ENABLED", "false")
            .WithEnvironment("GF_ANALYTICS_CHECK_FOR_UPDATES", "false")
            .WithEnvironment("GF_PLUGINS_PREINSTALL_DISABLED", "true")
            .WithCreateParameterModifier(HostNetwork)
            .Build();
    }

    private static async Task WaitForGrafanaAsync(HttpClient http, IContainer grafana)
    {
        var deadline = DateTime.UtcNow + StartLimit;
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(new Uri("/api/datasources/uid/prometheus", UriKind.Relative), Ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            if (DateTime.UtcNow > deadline)
            {
                var (stdout, stderr) = await grafana.GetLogsAsync(ct: CancellationToken.None);
                throw new TimeoutException($"Grafana did not start with the Prometheus data source.\n{stdout}\n{stderr}");
            }

            await Task.Delay(500, Ct);
        }
    }

    private static async Task ImportAsync(HttpClient http, GrafanaDashboard dashboard)
    {
        var body = new JsonObject { ["dashboard"] = dashboard.Json.DeepClone(), ["overwrite"] = true };
        using var response = await http.PostAsJsonAsync(new Uri("/api/dashboards/db", UriKind.Relative), body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue($"The import failed ({(int)response.StatusCode}): {text}");
        ((string?)JsonNode.Parse(text)?["status"]).ShouldBe("success", text);

        // Read it back: Grafana kept every panel.
        var uid = (string?)dashboard.Json["uid"];
        var stored = JsonNode.Parse(await http.GetStringAsync(new Uri($"/api/dashboards/uid/{uid}", UriKind.Relative), Ct));
        var panels = stored?["dashboard"]?["panels"]?.AsArray().Count ?? 0;
        panels.ShouldBe(dashboard.Panels.Count + dashboard.Rows.Count, "Grafana did not keep every panel.");
    }

    /// <summary>
    /// Runs every query through Grafana until each panel has data and no query fails. A panel
    /// shows data when any of its queries does (a disconnects series, for example, is rightly
    /// empty here).
    /// </summary>
    private static async Task WaitForEveryPanelAsync(HttpClient http, GrafanaDashboard dashboard)
    {
        var deadline = DateTime.UtcNow + DataLimit;
        var report = string.Empty;
        while (true)
        {
            var results = new List<(DashboardQuery Query, string? Error, bool HasData)>();
            foreach (var query in dashboard.Queries)
            {
                var (error, hasData) = await RunAsync(http, query);
                results.Add((query, error, hasData));
            }

            var errors = results.Where(r => r.Error is not null).ToList();
            var empty = results.GroupBy(r => r.Query.Panel).Where(g => !g.Any(r => r.HasData)).Select(g => g.Key).ToList();
            if (errors.Count == 0 && empty.Count == 0)
            {
                return;
            }

            report = string.Join('\n', errors.Select(e => $"Error: {e.Query}: {e.Error}").Concat(empty.Select(p => $"No data: {p}")));
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Not every panel showed data:\n{report}");
            }

            await Task.Delay(2000, Ct);
        }
    }

    private static async Task<(string? Error, bool HasData)> RunAsync(HttpClient http, DashboardQuery query)
    {
        // A variable's label_values(series, label) is answered from the series itself.
        var variable = LabelValues().Match(query.Expr);
        var expr = GrafanaDashboard.Interpolate(variable.Success ? variable.Groups["series"].Value : query.Expr);

        var body = new JsonObject
        {
            ["from"] = "now-2m",
            ["to"] = "now",
            ["queries"] = new JsonArray(new JsonObject
            {
                ["refId"] = "A",
                ["datasource"] = new JsonObject { ["type"] = "prometheus", ["uid"] = "prometheus" },
                ["expr"] = expr,
                ["instant"] = query.Instant,
                ["range"] = !query.Instant,
                ["intervalMs"] = 1000,
                ["maxDataPoints"] = 120,
            }),
        };
        using var response = await http.PostAsJsonAsync(new Uri("/api/ds/query", UriKind.Relative), body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var result = JsonNode.Parse(text)?["results"]?["A"];
        var error = (string?)result?["error"];
        if (!response.IsSuccessStatusCode || result is null || error is not null)
        {
            return (error ?? $"HTTP {(int)response.StatusCode}: {text}", false);
        }

        var hasData = (result["frames"]?.AsArray() ?? []).Any(frame =>
            (frame?["data"]?["values"]?.AsArray() ?? []).Any(column => column is JsonArray { Count: > 0 }));
        return (null, hasData);
    }

    private static void HostNetwork(Docker.DotNet.Models.CreateContainerParameters parameters) =>
        (parameters.HostConfig ??= new()).NetworkMode = "host";

    private static UnixFileModes ReadableByAll =>
        UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.GroupRead | UnixFileModes.OtherRead;

    // VERSION, DEVICES and WATCH are the session's start; FakeGpsd has already sent its VERSION.
    [GeneratedRegex("""^\{"class":"(VERSION|DEVICES|WATCH)"[,}]""")]
    private static partial Regex SessionSetup();

    [GeneratedRegex(@"^label_values\((?<series>[^,]+),\s*[a-z_]+\)$")]
    private static partial Regex LabelValues();
}
