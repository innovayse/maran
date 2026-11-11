#!/usr/bin/env bash
# `maran codeql filter <sarif-dir>` / `maran codeql selftest` — see codeql-sarif-filter.py.
set -euo pipefail
exec python3 -I "$(cd "$(dirname "$0")" && pwd)/codeql-sarif-filter.py" "$@"
