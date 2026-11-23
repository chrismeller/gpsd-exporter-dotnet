# gpsd fixtures

Each `.jsonl` file is a gpsd session as received over the socket: one JSON object per line, in order.
Lines starting with `#` are header comments that say what the file covers and which `gpsd_json(5)`
section it follows. The test loader (`GpsdExporter.Testing.Fixtures`) strips them, along with empty
lines, before a file is replayed. The exporter's parser has no special handling for them.

Read the files as bytes: `bad-input.jsonl` contains invalid UTF-8 on purpose.

The man page source used is `man/gpsd_json.adoc` at gpsd commit
`edd38e78a875062f53add5c1ade745761240fffb` (2026-11-19).

## Catalogue

| File | Built from | Contents | Used by |
| -- | -- | -- | -- |
| `doc-examples.jsonl` | Every JSON example in the man page, verbatim (each joined onto one line) | TPV, SKY, GST, ATT, TOFF, PPS, OSC, VERSION, DEVICES, WATCH, POLL, DEVICE, ACK, ERROR, RTCM2. Includes the two wrong examples: VERSION with `version`, DEVICES with `"class"=` | T2.1, T2.5, T2.6, T3.1 |
| `session-3d-fix.jsonl` | TPV, SKY and satellite tables | VERSION, DEVICES, WATCH, then 10 epochs of mode 3 TPV and SKY with every field in the tables | T3.1, T7.2, T7.4 |
| `session-partial-epochs.jsonl` | Multiple TPVs per epoch; DOP-only SKY | Split TPVs with the same `time`, `epx` that stops after epoch 1, a DOP-only SKY | T4.1, T4.2, T4.6 |
| `session-fix-loss.jsonl` | TPV table, fields per mode | Mode sequence 3, 3, 2, 1, 0, 1, 2, 3 | T4.3, T4.4, T4.12 |
| `session-sky-shrink.jsonl` | SKY table | SKY with 3 satellites, then 1 (the audit's reproduction) | T4.5, T4.9 |
| `session-multi-gnss.jsonl` | PRN, GNSS id, SV id and SIG id section | Every gnssid, GPS svid 3 on sigid 0 and 6, svid 8 on sigid 0 and 7, PRN-only satellites, a duplicate identity | T6.2, T6.3, T6.4 |
| `session-two-devices.jsonl` | `device` on every class | Interleaved TPV and SKY from two devices, then DEVICE `activated:0` for one | T4.8, T6.1 |
| `session-timing.jsonl` | TOFF, PPS and OSC tables | PPS with and without `qErr`, negative offsets, nanosecond roll-over, OSC | T3.4, T3.5 |
| `bad-input.jsonl` | The 10,240-character limit and invalid-fragment warning; the invalid DEVICES example | Truncated lines, non-objects, missing `class`, wrong field types, a line over 64 KiB, invalid UTF-8 | T2.1 to T2.8, T5.3 |
| `gpsd-regress/*.jsonl` | gpsd's `test/daemon/*.log.chk` | Real receiver output (see below) | T2.1, T2.7, T7.2 |

`FixtureCatalogueTests` in the unit tests checks that every file here loads, and that the synthetic
sessions use only field names from the man page tables, with the right JSON types and value ranges.

## gpsd regression logs

These are the JSON lines from gpsd's own regression tests, at the commit above. NMEA and other raw
lines are removed. Long files are cut to their first 300 JSON lines. gpsd's regression output has no
`device` field and no VERSION, DEVICES or WATCH preamble, so tests that replay them through the
exporter need to supply a single active device first.

| File | Receiver | Source |
| -- | -- | -- |
| `gpsd-regress/ublox-neo-m8n.jsonl` | u-blox NEO-M8N, UBX, multi-GNSS | `test/daemon/ublox-neo-m8n.log.chk` (all) |
| `gpsd-regress/ublox-zed-f9t-ubx_nmea_l5.jsonl` | u-blox ZED-F9T, multi-band with L5 `sigid`s | `test/daemon/ublox-zed-f9t-ubx_nmea_l5.log.chk` (all) |
| `gpsd-regress/ublox-8-time.jsonl` | u-blox 8 timing; per-satellite `health`, `qual`, `prRes` | `test/daemon/ublox-8-time.log.chk` (first 300) |
| `gpsd-regress/sirfstarv.jsonl` | SiRFstar V, SiRF binary | `test/daemon/sirfstarv.log.chk` (first 300) |
| `gpsd-regress/quectel-l76k-nmea.jsonl` | Quectel L76K, NMEA 0183 | `test/daemon/quectel-l76k-nmea.log.chk` (first 300) |

## Licence

`doc-examples.jsonl` and everything under `gpsd-regress/` are derived from the GPSD project, which is
Copyright the GPSD project and licensed under BSD-2-Clause. The licence is in `COPYING.gpsd`.
The synthetic sessions follow the man page's field tables but contain no copied text.

## Captures from a real receiver

Captures from Chris's receiver are deferred (EXT-906). When they exist they are added here as extra
fixtures, without changing the tests.
