# gpsd-exporter-dotnet

A Prometheus exporter for [gpsd](https://gpsd.io/), written in .NET 10 and shipped as a single native
binary and a container image for amd64 and arm64.

It replaces the Go `gpsd-exporter` written by Nate Sales
([natesales/gpsd-exporter](https://github.com/natesales/gpsd-exporter)), whose work this builds on. It
takes the same command-line flags and serves `/metrics` on the same port, so it can replace it in place.
The metric names are new (see [Migrating from the Go exporter](#migrating-from-the-go-exporter)).

What it does differently:

- It streams from gpsd (`?WATCH`) instead of polling, and reconnects with backoff when gpsd restarts.
- Every series is labelled by `device`, and satellites by constellation, satellite and signal.
- A value is served only while gpsd keeps reporting it. Fields gpsd did not send, satellites that set
  and devices that went away disappear instead of freezing at their last value.
- Names follow Prometheus conventions: base units (seconds, meters), `_total` counters, `_info` metrics.
- It reports its own health (connection attempts, parse errors, scrape duration), and can send its own
  metrics and logs over OTLP.

## Requirements

- Linux on `x86_64` or `aarch64` (64-bit Raspberry Pi OS included). 32-bit ARM is not supported.
- glibc 2.36 or newer for the binary: Debian 12, Raspberry Pi OS bookworm, Ubuntu 22.04 and later. The
  container image brings its own.
- gpsd 3.20 or newer (protocol version 3). Debian 12 and Raspberry Pi OS bookworm ship 3.22.

## Installing the binary

Each release has a single self-contained executable for `linux-x64` and `linux-arm64` (64-bit
Raspberry Pi OS included). It needs glibc 2.36 or newer (Debian 12, Raspberry Pi OS bookworm, Ubuntu
22.04 and later) and nothing else; no .NET install is required. It takes the Go exporter's flags, so it
can replace it in place.

```sh
arch=arm64   # or x64
base=https://github.com/chrismeller/gpsd-exporter-dotnet/releases/latest/download
curl -fsSLO "$base/gpsd-exporter-linux-$arch"
curl -fsSLO "$base/gpsd-exporter-linux-$arch.sha256"
sha256sum --check "gpsd-exporter-linux-$arch.sha256"
sudo install -m 0755 "gpsd-exporter-linux-$arch" /usr/local/bin/gpsd-exporter
gpsd-exporter --version
```

### Running it with systemd

The release also has `gpsd-exporter.service` (from `packaging/systemd`). It starts after gpsd, restarts
on failure (but not on a configuration error, exit code 2), and runs as a transient unprivileged user
(`DynamicUser=yes`) with a read-only view of the system.

```sh
curl -fsSLO "$base/gpsd-exporter.service"
curl -fsSLO "$base/gpsd-exporter.env"
sudo install -m 0644 gpsd-exporter.service /etc/systemd/system/
sudo install -m 0644 gpsd-exporter.env /etc/default/gpsd-exporter   # optional; every line is commented out
sudo systemctl daemon-reload
sudo systemctl enable --now gpsd-exporter
curl -s localhost:9978/metrics | grep gpsd_up
```

Settings go in `/etc/default/gpsd-exporter` as `GPSD_EXPORTER_` variables, the section and key joined by a
double underscore, for example `GPSD_EXPORTER_GPSD__ADDRESS=localhost:2947` or
`GPSD_EXPORTER_HTTP__LISTEN=127.0.0.1:9978`. The example file lists the common ones with their
defaults. Run `sudo systemctl restart gpsd-exporter` after changing it, and `journalctl -u gpsd-exporter`
to see the logs.

## Running the container image

`ghcr.io/chrismeller/gpsd-exporter-dotnet` is published for `linux/amd64` and `linux/arm64`. It holds
only the native binary on Microsoft's chiseled `runtime-deps` base (no shell or package manager) and
runs as an unprivileged user.

gpsd usually listens only on localhost, so the simplest setup shares the host's network. The exporter
then reaches gpsd at its default `localhost:2947` and serves `/metrics` on port 9978:

<!-- readme-example: docker -->
```sh
docker run -d --name gpsd-exporter --restart unless-stopped --network host \
  ghcr.io/chrismeller/gpsd-exporter-dotnet:latest
```

If gpsd is elsewhere, or listens on an address the container can reach, use a normal network and pass
its address. Flags and `GPSD_EXPORTER_` variables work as they do for the binary:

```sh
docker run -d --name gpsd-exporter --restart unless-stopped -p 9978:9978 \
  -e GPSD_EXPORTER_GPSD__ADDRESS=gps-host.example:2947 \
  ghcr.io/chrismeller/gpsd-exporter-dotnet:latest
```

## Configuration

Settings come from, highest precedence first: command-line flags, `GPSD_EXPORTER_` environment
variables, `appsettings.json` in the working directory, and the defaults below. In an environment
variable the section and key are joined by a double underscore (`Gpsd:Address` is
`GPSD_EXPORTER_GPSD__ADDRESS`). Any setting can also be given on the command line as
`--Section:Key=value`. An invalid value stops the exporter with a message and exit code 2.

| Setting | Flag | Default | Notes |
| -- | -- | -- | -- |
| `Gpsd:Address` | `-d`, `--gpsd` | `localhost:2947` | gpsd's `host:port`; IPv6 addresses in brackets. |
| `Gpsd:Device` | `--device` | (all) | Watch only this device. |
| `Gpsd:Pps` | `--pps` | `true` | Ask gpsd for PPS and TOFF reports. |
| `Gpsd:ConnectTimeout` | | `00:00:05` | |
| `Gpsd:ReconnectInitialDelay` | | `00:00:01` | Reconnects back off from this, doubling, with jitter. |
| `Gpsd:ReconnectMaxDelay` | | `00:00:30` | |
| `Gpsd:KeepaliveIdle` | | `00:00:15` | After this long with no data, the exporter checks that gpsd is still there. |
| `Gpsd:KeepaliveTimeout` | | `00:00:05` | |
| `Staleness:Tpv`, `:Sky`, `:Pps`, `:Toff`, `:Osc` | | 10 s, 30 s, 10 s, 10 s, 30 s | How long a report's values are served after it arrived. |
| `Metrics:Satellites` | | `true` | Per-satellite series. Turn off if their number matters. |
| `Metrics:Runtime` | | `false` | .NET runtime and process metrics on `/metrics`. |
| `Http:Listen` | `-l`, `--listen` | `:9978` | `[addr]:port`; `:9978` listens on every address. |
| `Http:MetricsPath` | | `/metrics` | `/healthz` is always served too. |
| `Otlp:Endpoint` | | (off) | Send the exporter's own metrics and logs over OTLP. The standard `OTEL_EXPORTER_OTLP_*` variables work too. GPS metrics are never sent over OTLP. |
| `Logging:Format` | `--log-format` | `text` | `text` or `json`. |
| `Logging:LogLevel:Default` | `-v`, `-vv` | `Information` | `-v` is Debug, `-vv` is Trace. |
| | `-p` | | Accepted for compatibility with the Go exporter and ignored with a warning. |
| | `--version` | | Prints the version and exits. |
| | `-h`, `--help` | | Prints the flags and exits. |

## Grafana dashboard

[`grafana-dashboard.json`](grafana-dashboard.json) shows the fix, position, satellites by constellation
and signal, dilution of precision, PPS and TOFF offsets, and the exporter's health. Import it in Grafana
(Dashboards, New, Import) and pick your Prometheus data source; the `device` variable selects receivers.

## Metrics

A series exists only while its value is known: a field gpsd did not send is absent rather than 0, and
every gpsd series disappears when gpsd disconnects, when its device goes away, or when its report is
older than the staleness setting for its class. `gpsd_up` and the exporter's own metrics are always
present. Every gpsd series has a `device` label. Satellite series are labelled `device` and `prn`, plus
`constellation`, `gnssid` and `svid` when gpsd reports the satellite's GNSS and SV ids, and `sigid` when it
reports the signal.

<!-- The tables below are generated from the metric catalogue; a test fails if they differ. -->
<!-- metrics-table:begin -->
### Connection and devices

| Name | Type | Unit | Labels | Help |
| -- | -- | -- | -- | -- |
| `gpsd_up` | gauge | 1 |  | Whether the exporter is connected to gpsd and has received VERSION (1) or not (0). |
| `gpsd_version_info` | gauge | 1 | `release`, `rev`, `proto_major`, `proto_minor` | gpsd daemon version, from VERSION. Always 1. |
| `gpsd_device_info` | gauge | 1 | `device`, `driver`, `subtype` | A device reported by gpsd. Always 1. |
| `gpsd_device_cycle_seconds` | gauge | s | `device` | Device reporting cycle, from DEVICE cycle. |
| `gpsd_device_bits_per_second` | gauge | bit/s | `device` | Device serial speed, from DEVICE bps. |
| `gpsd_device_last_report_timestamp_seconds` | gauge | s | `device`, `class` | When the exporter last received a report of this class from this device, by the exporter's clock. |

### Position, velocity and time (TPV)

| Name | Type | Unit | Labels | Help |
| -- | -- | -- | -- | -- |
| `gpsd_tpv_mode` | gauge | 1 | `device` | NMEA mode from TPV mode: 0 unknown, 1 no fix, 2 2D fix, 3 3D fix. |
| `gpsd_tpv_status` | gauge | 1 | `device` | GPS fix status from TPV status, as gpsd numbers it (2 DGPS, 3 RTK fixed, 4 RTK float, ...). |
| `gpsd_tpv_timestamp_seconds` | gauge | s | `device` | Time of the fix from TPV time, in seconds since the Unix epoch. |
| `gpsd_tpv_leap_seconds` | gauge | s | `device` | Current leap seconds (GPS minus UTC), from TPV leapseconds. |
| `gpsd_tpv_latitude_degrees` | gauge | deg | `device` | Latitude in degrees, positive north, from TPV lat. |
| `gpsd_tpv_longitude_degrees` | gauge | deg | `device` | Longitude in degrees, positive east, from TPV lon. |
| `gpsd_tpv_altitude_hae_meters` | gauge | m | `device` | Altitude above the WGS84 ellipsoid, from TPV altHAE. |
| `gpsd_tpv_altitude_msl_meters` | gauge | m | `device` | Altitude above mean sea level, from TPV altMSL. |
| `gpsd_tpv_geoid_separation_meters` | gauge | m | `device` | Geoid separation (MSL minus ellipsoid height), from TPV geoidSep. |
| `gpsd_tpv_speed_meters_per_second` | gauge | m/s | `device` | Speed over ground, from TPV speed. |
| `gpsd_tpv_climb_meters_per_second` | gauge | m/s | `device` | Climb (positive) or sink (negative) rate, from TPV climb. |
| `gpsd_tpv_track_degrees` | gauge | deg | `device` | Course over ground relative to true north, from TPV track. |
| `gpsd_tpv_magnetic_track_degrees` | gauge | deg | `device` | Course over ground relative to magnetic north, from TPV magtrack. |
| `gpsd_tpv_magnetic_variation_degrees` | gauge | deg | `device` | Magnetic variation, from TPV magvar. |
| `gpsd_tpv_velocity_meters_per_second` | gauge | m/s | `device`, `axis` | Velocity component (north, east, down), from TPV velN, velE and velD. |
| `gpsd_tpv_ecef_position_meters` | gauge | m | `device`, `axis` | ECEF position component (x, y, z), from TPV ecefx, ecefy and ecefz. |
| `gpsd_tpv_ecef_velocity_meters_per_second` | gauge | m/s | `device`, `axis` | ECEF velocity component (x, y, z), from TPV ecefvx, ecefvy and ecefvz. |
| `gpsd_tpv_ecef_position_error_meters` | gauge | m | `device` | ECEF position error estimate, from TPV ecefpAcc. gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma). |
| `gpsd_tpv_ecef_velocity_error_meters_per_second` | gauge | m/s | `device` | ECEF velocity error estimate, from TPV ecefvAcc. gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma). |
| `gpsd_tpv_relative_position_meters` | gauge | m | `device`, `axis` | Position relative to the base station (north, east, down), from TPV relN, relE and relD. |
| `gpsd_tpv_position_error_meters` | gauge | m | `device`, `kind` | Position error estimate (horizontal eph, vertical epv, latitude epy, longitude epx, spherical sep). gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma). |
| `gpsd_tpv_time_error_seconds` | gauge | s | `device` | Time error estimate, from TPV ept. gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma). |
| `gpsd_tpv_speed_error_meters_per_second` | gauge | m/s | `device` | Speed error estimate, from TPV eps. gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma). |
| `gpsd_tpv_climb_error_meters_per_second` | gauge | m/s | `device` | Climb error estimate, from TPV epc. gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma). |
| `gpsd_tpv_track_error_degrees` | gauge | deg | `device` | Track error estimate, from TPV epd. gpsd does not say which confidence level the receiver uses (95%, 50%, 1 sigma or 2 sigma). |
| `gpsd_tpv_clock_bias_seconds` | gauge | s | `device` | Receiver clock bias, from TPV clockbias (nanoseconds) converted to seconds. |
| `gpsd_tpv_clock_drift_seconds_per_second` | gauge | s/s | `device` | Receiver clock drift, from TPV clockdrift (nanoseconds per second) converted to seconds per second. |
| `gpsd_tpv_dgps_age_seconds` | gauge | s | `device` | Age of the DGPS data, from TPV dgpsAge. |
| `gpsd_tpv_dgps_station` | gauge | 1 | `device` | DGPS station id, from TPV dgpsSta. |
| `gpsd_tpv_jamming_indicator` | gauge | 1 | `device` | Jamming indicator, 0 (none) to 255 (strong), from TPV jam. |
| `gpsd_tpv_antenna_status` | gauge | 1 | `device` | Antenna status code, from TPV ant: 0 unknown, 1 ok, 2 open, 3 short. |
| `gpsd_tpv_antenna_power_status` | gauge | 1 | `device` | Antenna power status code, from TPV antPwr: 0 unknown, 1 off, 2 on. |
| `gpsd_tpv_receiver_temperature_celsius` | gauge | Cel | `device` | Receiver temperature, from TPV temp. |

