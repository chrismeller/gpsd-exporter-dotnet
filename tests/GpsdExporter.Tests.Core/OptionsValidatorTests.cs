using GpsdExporter.Connection;
using GpsdExporter.State;

namespace GpsdExporter.Tests.Core;

/// <summary>The generated validators report the options' own rules.</summary>
public sealed class OptionsValidatorTests
{
    [Fact]
    public void Valid_gpsd_options_pass() =>
        new GpsdOptionsValidator().Validate(null, new GpsdOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void Invalid_gpsd_options_fail_with_every_rule_they_break()
    {
        var options = new GpsdOptions { Address = "[::1", ConnectTimeout = TimeSpan.Zero };

        var result = new GpsdOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldBe(options.Validate(), ignoreOrder: true);
    }

    [Fact]
    public void Valid_staleness_options_pass() =>
        new StalenessOptionsValidator().Validate(null, new StalenessOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void Invalid_staleness_options_fail_with_every_rule_they_break()
    {
        var options = new StalenessOptions { Tpv = TimeSpan.Zero, Osc = TimeSpan.FromSeconds(-1) };

        var result = new StalenessOptionsValidator().Validate(null, options);

        result.Failures.ShouldBe(["Staleness:Tpv must be positive.", "Staleness:Osc must be positive."], ignoreOrder: true);
    }
}
