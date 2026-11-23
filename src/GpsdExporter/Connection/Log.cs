using Microsoft.Extensions.Logging;

namespace GpsdExporter.Connection;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "Connected to gpsd at {Address}")]
    public static partial void Connected(ILogger logger, string address);

    [LoggerMessage(2, LogLevel.Information, "Disconnected from gpsd: {Reason}")]
    public static partial void Disconnected(ILogger logger, string reason, Exception? exception);

    [LoggerMessage(3, LogLevel.Information, "Reconnecting to gpsd at {Address} in {Delay}")]
    public static partial void ReconnectDelay(ILogger logger, string address, TimeSpan delay);

    [LoggerMessage(4, LogLevel.Debug, "Connecting to gpsd at {Address} failed: {Result}")]
    public static partial void ConnectFailed(ILogger logger, string address, string result, Exception? exception);

    [LoggerMessage(5, LogLevel.Information, "gpsd VERSION received: release {Release}, protocol {ProtoMajor}.{ProtoMinor}")]
    public static partial void VersionReceived(ILogger logger, string? release, int? protoMajor, int? protoMinor);

    [LoggerMessage(6, LogLevel.Warning, "gpsd reports protocol version {ProtoMajor}.{ProtoMinor}; this exporter supports protocol 3 (gpsd 3.20 or later). Carrying on.")]
    public static partial void UnsupportedProtocol(ILogger logger, int? protoMajor, int? protoMinor);

    [LoggerMessage(7, LogLevel.Warning, "gpsd sent ERROR: {Message}")]
    public static partial void GpsdError(ILogger logger, string? message);

    [LoggerMessage(8, LogLevel.Information, "Device added: {Device}")]
    public static partial void DeviceAdded(ILogger logger, string device);

    [LoggerMessage(9, LogLevel.Information, "Device removed: {Device}")]
    public static partial void DeviceRemoved(ILogger logger, string device);

    [LoggerMessage(10, LogLevel.Debug, "Keepalive: no data from gpsd for {Idle}, sent ?VERSION;")]
    public static partial void KeepaliveProbe(ILogger logger, TimeSpan idle);
}