### Sky view and satellites (SKY)

| Name | Type | Unit | Labels | Help |
| -- | -- | -- | -- | -- |
| `gpsd_sky_satellites_visible` | gauge | 1 | `device` | Satellite signals in the sky view, from SKY nSat or the length of the satellites array. |
| `gpsd_sky_satellites_used` | gauge | 1 | `device` | Satellite signals used in the fix, from SKY uSat or the satellites marked used. |
| `gpsd_sky_dop` | gauge | 1 | `device`, `kind` | Dilution of precision (geometric, horizontal, position, time, vertical, longitudinal, latitudinal), from SKY gdop, hdop, pdop, tdop, vdop, xdop and ydop. |
| `gpsd_satellite_used` | gauge | 1 | `device`, `constellation`, `gnssid`, `svid`, `sigid`, `prn` | Whether the satellite signal is used in the fix (1) or not (0). |
| `gpsd_satellite_snr_dbhz` | gauge | dBHz | `device`, `constellation`, `gnssid`, `svid`, `sigid`, `prn` | Signal strength (carrier to noise density), from ss. |
| `gpsd_satellite_azimuth_degrees` | gauge | deg | `device`, `constellation`, `gnssid`, `svid`, `sigid`, `prn` | Azimuth from true north, from az. |
| `gpsd_satellite_elevation_degrees` | gauge | deg | `device`, `constellation`, `gnssid`, `svid`, `sigid`, `prn` | Elevation above the horizon, from el. |
| `gpsd_satellite_health` | gauge | 1 | `device`, `constellation`, `gnssid`, `svid`, `sigid`, `prn` | Satellite health, from health: 0 unknown, 1 ok, 2 unhealthy. |
| `gpsd_satellite_quality` | gauge | 1 | `device`, `constellation`, `gnssid`, `svid`, `sigid`, `prn` | Signal quality indicator, 0 to 7, from qual. |
| `gpsd_satellite_pseudorange_residual_meters` | gauge | m | `device`, `constellation`, `gnssid`, `svid`, `sigid`, `prn` | Pseudorange residual, from prRes. |

