#!/usr/bin/env bash
# `maran api` — the HTTP request contract between the SPA and the panel's CQRS commands.
#
# Every mutating endpoint binds its command directly, and neither build notices when a command
# member is renamed: the field simply stops arriving. This is the gate that notices. It joins the
# SPA's call sites to the controller actions on verb + path and compares the bound command's
# JSON-visible members against the TypeScript type of the body, then holds the rendered result
# against a committed baseline — the same arrangement `maran proto` gives the agent contract.
#
# The work is done by api-contract.mjs, which needs the SPA's own TypeScript compiler; this file
# exists to find node, say something useful when it is missing, and keep the dispatcher's shape.
#
#   maran api            check
#   maran api --accept   record the rendered contract as the new baseline (never run in CI)
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

if ! command -v node >/dev/null 2>&1; then
  echo "api-contract: node is not on PATH. Run 'source scripts/dev' first." >&2
  exit 2
fi

exec node "$root/scripts/lib/api-contract.mjs" "$@"
