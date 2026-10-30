#!/usr/bin/env bash
# `maran polygon selftest` — proves the `maran dev` polygon image gate in both directions, against a
# stubbed `docker`, so it needs no daemon and touches no image or container.
#
# WHAT IT ASSERTS
#
#   1. absent image       `dev_stage_require_polygon_image` exits 1 and names `maran polygon build ubuntu24`
#   2. unlabelled image   the same refusal and the same command
#   3. stale image        an image recording another fingerprint: the same refusal, both hashes named
#   4. current image      the INVERSE CONTROL: status 0, `CURRENT`, and no refusal — a gate mutated to
#                         refuse everything passes cases 1-3 and fails here (rules/testing.md)
#   5. `maran dev --selfcheck` on a stale image exits non-zero with the rebuild command, and the
#      stubbed `docker` saw no call but the image inspections — nothing was started.
#
# Every case also requires the stub to have been CALLED. A gate that stopped asking docker at all
# would otherwise be judged on whatever it printed without looking.
#
# UNOBSERVED HERE: a real `docker image inspect`, and a real rebuild through `maran polygon build`.
# The stub answers in the two shapes `polygon_image_fingerprint` reads (a label value, `<no value>`)
# and the failure shape of an absent image; a docker that changed those shapes is not seen here.
#
# Usage:  maran polygon selftest
# Exit:   0 only when every case passed; 1 otherwise, naming each failure.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/polygon.sh"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"

# The stub: answers `docker image inspect -f TEMPLATE IMAGE` from $STUB_IMAGE_STATE (`absent`,
# `unlabelled`, or `label=<fingerprint>`), records every call, and refuses anything else with 99 so
# an unexpected call is visible in the call log rather than quietly succeeding.
cat >"$work/bin/docker" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"$STUB_CALL_LOG"
[ "${1:-} ${2:-}" = "image inspect" ] || exit 99
case "$STUB_IMAGE_STATE" in
  absent) echo "Error: No such image: ${4:-}" >&2; exit 1 ;;
esac
case "${4:-}" in
  *Labels*)
    case "$STUB_IMAGE_STATE" in
      unlabelled) echo '<no value>' ;;
      label=*) echo "${STUB_IMAGE_STATE#label=}" ;;
    esac
    ;;
  *) echo "sha256:0000stub" ;;
esac
STUB
chmod +x "$work/bin/docker"

rebuild="$(polygon_build_command ubuntu24)"
current="$(polygon_fingerprint "$root" ubuntu24)"
passed=0
failed=0
findings=""

note_pass() {
  passed=$((passed + 1))
  echo "  PASS  $1"
}
note_fail() {
  failed=$((failed + 1))
  findings="$findings
  $1"
  echo "  FAIL  $1"
  sed 's/^/          /' "$work/out"
}

# run_stage: runs the dev stage in a child shell against the stub in state $1. Sets $status and
# leaves the combined output in $work/out and the stub's calls in $work/calls.
run_stage() {
  : >"$work/calls"
  set +e
  STUB_IMAGE_STATE="$1" STUB_CALL_LOG="$work/calls" PATH="$work/bin:$PATH" root="$root" bash -c '
    . "$root/scripts/lib/dev-stages.sh"
    dev_stage_require_polygon_image
  ' >"$work/out" 2>&1
  status=$?
  set -e
}

# expect_refused: the case's label, then the stub state. A refusal must exit 1, say REFUSED, name
# the rebuild command, and have asked the stub.
expect_refused() {
  local label="$1" state="$2"
  run_stage "$state"
  if [ "$status" -eq 1 ] && grep -q 'REFUSED' "$work/out" && grep -qF "$rebuild" "$work/out" \
    && [ -s "$work/calls" ]; then
    note_pass "$label — refused, naming '$rebuild'"
  else
    note_fail "$label — expected status 1, REFUSED and '$rebuild' (status $status, $(wc -l <"$work/calls") stub call(s))"
  fi
}

echo "maran dev polygon image gate, against a stubbed docker:"
expect_refused "absent image" absent
expect_refused "unlabelled image" unlabelled
expect_refused "stale image" "label=000000000000"
if grep -qF "000000000000" "$work/out" && grep -qF "$current" "$work/out"; then
  note_pass "stale image — both the recorded and this tree's fingerprint are named"
else
  note_fail "stale image — the refusal does not name both fingerprints (000000000000, $current)"
fi

run_stage "label=$current"
if [ "$status" -eq 0 ] && grep -q 'CURRENT' "$work/out" && ! grep -q 'REFUSED' "$work/out" \
  && [ -s "$work/calls" ]; then
  note_pass "current image — proceeds (inverse control)"
else
  note_fail "current image — expected status 0, CURRENT and no refusal (status $status)"
fi

# Case 5: the whole command, as a developer types it. It must refuse at the image gate, which runs
# before any container is started, so the stub may only ever have been asked to inspect an image.
: >"$work/calls"
set +e
STUB_IMAGE_STATE="label=000000000000" STUB_CALL_LOG="$work/calls" PATH="$work/bin:$PATH" \
  "$root/scripts/maran" dev --selfcheck >"$work/out" 2>&1
status=$?
set -e
if [ "$status" -ne 0 ] && grep -qF "$rebuild" "$work/out" && [ -s "$work/calls" ] \
  && ! grep -qv '^image inspect ' "$work/calls"; then
  note_pass "maran dev --selfcheck on a stale image — fails with '$rebuild', nothing started"
else
  note_fail "maran dev --selfcheck on a stale image — expected a refusal naming '$rebuild' and only image inspections (status $status; calls: $(tr '\n' ';' <"$work/calls"))"
fi

echo
if [ "$failed" -eq 0 ]; then
  echo "DEV IMAGE SELFTEST: OK — $passed passed / 0 failed"
  exit 0
fi
echo "DEV IMAGE SELFTEST: FAILED — $passed passed / $failed failed:$findings"
exit 1
