#!/usr/bin/env bash
# T12.2 / AC12.2: the binary carries the release's version. `--version` must print it, and a
# running exporter must serve it as the version label of gpsd_exporter_build_info. No gpsd is
# needed; the exporter serves its own metrics without one.
#
# Usage: scripts/check-release-version.sh <binary> <version>
set -euo pipefail

binary="${1:?usage: check-release-version.sh <binary> <version>}"
version="${2:?usage: check-release-version.sh <binary> <version>}"

output=$("$binary" --version)
echo "--version: $output"
case "$output" in
  "gpsd-exporter $version "*) ;;
  *) echo "::error::--version printed '$output', not version $version."; exit 1 ;;
esac

port=19978
log=$(mktemp)
"$binary" --listen "127.0.0.1:$port" >"$log" 2>&1 &
pid=$!
trap 'kill "$pid" 2>/dev/null || true' EXIT

metrics=""
for _ in $(seq 1 50); do
  metrics=$(curl -fsS "http://127.0.0.1:$port/metrics" 2>/dev/null) && break
  sleep 0.2
done
build_info=$(printf '%s\n' "$metrics" | grep '^gpsd_exporter_build_info{' || true)
echo "build_info: $build_info"
if ! printf '%s\n' "$build_info" | grep -qF "version=\"$version\""; then
  echo "::error::gpsd_exporter_build_info does not carry version $version."
  cat "$log"
  exit 1
fi
