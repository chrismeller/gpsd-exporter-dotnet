using GpsdExporter.Configuration;
using GpsdExporter.Connection;
using GpsdExporter.State;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GpsdExporter.Tests.Host;

/// <summary>The exporter's settings reach its options, and invalid ones stop the host before it starts.</summary>
public sealed class OptionsTests
{
    [Fact]
    public async Task Settings_bind_to_the_options()
    {
        await using var factory = Factory(
            ("Gpsd:Address", "gpsd.lan:2948"),
            ("Gpsd:Device", "/dev/ttyACM0"),
            ("Gpsd:Pps", "false"),
            ("Staleness:Tpv", "00:00:15"),
            ("Http:Listen", "127.0.0.1:9100"));

        var gpsd = factory.Services.GetRequiredService<IOptions<GpsdOptions>>().Value;
        gpsd.Address.ShouldBe("gpsd.lan:2948");
        gpsd.Device.ShouldBe("/dev/ttyACM0");
        gpsd.Pps.ShouldBeFalse();
        factory.Services.GetRequiredService<IOptions<StalenessOptions>>().Value.Tpv.ShouldBe(TimeSpan.FromSeconds(15));
        factory.Services.GetRequiredService<IOptions<HttpOptions>>().Value.Listen.ShouldBe("127.0.0.1:9100");
    }

    [Fact]
    public async Task Unset_settings_keep_the_defaults()
    {
        await using var factory = Factory();

        factory.Services.GetRequiredService<IOptions<GpsdOptions>>().Value.ShouldBe(new GpsdOptions());
        factory.Services.GetRequiredService<IOptions<StalenessOptions>>().Value.ShouldBe(new StalenessOptions());
        factory.Services.GetRequiredService<IOptions<HttpOptions>>().Value.Listen.ShouldBeNull();
    }

    [Fact]
    public async Task The_config_file_is_not_watched()
    {
        await using var factory = Factory();

        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();
        configuration.Providers.OfType<JsonConfigurationProvider>()
            .ShouldAllBe(p => !p.Source.ReloadOnChange);
    }

    [Fact]
    public async Task The_test_host_settings_still_reach_the_host()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.UseEnvironment("Testing"));

        factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.ShouldBe("Testing");
    }

    private static WebApplicationFactory<Program> Factory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }
        });
}