### Timing (PPS, TOFF and OSC)

| Name | Type | Unit | Labels | Help |
| -- | -- | -- | -- | -- |
| `gpsd_pps_offset_seconds` | gauge | s | `device` | System clock minus the PPS reference time at the last pulse; positive means the system clock is ahead. |
| `gpsd_pps_precision_log2_seconds` | gauge | log2(s) | `device` | PPS precision as log2 seconds (NTP style), from PPS precision. |
| `gpsd_pps_quantization_error_seconds` | gauge | s | `device` | PPS quantization error, from PPS qErr (picoseconds) converted to seconds. |
| `gpsd_pps_pulse_timestamp_seconds` | gauge | s | `device` | System clock at the last pulse, from PPS clock_sec and clock_nsec. |
| `gpsd_pps_pulses_total` | counter | 1 | `device` | PPS reports received from the device on the current connection. |
| `gpsd_toff_offset_seconds` | gauge | s | `device` | System clock minus the GPS time in the last TOFF; positive means the system clock is ahead. |
| `gpsd_toff_precision_log2_seconds` | gauge | log2(s) | `device` | TOFF precision as log2 seconds (NTP style), from TOFF precision. |
| `gpsd_osc_running` | gauge | 1 | `device` | Whether the oscillator is running (1) or not (0), from OSC running. |
| `gpsd_osc_reference` | gauge | 1 | `device` | Whether the oscillator has a PPS reference (1) or not (0), from OSC reference. |
| `gpsd_osc_disciplined` | gauge | 1 | `device` | Whether the oscillator is disciplined to the reference (1) or not (0), from OSC disciplined. |
| `gpsd_osc_delta_seconds` | gauge | s | `device` | Time difference between the oscillator and the PPS reference, from OSC delta (nanoseconds) converted to seconds. |

