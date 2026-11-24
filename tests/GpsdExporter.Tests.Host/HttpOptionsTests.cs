using GpsdExporter.Configuration;

namespace GpsdExporter.Tests.Host;

/// <summary>The <c>-l</c> / <c>Http:Listen</c> address format.</summary>
public sealed class HttpOptionsTests
{
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
    [InlineData(":65536")]
    [InlineData(":http")]
    public void Bad_listen_addresses_do_not_parse(string value) =>
        HttpOptions.TryParseListen(value, out _).ShouldBeFalse();
}
