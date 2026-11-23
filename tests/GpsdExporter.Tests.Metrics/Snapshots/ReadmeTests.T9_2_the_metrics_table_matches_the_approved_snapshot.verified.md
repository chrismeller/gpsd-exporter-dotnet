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