### Exporter

| Name | Type | Unit | Labels | Help |
| -- | -- | -- | -- | -- |
| `gpsd_exporter_build_info` | gauge | 1 | `version`, `revision`, `runtime` | Build information. Always 1. |
| `gpsd_exporter_connection_attempts_total` | counter | 1 | `result` | Connection attempts to gpsd, by result. |
| `gpsd_exporter_disconnects_total` | counter | 1 | `reason` | Connections to gpsd lost or closed, by reason. |
| `gpsd_exporter_reconnect_delay_seconds` | gauge | s |  | Current reconnect backoff delay; 0 while connected. |
| `gpsd_exporter_connected_timestamp_seconds` | gauge | s |  | When the current connection to gpsd was established. Present only while connected. |
| `gpsd_exporter_messages_total` | counter | 1 | `class` | gpsd objects received, by class. |
| `gpsd_exporter_parse_errors_total` | counter | 1 | `reason` | Input lines or fields skipped, by reason. |
| `gpsd_exporter_last_message_timestamp_seconds` | gauge | s |  | When any line was last received from gpsd. Present only after the first line. |
| `gpsd_exporter_scrape_duration_seconds` | histogram | s |  | Time to collect the gpsd metrics for a scrape. |
<!-- metrics-table:end -->

## Migrating from the Go exporter

