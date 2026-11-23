#!/usr/bin/env bash
# T11.4: `systemd-analyze verify` passes for the unit, with no warnings.
#
# verify needs ExecStart to exist and resolves the units the unit refers to, so this uses a
# temporary root with the system's units, a stand-in binary and a stub gpsd.service, and
# fails on any output.
#
# Usage: scripts/verify-systemd-unit.sh [unit]   (default packaging/systemd/gpsd-exporter.service)
set -euo pipefail

unit="${1:-packaging/systemd/gpsd-exporter.service}"
root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT

mkdir -p "$root/usr/local/bin" "$root/etc/systemd/system" "$root/usr/lib/systemd"
# The system's own units (sysinit.target, multi-user.target and so on).
cp -r /usr/lib/systemd/system "$root/usr/lib/systemd/"
printf '#!/bin/sh\nexit 0\n' > "$root/usr/local/bin/gpsd-exporter"
chmod 0755 "$root/usr/local/bin/gpsd-exporter"
cp "$unit" "$root/etc/systemd/system/gpsd-exporter.service"
printf '[Unit]\nDescription=stand-in for gpsd\n[Service]\nExecStart=/bin/true\n' > "$root/etc/systemd/system/gpsd.service"

output=$(systemd-analyze verify --root="$root" "$root/etc/systemd/system/gpsd-exporter.service" 2>&1) || {
  printf '%s\n' "$output"
  echo "::error::systemd-analyze verify failed for $unit."
  exit 1
}
if [ -n "$output" ]; then
  printf '%s\n' "$output"
  echo "::error::systemd-analyze verify reported problems in $unit."
  exit 1
fi
echo "systemd-analyze verify: $unit is clean."
