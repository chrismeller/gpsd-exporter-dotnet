#!/usr/bin/env bash
# Lays out the container image's build context from the binaries workflow's artifacts:
# <dist>/gpsd-exporter-linux-x64 and -linux-arm64 (each with its .sha256) become
# <context>/linux/amd64/gpsd-exporter and <context>/linux/arm64/gpsd-exporter, the paths
# packaging/docker/Dockerfile copies by TARGETPLATFORM. Checksums are verified first.
# Only the architectures present in <dist> are laid out.
#
# Usage: scripts/image-context.sh <dist> <context>
set -euo pipefail

dist="${1:?usage: image-context.sh <dist> <context>}"
context="${2:?usage: image-context.sh <dist> <context>}"

found=0
for pair in x64:amd64 arm64:arm64; do
  rid="linux-${pair%%:*}"
  arch="${pair##*:}"
  binary="$dist/gpsd-exporter-$rid"
  [ -f "$binary" ] || continue
  (cd "$(dirname "$binary")" && sha256sum --check --strict "gpsd-exporter-$rid.sha256")
  mkdir -p "$context/linux/$arch"
  install -m 0555 "$binary" "$context/linux/$arch/gpsd-exporter"
  found=$((found + 1))
done

if [ "$found" -eq 0 ]; then
  echo "::error::No gpsd-exporter-linux-* binaries in $dist."
  exit 1
fi
find "$context" -type f -exec ls -l {} +