The binary takes the Go exporter's flags (`-d`, `-l`, `-v`, `-vv`; `-p` is ignored) and listens on the
same port, so a systemd unit or container that ran the Go exporter runs this one unchanged. The metric
names are new and there is no compatibility mode: update dashboards and alerts using the table below, or
start from the new Grafana dashboard. Timestamps are in seconds, not milliseconds, and every series now
has a `device` label.

<!-- migration-table:begin -->
| Go exporter | Replacement | Notes |
| -- | -- | -- |
| `gpsd_last_poll` | `gpsd_exporter_last_message_timestamp_seconds` | There is no polling any more: this is when the last message arrived. Seconds, not milliseconds. |
| `gpsd_version` | `gpsd_version_info` | Same labels. |
| `gpsd_tpv_mode` | `gpsd_tpv_mode` | |
| `gpsd_tpv_status` | `gpsd_tpv_status` | |
| `gpsd_tpv_time` | `gpsd_tpv_timestamp_seconds` | Seconds, not milliseconds. |
| `gpsd_tpv_altHAE` | `gpsd_tpv_altitude_hae_meters` | |
| `gpsd_tpv_altMSL` | `gpsd_tpv_altitude_msl_meters` | |
| `gpsd_tpv_climb` | `gpsd_tpv_climb_meters_per_second` | |
| `gpsd_tpv_depth` | Removed | Marine field. |
| `gpsd_tpv_dgpsAge` | `gpsd_tpv_dgps_age_seconds` | |
| `gpsd_tpv_dgpsSta` | `gpsd_tpv_dgps_station` | |
| `gpsd_tpv_epc` | `gpsd_tpv_climb_error_meters_per_second` | |
| `gpsd_tpv_epd` | `gpsd_tpv_track_error_degrees` | |
| `gpsd_tpv_eph` | `gpsd_tpv_position_error_meters{kind="horizontal"}` | |
| `gpsd_tpv_eps` | `gpsd_tpv_speed_error_meters_per_second` | |
| `gpsd_tpv_ept` | `gpsd_tpv_time_error_seconds` | |
| `gpsd_tpv_epx` | `gpsd_tpv_position_error_meters{kind="longitude"}` | |
| `gpsd_tpv_epy` | `gpsd_tpv_position_error_meters{kind="latitude"}` | |
| `gpsd_tpv_epv` | `gpsd_tpv_position_error_meters{kind="vertical"}` | |
| `gpsd_tpv_geoidSep` | `gpsd_tpv_geoid_separation_meters` | |
| `gpsd_tpv_lat` | `gpsd_tpv_latitude_degrees` | |
| `gpsd_tpv_leapseconds` | `gpsd_tpv_leap_seconds` | |
| `gpsd_tpv_lon` | `gpsd_tpv_longitude_degrees` | |
| `gpsd_tpv_track` | `gpsd_tpv_track_degrees` | |
| `gpsd_tpv_magtrack` | `gpsd_tpv_magnetic_track_degrees` | |
| `gpsd_tpv_magvar` | `gpsd_tpv_magnetic_variation_degrees` | |
| `gpsd_tpv_speed` | `gpsd_tpv_speed_meters_per_second` | |
| `gpsd_tpv_ecefx` | `gpsd_tpv_ecef_position_meters{axis="x"}` | |
| `gpsd_tpv_ecefy` | `gpsd_tpv_ecef_position_meters{axis="y"}` | |
| `gpsd_tpv_ecefz` | `gpsd_tpv_ecef_position_meters{axis="z"}` | |
| `gpsd_tpv_ecefpAcc` | `gpsd_tpv_ecef_position_error_meters` | |
| `gpsd_tpv_ecefvx` | `gpsd_tpv_ecef_velocity_meters_per_second{axis="x"}` | |
| `gpsd_tpv_ecefvy` | `gpsd_tpv_ecef_velocity_meters_per_second{axis="y"}` | |
| `gpsd_tpv_ecefvz` | `gpsd_tpv_ecef_velocity_meters_per_second{axis="z"}` | |
| `gpsd_tpv_ecefvAcc` | `gpsd_tpv_ecef_velocity_error_meters_per_second` | |
| `gpsd_tpv_sep` | `gpsd_tpv_position_error_meters{kind="spherical"}` | |
| `gpsd_tpv_relD` | `gpsd_tpv_relative_position_meters{axis="down"}` | |
| `gpsd_tpv_relE` | `gpsd_tpv_relative_position_meters{axis="east"}` | |
| `gpsd_tpv_relN` | `gpsd_tpv_relative_position_meters{axis="north"}` | |
| `gpsd_tpv_velD` | `gpsd_tpv_velocity_meters_per_second{axis="down"}` | |
| `gpsd_tpv_velE` | `gpsd_tpv_velocity_meters_per_second{axis="east"}` | |
| `gpsd_tpv_velN` | `gpsd_tpv_velocity_meters_per_second{axis="north"}` | |
| `gpsd_tpv_wanglem` | Removed | Marine field. |
| `gpsd_tpv_wangler` | Removed | Marine field. |
| `gpsd_tpv_wanglet` | Removed | Marine field. |
| `gpsd_tpv_wspeedr` | Removed | Marine field. |
| `gpsd_tpv_wspeedt` | Removed | Marine field. |
| `gpsd_sky_nSat` | `gpsd_sky_satellites_visible` | |
| `gpsd_sky_uSat` | `gpsd_sky_satellites_used` | |
| `gpsd_sky_gdop` | `gpsd_sky_dop{kind="geometric"}` | |
| `gpsd_sky_hdop` | `gpsd_sky_dop{kind="horizontal"}` | |
| `gpsd_sky_pdop` | `gpsd_sky_dop{kind="position"}` | |
| `gpsd_sky_tdop` | `gpsd_sky_dop{kind="time"}` | |
| `gpsd_sky_vdop` | `gpsd_sky_dop{kind="vertical"}` | |
| `gpsd_sky_xdop` | `gpsd_sky_dop{kind="longitudinal"}` | |
| `gpsd_sky_ydop` | `gpsd_sky_dop{kind="latitudinal"}` | |
| `gpsd_sky_prRes` | `gpsd_satellite_pseudorange_residual_meters` | Per satellite, as gpsd reports it. The Go exporter read it from SKY itself, where gpsd does not send it. |
| `gpsd_sky_qual` | `gpsd_satellite_quality` | Per satellite; see `gpsd_sky_prRes`. |
| `gpsd_sky_time` | Removed | `gpsd_device_last_report_timestamp_seconds{class="SKY"}` says when the last SKY arrived. |
| `gpsd_sat_az` | `gpsd_satellite_azimuth_degrees` | Satellites are identified by `constellation`, `gnssid`, `svid`, `sigid` and `prn`, not `prn` alone. |
| `gpsd_sat_el` | `gpsd_satellite_elevation_degrees` | |
| `gpsd_sat_ss` | `gpsd_satellite_snr_dbhz` | |
| `gpsd_sat_used` | `gpsd_satellite_used` | |
| `gpsd_sat_gnssid` | Removed | Now the `gnssid` and `constellation` labels. |
| `gpsd_sat_svid` | Removed | Now the `svid` label. |
| `gpsd_sat_sigid` | Removed | Now the `sigid` label. |
| `gpsd_sat_freqid` | Removed | GLONASS frequency slot; not exported. |
| `gpsd_sat_health` | `gpsd_satellite_health` | |
| `gpsd_gst_time` | Removed | GST (pseudorange noise) is not exported. |
| `gpsd_gst_rms` | Removed | GST is not exported. |
| `gpsd_gst_major` | Removed | GST is not exported. |
| `gpsd_gst_minor` | Removed | GST is not exported. |
| `gpsd_gst_orient` | Removed | GST is not exported. |
| `gpsd_gst_lat` | Removed | GST is not exported. |
| `gpsd_gst_lon` | Removed | GST is not exported. |
| `gpsd_gst_alt` | Removed | GST is not exported. |
| `gpsd_pps_real_sec` | `gpsd_pps_offset_seconds`, `gpsd_pps_pulse_timestamp_seconds` | The four sec and nsec gauges are replaced by the offset (clock minus real) and the pulse time. |
| `gpsd_pps_real_nsec` | `gpsd_pps_offset_seconds` | |
| `gpsd_pps_clock_sec` | `gpsd_pps_offset_seconds` | |
| `gpsd_pps_clock_nsec` | `gpsd_pps_offset_seconds` | |
| `gpsd_pps_precision` | `gpsd_pps_precision_log2_seconds` | |
| `gpsd_pps_qErr` | `gpsd_pps_quantization_error_seconds` | Seconds, not picoseconds. |
| `gpsd_toff_real_sec` | `gpsd_toff_offset_seconds` | The four sec and nsec gauges are replaced by the offset (clock minus real). |
| `gpsd_toff_real_nsec` | `gpsd_toff_offset_seconds` | |
| `gpsd_toff_clock_sec` | `gpsd_toff_offset_seconds` | |
| `gpsd_toff_clock_nsec` | `gpsd_toff_offset_seconds` | |
| `gpsd_osc_running` | `gpsd_osc_running` | |
| `gpsd_osc_reference` | `gpsd_osc_reference` | |
| `gpsd_osc_disciplined` | `gpsd_osc_disciplined` | |
| `gpsd_osc_delta` | `gpsd_osc_delta_seconds` | Seconds, not nanoseconds. |
<!-- migration-table:end -->

