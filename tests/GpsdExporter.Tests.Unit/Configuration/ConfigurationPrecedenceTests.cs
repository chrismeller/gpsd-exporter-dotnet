using GpsdExporter.Configuration;
using Microsoft.Extensions.Configuration;

namespace GpsdExporter.Tests.Unit.Configuration;

/// <summary>
/// Precedence, highest first: command line, <c>GPSD_EXPORTER_</c> environment variables,
/// <c>appsettings.json</c>, defaults. These tests set process environment variables, so they
/// run on their own.
/// </summary>
[Collection(Name)]
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfigurationPrecedenceTests : IDisposable
{
    public const string Name = "Process environment";

    private const string AddressVariable = "GPSD_EXPORTER_GPSD__ADDRESS";
    private const string StalenessVariable = "GPSD_EXPORTER_STALENESS__TPV";

    public ConfigurationPrecedenceTests()
    {
        Environment.SetEnvironmentVariable(AddressVariable, null);
        Environment.SetEnvironmentVariable(StalenessVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AddressVariable, null);
        Environment.SetEnvironmentVariable(StalenessVariable, null);
    }

    [Fact]
    public void Appsettings_overrides_the_default() =>
        Load(json: "from-json:1", env: null, args: []).Gpsd.Address.ShouldBe("from-json:1");

    [Fact]
    public void Environment_overrides_appsettings() =>
        Load(json: "from-json:1", env: "from-env:2", args: []).Gpsd.Address.ShouldBe("from-env:2");

    [Fact]
    public void Command_line_overrides_environment() =>
        Load(json: "from-json:1", env: "from-env:2", args: ["-d", "from-flag:3"]).Gpsd.Address.ShouldBe("from-flag:3");

    [Fact]
    public void Section_key_on_the_command_line_overrides_environment()
    {
        Environment.SetEnvironmentVariable(StalenessVariable, "00:00:20");

        Load(json: null, env: null, args: ["--Staleness:Tpv=00:00:25"]).Staleness.Tpv.ShouldBe(TimeSpan.FromSeconds(25));
        Load(json: null, env: null, args: []).Staleness.Tpv.ShouldBe(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Unset_everywhere_is_the_default() =>
        Load(json: null, env: null, args: []).Gpsd.Address.ShouldBe("localhost:2947");

    private static ExporterSettings Load(string? json, string? env, string[] args)
    {
        Environment.SetEnvironmentVariable(AddressVariable, env);
        var commandLine = CommandLine.Parse(args);
        commandLine.Errors.ShouldBeEmpty();

        // In the host, appsettings.json is one of the default sources, below these.
        var builder = new ConfigurationBuilder();
        if (json is not null)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Gpsd:Address"] = json });
        }

        builder.AddExporterSources(commandLine.ConfigArgs);
        var (settings, errors) = ExporterSettings.Load(builder.Build());
        errors.ShouldBeEmpty();
        return settings!;
    }
}
