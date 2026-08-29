#!/usr/bin/env bash
# Validates that every proto file compiles standalone. Used by CI (cross job).
set -euo pipefail
cd "$(dirname "$0")/.."
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
protoc --proto_path=proto --descriptor_set_out="$out/all.pb" proto/agent/v1/*.proto
echo "PROTO-OK"