## Building

The SDK version is pinned in `global.json`.

```sh
dotnet build
dotnet test
```

`dotnet test` uses Microsoft Testing Platform (selected in `global.json`). Compiler, analyzer, trim and
Native AOT warnings fail the build, as do known-vulnerable NuGet packages.

To produce the native binary:

```sh
dotnet publish src/GpsdExporter -r linux-x64 -c Release
```

Exposition tests use [Verify](https://github.com/VerifyTests/Verify) for snapshots. Approved snapshots are
`*.verified.txt` files under `tests/GpsdExporter.Tests.Metrics/Snapshots`; a mismatch writes a `.received.txt`
next to it (ignored by git). Verify's licence check is satisfied by its OpenSource exemption in
`Directory.Build.props`, which must be renewed before `Verify_SponsorshipExemptionUntil`.

## Releasing

Releases are managed by [Release Please](https://github.com/googleapis/release-please). Pull requests are
squash-merged, and their titles must be [Conventional Commits](https://www.conventionalcommits.org/)
(the "Pull request title" check enforces this). `feat:` makes a minor release, `fix:` a patch release, and
a breaking change (`feat!:` or a `BREAKING CHANGE:` footer) a major one. `docs:`, `chore:`, `ci:`, `test:`,
`build:` and `refactor:` do not make a release on their own.

Release Please keeps a release pull request open on main with the next version and its changelog. To
release, merge it. Release Please then tags the commit `vX.Y.Z` and creates the GitHub release, and the
release workflow builds both binaries with that version, checks that `--version` and
`gpsd_exporter_build_info` report it, and tests the image on both architectures. Only then does it push
`ghcr.io/chrismeller/gpsd-exporter-dotnet:X.Y.Z` and `:latest` and attach the binaries, their checksums,
`gpsd-exporter.service` and `gpsd-exporter.env` to the release.

Dependencies are kept current by [Renovate](https://docs.renovatebot.com/) (`renovate.json5`). It merges
each update by itself once every check on its pull request has passed, and leaves it open otherwise. Major
upgrades are not opened until they are approved on the Dependency Dashboard issue.
Updates to what ships (the OpenTelemetry packages, the .NET SDK and the image's base) are `fix(deps):`, so
they lead to a patch release; test and CI updates are `chore(deps):` and do not.

To force a particular version, put `Release-As: X.Y.Z` in the body of a commit on main.

To try a release without publishing anything, run the Release workflow by hand (Actions, Release, Run
workflow) with a test version. It does everything except publish, and leaves the release files as the
`release-files` artifact. It also runs this way on any pull request that changes the workflow.

## Layout

| Path | Contents |
| -- | -- |
| `src/GpsdExporter` | The exporter (Native AOT, ASP.NET Core slim host). |
| `tests/GpsdExporter.Tests.Unit` | Parser, state store, staleness, backoff and configuration. No sockets or HTTP. |
| `tests/GpsdExporter.Tests.Metrics` | Exposition tests: the host in-process, `/metrics` scraped and parsed. |
| `tests/GpsdExporter.Tests.Integration` | The exporter against FakeGpsd, a scripted in-process TCP server. |
| `tests/GpsdExporter.Tests.Release` | Smoke tests for the published binary and image. Skipped unless `GPSD_EXPORTER_BINARY` is set. |
| `tests/GpsdExporter.Testing` | Shared test helpers: the fixture loader, FakeGpsd, and the Prometheus text parser and snapshot normalizer. |
| `grafana-dashboard.json` | The Grafana dashboard: fix, position, satellites, DOP, PPS and TOFF, and exporter health. |
| `packaging/systemd` | The systemd unit and its example settings file. |
| `packaging/docker` | The container image's Dockerfile. |
| `scripts` | CI helpers: the glibc version check, the systemd unit check, the image build context and the release version check. |
| `tests/fixtures` | gpsd sessions as `.jsonl`, built from `gpsd_json(5)` and gpsd's regression logs. See its README. |

## Licence

Apache 2.0. See `LICENSE`.
