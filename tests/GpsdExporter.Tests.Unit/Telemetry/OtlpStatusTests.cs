using GpsdExporter.Configuration;
using GpsdExporter.Telemetry;

namespace GpsdExporter.Tests.Unit.Telemetry;

/// <summary>Where OTLP export goes, resolved as the OpenTelemetry SDK resolves it, and the startup warnings.</summary>
public sealed class OtlpStatusTests
{
    [Fact]
    public void Off_with_no_OTEL_variables_has_nothing_to_say()
    {
        var status = OtlpStatus.From(new OtlpOptions());

        status.Enabled.ShouldBeFalse();
        status.Signals.ShouldBeEmpty();
        status.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void OTEL_variables_without_an_endpoint_warn_that_OTLP_is_off()
    {
        var status = OtlpStatus.From(Options(new()
        {
            ["OTEL_SERVICE_NAME"] = "gps",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        }));

        status.Enabled.ShouldBeFalse();
        status.Warnings.ShouldHaveSingleItem().ShouldBe(
            "OTLP export is off: OTEL_EXPORTER_OTLP_PROTOCOL, OTEL_SERVICE_NAME are set, but no endpoint is. "
            + "Set OTEL_EXPORTER_OTLP_ENDPOINT or Otlp:Endpoint to turn it on.");
    }

    [Fact]
    public void The_standard_endpoint_defaults_to_gRPC_and_is_used_as_is()
    {
        var status = OtlpStatus.From(Options(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317" }));

        status.Enabled.ShouldBeTrue();
        status.Signals.ShouldBe(
        [
            new OtlpSignal("metrics", new Uri("http://collector:4317"), "grpc"),
            new OtlpSignal("logs", new Uri("http://collector:4317"), "grpc"),
        ]);
        status.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void With_http_protobuf_the_standard_endpoint_gets_each_signal_path()
    {
        var status = OtlpStatus.From(Options(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318/otlp/",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        }));

        status.Signals.ShouldBe(
        [
            new OtlpSignal("metrics", new Uri("http://collector:4318/otlp/v1/metrics"), "http/protobuf"),
            new OtlpSignal("logs", new Uri("http://collector:4318/otlp/v1/logs"), "http/protobuf"),
        ]);
        status.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Per_signal_variables_win_over_the_general_ones()
    {
        var status = OtlpStatus.From(Options(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317",
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = "http://logs:4318/custom/path",
            ["OTEL_EXPORTER_OTLP_LOGS_PROTOCOL"] = "http/protobuf",
        }));

        status.Signals.ShouldBe(
        [
            new OtlpSignal("metrics", new Uri("http://collector:4317"), "grpc"),
            new OtlpSignal("logs", new Uri("http://logs:4318/custom/path"), "http/protobuf"),
        ]);
    }

    [Fact]
    public void A_signal_with_no_endpoint_of_its_own_goes_to_the_SDK_default()
    {
        var status = OtlpStatus.From(Options(new() { ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "http://collector:4317" }));

        status.Signals.ShouldBe(
        [
            new OtlpSignal("metrics", new Uri("http://collector:4317"), "grpc"),
            new OtlpSignal("logs", new Uri("http://localhost:4317"), "grpc"),
        ]);
    }

    [Fact]
    public void Otlp_Endpoint_wins_over_the_standard_variables()
    {
        var status = OtlpStatus.From(Options(
            new()
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://ignored:4317",
                ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "http://ignored:4317",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            },
            endpoint: "http://collector:4318"));

        status.Signals.ShouldBe(
        [
            new OtlpSignal("metrics", new Uri("http://collector:4318/v1/metrics"), "http/protobuf"),
            new OtlpSignal("logs", new Uri("http://collector:4318/v1/logs"), "http/protobuf"),
        ]);
    }

    [Fact]
    public void gRPC_to_the_HTTP_port_warns_about_the_protocol()
    {
        var status = OtlpStatus.From(Options(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4318" }));

        status.Enabled.ShouldBeTrue();
        status.Warnings.Count.ShouldBe(2);
        status.Warnings[0].ShouldBe(
            "OTLP metrics go to port 4318, the usual OTLP/HTTP port, over gRPC, the .NET default. "
            + "If the collector expects HTTP there, set OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf.");
        status.Warnings[1].ShouldStartWith("OTLP logs go to port 4318");
    }

    [Fact]
    public void HTTP_to_the_gRPC_port_warns_about_the_protocol()
    {
        var status = OtlpStatus.From(Options(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        }));

        status.Warnings.Count.ShouldBe(2);
        status.Warnings[0].ShouldBe(
            "OTLP metrics go to port 4317, the usual OTLP/gRPC port, over http/protobuf. "
            + "If the collector expects gRPC there, set OTEL_EXPORTER_OTLP_PROTOCOL=grpc or use port 4318.");
    }

    [Fact]
    public void An_unsupported_protocol_warns_once_and_falls_back_to_gRPC()
    {
        var status = OtlpStatus.From(Options(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://collector:4317",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/json",
        }));

        status.Signals.ShouldAllBe(s => s.Protocol == "grpc");
        status.Warnings.ShouldHaveSingleItem().ShouldBe(
            "OTEL_EXPORTER_OTLP_PROTOCOL 'http/json' is not supported, so grpc is used. Use grpc or http/protobuf.");
    }

    [Fact]
    public void An_endpoint_that_is_not_a_URL_warns()
    {
        var status = OtlpStatus.From(Options(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "collector:4317" }));

        status.Signals.ShouldAllBe(s => s.Endpoint == new Uri("http://localhost:4317"));
        status.Warnings.ShouldHaveSingleItem().ShouldBe("OTEL_EXPORTER_OTLP_ENDPOINT 'collector:4317' is not an absolute http or https URL and is ignored.");
    }

    [Theory]
    [InlineData("\"Authorization=Bearer abc\"")]
    [InlineData("'Authorization=Bearer abc'")]
    public void A_value_wrapped_in_quotes_warns(string headers)
    {
        var status = OtlpStatus.From(Options(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://collector",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = headers,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        }));

        status.Enabled.ShouldBeTrue();
        status.Warnings.ShouldHaveSingleItem().ShouldBe(
            "OTEL_EXPORTER_OTLP_HEADERS is wrapped in quotes, which are part of its value. "
            + "Docker's --env-file and the list form of Compose's environment keep quotes; remove them.");
    }

    [Fact]
    public void Quotes_inside_a_value_do_not_warn()
    {
        var status = OtlpStatus.From(Options(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://collector",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "Authorization=Bearer \"abc\"x",
        }));

        status.Warnings.ShouldBeEmpty();
    }

    private static OtlpOptions Options(Dictionary<string, string> standard, string? endpoint = null) =>
        new() { Endpoint = endpoint, Standard = standard };
}
