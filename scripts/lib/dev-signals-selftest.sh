#!/usr/bin/env bash
# Proves `maran dev` can be stopped with SIGINT when started in the background (issue #395).
#
# A probe script stands in for the command: it sources dev-signals.sh, optionally restores SIGINT,
# arms a trap that writes a marker, and waits. The probe is started with `&` from this
# non-interactive shell — the exact situation that makes the kernel ignore SIGINT — then sent
# `kill -INT`.
#
#   case 1  restore called   -> the trap MUST run (the fix works)
#   case 2  restore skipped  -> the trap MUST NOT run (control: the harness really reproduces the
#                               defect, so case 1 is not a pass over an environment that never
#                               ignored SIGINT)
#
# UNOBSERVED HERE: the full `maran dev` stack (docker, API, SPA); only the signal path is driven.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cat > "$work/probe.sh" <<PROBE
#!/usr/bin/env bash
set -euo pipefail
. "$here/dev-signals.sh"
if [ "\$1" = "restore" ]; then dev_signals_restore_int "\$0" "\$@"; fi
trap 'echo trapped > "$work/\$1.marker"; exit 0' INT
echo ready > "$work/\$1.ready"
sleep 30 &
wait \$!
PROBE

# run_case <restore|skip>: starts the probe in the background, interrupts it, reports whether the
# trap ran. Prints "trapped" or "ignored".
run_case() {
  local kind="$1" pid i
  bash "$work/probe.sh" "$kind" &
  pid=$!
  for i in $(seq 1 100); do
    [ -f "$work/$kind.ready" ] && break
    sleep 0.1
  done
  [ -f "$work/$kind.ready" ] || { echo "probe '$kind' never became ready"; kill "$pid" 2>/dev/null || true; return 2; }
  kill -INT "$pid"
  sleep 1
  if [ -f "$work/$kind.marker" ]; then
    echo trapped
  else
    echo ignored
  fi
  pkill -P "$pid" 2>/dev/null || true
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
}

failed=0
got="$(run_case restore)"
echo "restore called : $got (want trapped)"
[ "$got" = "trapped" ] || failed=1
got="$(run_case skip)"
echo "restore skipped: $got (want ignored)"
[ "$got" = "ignored" ] || failed=1

if [ "$failed" -eq 0 ]; then
  echo "SIGNALS VERDICT: OK"
else
  echo "SIGNALS VERDICT: FAILED"
  exit 1
fi
