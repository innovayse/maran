#!/usr/bin/env bash
# The lane-ownership rule of the polygon, proved both ways (run by `maran polygon selftest`).
#
# A suite declared in POLYGON_LANE_OWNED_SUITES (scripts/lib/polygon.sh) is run and proved by another
# lane, so the `polygon` job does not require it, and that lane's verdict requires it to pass exactly.
# This file holds the declaration to that, with a controlled tree and planted logs, no docker:
#
#   1. an owned suite ABSENT from the polygon logs        -> the polygon scoring is OK
#   2. the same suite UNDECLARED (declaration emptied)    -> the polygon scoring FAILS naming it
#   3. an undeclared ignored suite beside a declared one  -> still FAILS naming only the undeclared one
#   4. the owning lane's log with the suite MISSING, empty, failing, or at the wrong count -> FAILED
#   5. the owning lane's log with the suite started and passed exactly the declared count  -> OK
#   6. the real declaration agrees with the tree, the lane script and the workflow
#
# The parts of `maran polygon verify` exercised are the functions it calls (polygon_without_lane_owned,
# polygon_verify_ran, polygon_lane_verdict), on the real implementations.
#
# UNOBSERVED HERE: a real cargo log, and the polygon-mail job itself; the planted logs follow the shape
# of the existing presence fixtures (polygon_selftest_presence).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/polygon.sh"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
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
}

# A tree with two ignored suites and the planted logs.
tree="$work/tree"
mkdir -p "$tree/agent/crates/agent/tests"
for name in alpha_on_a_real_host omega_on_a_real_host rogue_on_a_real_host; do
  printf '#[test]\n#[ignore]\nfn it() {}\n' >"$tree/agent/crates/agent/tests/$name.rs"
done

# banner <suite>: the line cargo prints when it starts a suite's binary.
banner() {
  echo "     Running tests/$1.rs (/tmp/target/debug/deps/$1-0123456789abcdef)"
}
# A polygon log in which alpha ran and passed one test; nothing else did.
polygon_log="$work/polygon.log"
{ banner alpha_on_a_real_host; echo 'test result: ok. 1 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out'; } >"$polygon_log"
observed="$work/observed.tsv"
printf 'alpha_on_a_real_host [tests/alpha_on_a_real_host.rs]\t1\t0\t0\n' >"$observed"
baseline="$work/baseline.tsv"
printf 'alpha_on_a_real_host [tests/alpha_on_a_real_host.rs]\t0\t0\t1\nomega_on_a_real_host [tests/omega_on_a_real_host.rs]\t0\t0\t1\n' >"$baseline"

# score_polygon: what `maran polygon verify` does with the discovered list, on the planted log. Prints
# the required list on the first line and the scoring on the rest; status is the scoring's.
score_polygon() {
  local required
  mapfile -t required < <(polygon_suites "$tree" | polygon_without_lane_owned)
  echo "required: ${required[*]}"
  polygon_verify_ran "$polygon_log" 0 "$observed" "$baseline" "${required[@]}"
}

declare_owned() {
  POLYGON_LANE_OWNED_SUITES=("omega_on_a_real_host|polygon-mail|docker/polygon/asserts/mail-lane.sh|1")
}

# Case 1: owned suite absent from the polygon logs, and the undeclared rogue suite... is not there yet
# (removed below), so only alpha and the owned omega exist.
rm "$tree/agent/crates/agent/tests/rogue_on_a_real_host.rs"
declare_owned
if out="$(score_polygon 2>&1)" && ! grep -q 'omega_on_a_real_host' <<<"$(tail -n +2 <<<"$out")"; then
  note_pass "owned suite absent from the polygon logs — the polygon scoring is OK"
else
  note_fail "owned suite absent from the polygon logs — expected OK ($out)"
fi

# Case 2: inverse control. Same tree, same log, declaration emptied: the suite is required, so the
# scoring fails and names it. Without this, case 1 would pass for a scorer that requires nothing.
POLYGON_LANE_OWNED_SUITES=()
if out="$(score_polygon 2>&1)"; then
  note_fail "undeclared suite absent from the polygon logs — expected FAILED, scoring passed"
