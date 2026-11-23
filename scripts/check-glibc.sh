#!/usr/bin/env bash
# T11.5 / AC11.7: fail if a binary needs a glibc symbol version newer than the limit, so it
# runs on Debian 12 and Raspberry Pi OS bookworm (glibc 2.36).
#
# Usage: scripts/check-glibc.sh <binary> [max-version]   (default max-version 2.36)
set -euo pipefail

binary="${1:?usage: check-glibc.sh <binary> [max-version]}"
max="${2:-2.36}"

versions=$(objdump -T "$binary" | grep -oE 'GLIBC_[0-9]+(\.[0-9]+)+' | sed 's/^GLIBC_//' | sort -u -V)
if [ -z "$versions" ]; then
  echo "::error::$binary has no GLIBC symbol versions; is it a dynamically linked glibc binary?"
  exit 1
fi

newest=$(printf '%s\n' "$versions" | tail -n 1)
echo "$binary needs GLIBC symbol versions up to $newest (limit $max)."
if [ "$(printf '%s\n%s\n' "$newest" "$max" | sort -V | tail -n 1)" != "$max" ]; then
  echo "::error::$binary needs GLIBC_$newest, newer than GLIBC_$max:"
  objdump -T "$binary" | grep -E "GLIBC_$newest\b" || true
  exit 1
fi
