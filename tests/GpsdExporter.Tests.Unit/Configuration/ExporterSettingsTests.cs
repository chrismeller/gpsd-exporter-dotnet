using GpsdExporter.Configuration;
using GpsdExporter.Connection;
using GpsdExporter.State;
using Microsoft.Extensions.Configuration;

namespace GpsdExporter.Tests.Unit.Configuration;

/// <summary>Binding and validation of every setting in the spec's Configuration table.</summary>
public sealed class ExporterSettingsTests
{
    [Fact]
    public void Empty_configuration_gives_the_spec_defaults()
    {
        var settings = Load([]);

        settings.ShouldBe(ExporterSettings.Default);
        settings.Gpsd.Address.ShouldBe("localhost:2947");
        settings.Gpsd.Pps.ShouldBeTrue();
        settings.Gpsd.ConnectTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        settings.Gpsd.ReconnectInitialDelay.ShouldBe(TimeSpan.FromSeconds(1));
        settings.Gpsd.ReconnectMaxDelay.ShouldBe(TimeSpan.FromSeconds(30));
        settings.Gpsd.KeepaliveIdle.ShouldBe(TimeSpan.FromSeconds(15));
        settings.Gpsd.KeepaliveTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        settings.Staleness.Tpv.ShouldBe(TimeSpan.FromSeconds(10));
        settings.Staleness.Sky.ShouldBe(TimeSpan.FromSeconds(30));
        settings.Metrics.Satellites.ShouldBeTrue();
        settings.Http.Listen.ShouldBe(":9978");
        settings.Http.MetricsPath.ShouldBe("/metrics");
        settings.LogFormat.ShouldBe(LogFormat.Text);
    }

    [Fact]
    public void Every_setting_binds()
    {
        var settings = Load(new()
        {
            ["Gpsd:Address"] = "[fe80::1]:2948",
            ["Gpsd:Device"] = "/dev/ttyACM0",
            ["Gpsd:Pps"] = "false",
            ["Gpsd:ConnectTimeout"] = "00:00:07",
            ["Gpsd:ReconnectInitialDelay"] = "00:00:02",
            ["Gpsd:ReconnectMaxDelay"] = "00:01:00",
            ["Gpsd:KeepaliveIdle"] = "00:00:20",
            ["Gpsd:KeepaliveTimeout"] = "00:00:03",
            ["Staleness:Tpv"] = "00:00:11",
            ["Staleness:Sky"] = "00:00:31",
            ["Staleness:Pps"] = "00:00:12",
            ["Staleness:Toff"] = "00:00:13",
            ["Staleness:Osc"] = "00:00:32",
            ["Metrics:Satellites"] = "false",
            ["Http:Listen"] = "127.0.0.1:9100",
            ["Http:MetricsPath"] = "/gps/metrics",
            ["Logging:Format"] = "JSON",
        });

        settings.Gpsd.ShouldBe(new GpsdOptions
        {
            Address = "[fe80::1]:2948",
            Device = "/dev/ttyACM0",
            Pps = false,
            ConnectTimeout = TimeSpan.FromSeconds(7),
            ReconnectInitialDelay = TimeSpan.FromSeconds(2),
            ReconnectMaxDelay = TimeSpan.FromMinutes(1),
            KeepaliveIdle = TimeSpan.FromSeconds(20),
            KeepaliveTimeout = TimeSpan.FromSeconds(3),
        });
        settings.Staleness.ShouldBe(new StalenessOptions
        {
            Tpv = TimeSpan.FromSeconds(11),
            Sky = TimeSpan.FromSeconds(31),
            Pps = TimeSpan.FromSeconds(12),
            Toff = TimeSpan.FromSeconds(13),
            Osc = TimeSpan.FromSeconds(32),
        });
        settings.Metrics.Satellites.ShouldBeFalse();
        settings.Http.ShouldBe(new HttpOptions { Listen = "127.0.0.1:9100", MetricsPath = "/gps/metrics" });
        settings.LogFormat.ShouldBe(LogFormat.Json);
    }

