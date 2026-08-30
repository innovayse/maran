#!/usr/bin/env bash
# Generates THIRD-PARTY-NOTICES.md from the dependencies this repository actually builds with.
#
# The installer distributes BINARIES built from those dependencies, and the MIT, Apache-2.0 and
# BSD licences they carry all require their notices to travel with the distribution. Shipping
# without this file is a licence violation, quietly, in every release.
#
# This script gathers the three inputs; licenses.py turns them into the document. Nothing here
# reaches the network — the answer must be the one the build used, and must come out the same on
# a machine with no internet.
#
# Usage:
#   scripts/maran licenses           write THIRD-PARTY-NOTICES.md
#   scripts/maran licenses --check   fail if the file is out of date, changing nothing
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

check_only=0
if [ "${1:-}" = "--check" ]; then
  check_only=1
fi

output="$root/THIRD-PARTY-NOTICES.md"
generated="$(mktemp)"
cargo_json="$(mktemp)"
npm_json="$(mktemp)"
trap 'rm -f "$generated" "$cargo_json" "$npm_json"' EXIT

# Written to files rather than passed as arguments: cargo's metadata alone is larger than the
# kernel's argument limit, and the failure ("Argument list too long") names neither cause.
(cd "$root/agent" && cargo metadata --format-version 1 --all-features --offline) > "$cargo_json"
(cd "$root/frontend" && npm ls --omit=dev --all --json 2>/dev/null || echo '{}') > "$npm_json"

python3 "$root/scripts/lib/licenses.py" "$root" "$cargo_json" "$npm_json" > "$generated"

if [ "$check_only" -eq 1 ]; then
  if [ ! -f "$output" ] || ! diff -q "$output" "$generated" >/dev/null; then
    echo "THIRD-PARTY-NOTICES.md is out of date. Run: maran licenses" >&2
    [ -f "$output" ] && diff -u "$output" "$generated" | head -40
    exit 1
  fi
  echo "NOTICES-OK"
  exit 0
fi

cp "$generated" "$output"
echo "wrote $(basename "$output")"
