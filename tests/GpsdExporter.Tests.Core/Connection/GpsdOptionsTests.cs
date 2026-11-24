using GpsdExporter.Connection;

namespace GpsdExporter.Tests.Core.Connection;

public sealed class GpsdOptionsTests
{
    [Fact]
    public void the_default_watch_command_is_exact()
    {
        new GpsdOptions().WatchCommand.ShouldBe("""?WATCH={"enable":true,"json":true,"pps":true};""");
    }

    [Fact]
    public void a_configured_device_is_added()
    {
        new GpsdOptions { Device = "/dev/ttyACM0" }.WatchCommand
            .ShouldBe("""?WATCH={"enable":true,"json":true,"pps":true,"device":"/dev/ttyACM0"};""");
    }

    [Fact]
    public void Pps_off_leaves_pps_out()
    {
        new GpsdOptions { Pps = false }.WatchCommand.ShouldBe("""?WATCH={"enable":true,"json":true};""");
    }

    [Fact]
    public void A_device_path_is_json_escaped()
    {
        new GpsdOptions { Device = "/dev/a\"b\\c" }.WatchCommand.ShouldContain("""
            "device":"/dev/a\"b\\c"
            """);
    }

    [Fact]
    public void The_defaults_are_valid()
    {
        new GpsdOptions().Validate().ShouldBeEmpty();
    }

    [Fact]
    public void a_device_that_makes_the_watch_line_longer_than_80_characters_is_rejected()
    {
        // The command without a device is 46 characters; ,"device":"" adds 12.
        var fits = new GpsdOptions { Device = "/dev/" + new string('x', 80 - 46 - 12 - 5) };
        fits.WatchCommand.Length.ShouldBe(80);
        fits.Validate().ShouldBeEmpty();

        var tooLong = fits with { Device = fits.Device + "x" };
        tooLong.WatchCommand.Length.ShouldBe(81);
        tooLong.Validate().ShouldHaveSingleItem().ShouldContain("81 characters");
    }

    [Theory]
    [InlineData("localhost:2947", "localhost", 2947)]
    [InlineData("gps.example:1234", "gps.example", 1234)]
    [InlineData("127.0.0.1:2947", "127.0.0.1", 2947)]
    [InlineData("[::1]:2948", "::1", 2948)]
    [InlineData("[fe80::1%eth0]:2947", "fe80::1%eth0", 2947)]
    [InlineData("localhost", "localhost", 2947)]
    [InlineData("[::1]", "::1", 2947)]
    public void Addresses_are_host_and_port(string address, string host, int port)
    {
        GpsdOptions.TryParseAddress(address, out var h, out var p).ShouldBeTrue();
        (h, p).ShouldBe((host, port));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(":2947")]
    [InlineData("host:")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:-1")]
    [InlineData("host:12ab")]
    [InlineData("::1")]
    [InlineData("::1:2947")]
    [InlineData("[]:2947")]
    [InlineData("[::1]2947")]
    [InlineData("[::1")]
    public void Bad_addresses_are_rejected(string address)
    {
        GpsdOptions.TryParseAddress(address, out _, out _).ShouldBeFalse();
        (new GpsdOptions { Address = address }).Validate().ShouldNotBeEmpty();
    }

    [Fact]
    public void Timings_must_be_positive_and_ordered()
    {
        var options = new GpsdOptions
        {
            ConnectTimeout = TimeSpan.Zero,
            KeepaliveIdle = TimeSpan.FromSeconds(-1),
            ReconnectInitialDelay = TimeSpan.FromSeconds(10),
            ReconnectMaxDelay = TimeSpan.FromSeconds(5),
        };

        var errors = options.Validate();

        errors.Count.ShouldBe(3);
        errors.ShouldContain(e => e.Contains("ConnectTimeout", StringComparison.Ordinal));
        errors.ShouldContain(e => e.Contains("KeepaliveIdle", StringComparison.Ordinal));
        errors.ShouldContain(e => e.Contains("ReconnectMaxDelay", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_device_is_rejected()
    {
        new GpsdOptions { Device = "" }.Validate().ShouldNotBeEmpty();
    }
}
