using GpsdExporter.Protocol;

namespace GpsdExporter.State;

/// <summary>
/// Applies parsed reports to the exporter's state. Single writer: the
/// connection's reader calls <see cref="Apply"/> and <see cref="Reset"/>; each call replaces
/// <see cref="Current"/> with a new immutable <see cref="GpsdState"/>.
/// </summary>
/// <remarks>
/// Readers never lock: each state is immutable, and it is published with a single volatile
/// reference write, so a reader sees either the whole of one state or the whole of the next.
/// A scrape takes one <see cref="Snapshot"/> at the start of collection.
/// </remarks>
public sealed class StateStore(TimeProvider timeProvider, StalenessOptions staleness)
{
    /// <summary>The most devices tracked at once; reports from more are skipped.</summary>
    public const int MaxDevices = 16;

    private GpsdState _current = GpsdState.Empty;

    public StateStore(TimeProvider timeProvider)
        : this(timeProvider, new StalenessOptions())
    {
    }

    /// <summary>The latest published state. Safe to read from any thread.</summary>
    public GpsdState Current
    {
        get => Volatile.Read(ref _current);
        private set => Volatile.Write(ref _current, value);
    }

    /// <summary>The latest state and the current time, for one scrape. Safe to call from any thread.</summary>
    public StateSnapshot Snapshot() => new(Current, timeProvider.GetUtcNow(), staleness);

    /// <summary>Forgets everything, as on disconnect.</summary>
    public void Reset() => Current = GpsdState.Empty;

    /// <summary>
    /// Applies one report. Returns the reason it was skipped, or null when it was applied:
    /// <see cref="ParseErrorReason.MissingDevice"/> when it has no <c>device</c> and there is
    /// not exactly one device to attribute it to, or
    /// <see cref="ParseErrorReason.TooManyDevices"/> when it would be a device beyond
    /// <see cref="MaxDevices"/>.
    /// </summary>
    public ParseErrorReason? Apply(GpsdReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var now = timeProvider.GetUtcNow();
        switch (report)
        {
            case VersionReport version:
                Current = Current with { Version = new Timed<VersionReport>(version, now) };
                return null;

            case DevicesReport devices:
                return ApplyDevices(devices, now);

            case DeviceReport device:
                return ApplyDevice(device, device.Path, removeWhenInactive: true, now);

            case TpvReport or SkyReport or TimeOffsetReport or OscReport:
                return ApplyDeviceReport(report, now);

            default:
                // WATCH, ERROR and other classes carry no state.
                return null;
        }
    }

    private ParseErrorReason? ApplyDevices(DevicesReport report, DateTimeOffset now)
    {
        // DEVICES lists active devices, so an entry without "activated" is not a removal.
        ParseErrorReason? first = null;
        foreach (var device in report.Devices)
        {
            var error = device.Path is null
                ? ParseErrorReason.MissingDevice
                : ApplyDevice(device, device.Path, removeWhenInactive: false, now);
            first ??= error;
        }

        return first;
    }

    private ParseErrorReason? ApplyDevice(DeviceReport device, string? path, bool removeWhenInactive, DateTimeOffset now)
    {
        var resolved = Resolve(path);
        if (resolved is null)
        {
            return ParseErrorReason.MissingDevice;
        }

        var remove = device.Activation == DeviceActivation.Deactivated
            || (removeWhenInactive && device.Activation == DeviceActivation.Absent);
        if (remove)
        {
            // Every series for the device goes, so nothing stale is left behind.
            Current = Current with { Devices = Current.Devices.Remove(resolved) };
            return null;
        }

        return Update(resolved, GpsdClass.Device, now, d => d with
        {
            Info = new DeviceInfo(device.Driver, device.Subtype, device.Cycle, device.Bps, now),
        });
    }

    private ParseErrorReason? ApplyDeviceReport(GpsdReport report, DateTimeOffset now)
    {
        var resolved = Resolve(report.Device);
        if (resolved is null)
        {
            return ParseErrorReason.MissingDevice;
        }

        return report switch
        {
            TpvReport tpv => Update(resolved, GpsdClass.Tpv, now, d => d with { Tpv = TpvState.Apply(d.Tpv, tpv, now) }),
            SkyReport sky => Update(resolved, GpsdClass.Sky, now, d => d with { Sky = SkyState.Apply(d.Sky, sky, now) }),
            TimeOffsetReport { Kind: GpsdClass.Pps } pps => Update(resolved, GpsdClass.Pps, now, d => d with
            {
                Pps = new TimeOffsetState(pps, now),
                PpsPulses = d.PpsPulses + 1,
            }),
            TimeOffsetReport toff => Update(resolved, GpsdClass.Toff, now, d => d with { Toff = new TimeOffsetState(toff, now) }),
            OscReport osc => Update(resolved, GpsdClass.Osc, now, d => d with { Osc = new OscState(osc, now) }),
            _ => null,
        };
    }

    /// <summary>The object's own device, or the single tracked device when it has none.</summary>
    private string? Resolve(string? device)
    {
        if (!string.IsNullOrEmpty(device))
        {
            return device;
        }

        return Current.Devices.Count == 1 ? Current.Devices.Keys.First() : null;
    }

    private ParseErrorReason? Update(string path, GpsdClass cls, DateTimeOffset now, Func<DeviceState, DeviceState> change)
    {
        if (!Current.Devices.TryGetValue(path, out var device))
        {
            if (Current.Devices.Count >= MaxDevices)
            {
                return ParseErrorReason.TooManyDevices;
            }

            // A report from a device not (yet) listed in DEVICES creates it, so its data is not lost.
            device = new DeviceState(path);
        }

        device = change(device);
        device = device with { LastReport = device.LastReport.SetItem(cls, now) };
        Current = Current with { Devices = Current.Devices.SetItem(path, device) };
        return null;
    }
}