elif grep -q "suite 'omega_on_a_real_host' never started" <<<"$out"; then
  note_pass "the same suite undeclared — FAILED, naming omega_on_a_real_host"
else
  note_fail "the same suite undeclared — failed, but not by naming the suite ($out)"
fi

# Case 3: a declared suite does not shelter another ignored suite.
printf '#[test]\n#[ignore]\nfn it() {}\n' >"$tree/agent/crates/agent/tests/rogue_on_a_real_host.rs"
declare_owned
if out="$(score_polygon 2>&1)"; then
  note_fail "undeclared ignored suite beside an owned one — expected FAILED, scoring passed"
elif grep -q "suite 'rogue_on_a_real_host' never started" <<<"$out" \
  && ! grep -q "suite 'omega_on_a_real_host'" <<<"$out"; then
  note_pass "undeclared ignored suite — FAILED naming only rogue_on_a_real_host, not the owned one"
else
  note_fail "undeclared ignored suite — wrong failure ($out)"
fi

# Case 4/5: the owning lane's own verdict.
lane_log="$work/lane.log"
lane_case() {
  local label="$1" expect="$2"
  if polygon_lane_verdict omega_on_a_real_host "$lane_log" >"$work/verdict.out" 2>&1; then
    got=ok
  else
    got=failed
  fi
  if [ "$got" = "$expect" ]; then
    note_pass "lane log: $label — $expect"
  else
    note_fail "lane log: $label — expected $expect, got $got ($(tr '\n' ' ' <"$work/verdict.out"))"
  fi
}
declare_owned
: >"$lane_log"
lane_case "empty (suite missing from its lane)" failed
{ banner alpha_on_a_real_host; echo 'test result: ok. 1 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out'; } >"$lane_log"
lane_case "another suite ran, the owned one did not" failed
{ banner omega_on_a_real_host; echo 'test result: ok. 0 passed; 0 failed; 1 ignored; 0 measured; 0 filtered out'; } >"$lane_log"
lane_case "owned suite started, no test ran" failed
{ banner omega_on_a_real_host; echo 'test result: ok. 2 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out'; } >"$lane_log"
lane_case "owned suite passed more than the declared count" failed
{ banner omega_on_a_real_host; echo 'test result: FAILED. 0 passed; 1 failed; 0 ignored; 0 measured; 0 filtered out'; } >"$lane_log"
lane_case "owned suite failed" failed
{ echo '     Finished tests/omega_on_a_real_host.rs (/tmp/target/debug/deps/omega_on_a_real_host-0123456789abcdef)'; echo 'test result: ok. 1 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out'; } >"$lane_log"
lane_case "result line without the suite ever starting" failed
{ banner omega_on_a_real_host; printf 'test result: \033[32mok\033[0m. 1 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out\n'; } >"$lane_log"
lane_case "owned suite started and passed exactly the declared count (coloured, as CI prints it)" ok
POLYGON_LANE_OWNED_SUITES=()
if polygon_lane_verdict omega_on_a_real_host "$lane_log" >/dev/null 2>&1; then
  note_fail "lane verdict for an undeclared suite — expected a refusal"
else
  note_pass "lane verdict for an undeclared suite — refused"
fi

# Case 6: the real declaration.
unset POLYGON_LANE_OWNED_SUITES
. "$root/scripts/lib/polygon.sh"
if problems="$(polygon_lane_declaration_problems "$root")"; then
  note_pass "the real declaration agrees with the tree, the lane script and the workflow"
else
  note_fail "the real declaration does not agree with the tree:$problems"
fi
if [ "${#POLYGON_LANE_OWNED_SUITES[@]}" -gt 0 ]; then
  note_pass "the real declaration is not empty"
else
  note_fail "POLYGON_LANE_OWNED_SUITES is empty, so nothing was checked"
fi

echo
if [ "$failed" -eq 0 ]; then
  echo "POLYGON LANES SELFTEST: OK — $passed passed / 0 failed"
  exit 0
fi
echo "POLYGON LANES SELFTEST: FAILED — $passed passed / $failed failed:$findings"
exit 1