    [Theory]
    [InlineData("Gpsd:Address", "gpsd:notaport", "Gpsd:Address 'gpsd:notaport' is not host:port")]
    [InlineData("Gpsd:Address", "::1:2947", "Gpsd:Address '::1:2947' is not host:port")]
    [InlineData("Gpsd:Device", "/dev/serial/by-id/usb-u-blox_AG_-_www.u-blox.com_u-blox_GNSS_receiver-if00", "the WATCH command would be")]
    [InlineData("Gpsd:ConnectTimeout", "-00:00:01", "Gpsd:ConnectTimeout must be positive.")]
    [InlineData("Gpsd:KeepaliveIdle", "00:00:00", "Gpsd:KeepaliveIdle must be positive.")]
    [InlineData("Gpsd:ReconnectInitialDelay", "00:01:00", "Gpsd:ReconnectMaxDelay must not be less than Gpsd:ReconnectInitialDelay.")]
    [InlineData("Staleness:Tpv", "00:00:00", "Staleness:Tpv must be positive.")]
    [InlineData("Staleness:Osc", "-00:00:05", "Staleness:Osc must be positive.")]
    [InlineData("Http:Listen", "9978", "Http:Listen '9978' is not [addr]:port")]
    [InlineData("Http:Listen", "gps.example:9978", "Http:Listen 'gps.example:9978' is not [addr]:port")]
    [InlineData("Http:Listen", ":65536", "Http:Listen ':65536' is not [addr]:port")]
    [InlineData("Http:MetricsPath", "metrics", "Http:MetricsPath 'metrics' must be a path starting with '/'.")]
    [InlineData("Logging:Format", "xml", "Logging:Format 'xml' must be text or json.")]
    [InlineData("Gpsd:ConnectTimeout", "five seconds", "Gpsd:ConnectTimeout")]
    [InlineData("Gpsd:Pps", "maybe", "Gpsd:Pps")]
    [InlineData("Gpsd:Adress", "localhost:2947", "Adress")]
    public void Invalid_values_are_reported(string key, string value, string message)
    {
        var (settings, errors) = ExporterSettings.Load(Build(new() { [key] = value }));

        settings.ShouldBeNull();
        errors.ShouldContain(e => e.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var (_, errors) = ExporterSettings.Load(Build(new()
        {
            ["Gpsd:Address"] = "",
            ["Staleness:Sky"] = "00:00:00",
            ["Http:Listen"] = "nope",
        }));

        errors.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(":9978", null, false, 9978)]
    [InlineData(":0", null, false, 0)]
    [InlineData("0.0.0.0:9978", "0.0.0.0", false, 9978)]
    [InlineData("127.0.0.1:9100", "127.0.0.1", false, 9100)]
    [InlineData("[::]:9978", "::", false, 9978)]
    [InlineData("[::1]:9978", "::1", false, 9978)]
    [InlineData("localhost:9978", null, true, 9978)]
    public void Listen_addresses_parse(string value, string? address, bool localhost, int port)
    {
        HttpOptions.TryParseListen(value, out var listen).ShouldBeTrue();

        listen!.Address?.ToString().ShouldBe(address);
        listen.Localhost.ShouldBe(localhost);
        listen.Port.ShouldBe(port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("9978")]
    [InlineData("::1:9978")]
    [InlineData("[127.0.0.1]:9978")]
    [InlineData("example.com:9978")]
    [InlineData(":-1")]
    [InlineData(":http")]
    public void Bad_listen_addresses_do_not_parse(string value) =>
        HttpOptions.TryParseListen(value, out _).ShouldBeFalse();

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ExporterSettings Load(Dictionary<string, string?> values)
    {
        var (settings, errors) = ExporterSettings.Load(Build(values));
        errors.ShouldBeEmpty();
        return settings.ShouldNotBeNull();
    }
}
