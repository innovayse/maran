#!/usr/bin/env bash
# `maran env selftest` — proves how `scripts/dev` applies `.env` under a caller's own values, in
# both directions, against a scratch copy of the file and a scratch `.env`. It needs no toolchain
# and touches neither the real `.env` nor the developer's shell.
#
# WHAT IT ASSERTS
#
#   1. first source          a name nobody set takes `.env`'s value
#   2. override, first       `Database__Database=x` set BEFORE the first source stands
#   3. override, re-source   `Database__Database=x` set in a shell that ALREADY sourced the file stands
#                            through the re-source a `maran` subprocess performs — the defect of
#                            issue #204, where the name was remembered and the value was not
#   4. edited `.env`         the INVERSE CONTROL: with nobody overriding, re-sourcing after `.env`
#                            changed applies the new value — a loader mutated to never overwrite
#                            passes cases 2-3 and fails here (rules/testing.md)
#   5. maran_env_from_file   answers true for the file's own value and false for an override, which
#                            is the question `maran dev`'s database check asks (dev-stages.sh)
#
# UNOBSERVED HERE: the rest of `maran dev`. This proves which value the environment holds after the
# file is sourced; that the stack then uses it is the database check's own business.
#
# Usage:  maran env selftest
# Exit:   0 only when every case passed; 1 otherwise, naming each failure.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

if [ "${1:-}" != "selftest" ]; then
  echo "usage: maran env selftest" >&2
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# A scratch repository holding the real `scripts/dev`, byte for byte, beside a scratch `.env`: the
# file resolves `.env` from its own location, so this is how it is pointed somewhere harmless.
mkdir -p "$work/repo/scripts" "$work/home"
cp "$root/scripts/dev" "$work/repo/scripts/dev"
printf '%s\n' '# scratch' 'Database__Database=from_file' >"$work/repo/.env"

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

# in_clean_shell: runs $1 in a bash with nothing inherited but PATH and a scratch HOME, so neither
# the developer's own `.env` values nor their records leak into a case. Prints what the script prints.
in_clean_shell() {
  env -i PATH="$PATH" HOME="$work/home" DEV="$work/repo/scripts/dev" bash -c "$1"
}

# expect: the case's label, the value it must print, and the shell script that prints it.
expect() {
  local label="$1" wanted="$2" actual
  actual="$(in_clean_shell "$3")"
  if [ "$actual" = "$wanted" ]; then
    note_pass "$label — '$actual'"
  else
    note_fail "$label — expected '$wanted', got '$actual'"
  fi
}

echo "scripts/dev applying .env under a caller's values:"

expect "first source takes .env's value" "from_file" '
  . "$DEV"
  printf "%s" "$Database__Database"'

expect "an override set before the first source stands" "override" '
  export Database__Database=override
  . "$DEV"
  printf "%s" "$Database__Database"'

# The re-source happens in a CHILD, as it does when `maran dev` runs `scripts/lib/run-dev.sh`: the
# parent's records arrive through the environment, exactly as they would from a developer's shell.
expect "an override set after sourcing survives a re-source in a child" "override" '
  . "$DEV"
  Database__Database=override bash -c ". \"\$DEV\"; printf \"%s\" \"\$Database__Database\""'

expect "an edited .env is picked up on re-source when nobody overrode it (inverse control)" "edited" '
  . "$DEV"
  sed -i "s/^Database__Database=.*/Database__Database=edited/" "$(dirname "$DEV")/../.env"
  . "$DEV"
  printf "%s" "$Database__Database"'
# The inverse control edited the scratch `.env`; put it back for the case below.
printf '%s\n' '# scratch' 'Database__Database=from_file' >"$work/repo/.env"

expect "maran_env_from_file tells the file's value from an override" "file:yes override:no" '
  . "$DEV"
  if maran_env_from_file Database__Database; then first=yes; else first=no; fi
  Database__Database=override
  if maran_env_from_file Database__Database; then second=yes; else second=no; fi
  printf "file:%s override:%s" "$first" "$second"'

echo
if [ "$failed" -eq 0 ]; then
  echo "DEV ENV SELFTEST: OK — $passed passed / 0 failed"
  exit 0
fi
echo "DEV ENV SELFTEST: FAILED — $passed passed / $failed failed:$findings"
exit 1
