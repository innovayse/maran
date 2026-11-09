#!/usr/bin/env bash
# `maran update-selftest`: proves the logic of `maran update` and `maran rollback` (issue #128)
# without a real server: signature, channel and checksum refusals, SemVer precedence, the release
# layout (install, replace, switch, previous, prune, interrupted-update detection, legacy adoption),
# the pre-update dump name contract and the archive refusals.
#
# THE SHAPE OF THIS TEST. This file is the driver: arguments, the assertion helpers, the tooling
# gate, the verdict and the floor. The cases live in parts it sources, each under 600 lines, in
# the order they depend on one another:
#   update-selftest-world.sh   the temporary world: shims, library copies, fixtures, helpers
#   update-selftest-verify.sh  versions, manifest, artifacts, the offline tarball, the curl argv
#   update-selftest-layout.sh  release.state, current, switch, previous, prune, interrupted, legacy
#   update-selftest-install.sh install from verified archives, the second sha256 check, replace
#   update-selftest-unpack.sh  archive member vetting and extraction, set-id stripping
#   update-selftest-dumps.sh   the pre-update dump name grammar, newest-by-name, pruning
#   update-selftest-notes.sh   the signed release date and notes URL: valid, absent, hostile, tampered
#   update-selftest-security.sh the signed security flag, the major rule, the unattended decision and report
#   update-selftest-requested.sh --requested, the check record and running state the panel reads, the report back
# The parts share this shell's variables and functions; none of them is run on its own.
#
# WHAT IT EXERCISES. The real CLI libraries installer/cli/lib/{common,release-version,release-verify,
# release-unpack,release-layout,update-dumps}.sh, sourced from COPIES in a temporary directory. The
# copies are rewritten with sed so their absolute path constants (/usr/local/maran, /etc/maran,
# /var/backups/maran-update, /var/lib/maran-cli-staging, and with them the baked public key path)
# point into a temporary root. That rewrite happens HERE and only on the copies: production code has
# no environment or flag that moves them, and the world part proves it by checking that every
# constant was rewritten and that the originals did contain them. No file outside the temporary
# directory is read or written, no root and no docker is needed.
#
# EVERY refusal is paired with an inverse control (rules/testing.md): the same command on the
# untouched fixture ACCEPTS, so a gate mutated to refuse everything fails here. A refusal must also
# come with its reason, so a typo in a fixture cannot pass for the check under test.
#
# UNOBSERVED HERE (printed again at the end, because a green line must not read as more):
#   - a real systemd: no unit is restarted and no health gate runs (update-health.sh is not driven);
#   - a real database: no pg_dump, pg_restore or migration runs (panel-dump.sh is not driven);
#   - the real release host: curl is a shim, so TLS and certificate verification are not exercised;
#   - cli_update and cli_rollback themselves: their order, the shared lock, the dump, the restarts
#     and the automatic rollback need root and systemd. What is driven is every library function
#     they take decisions from;
#   - file ownership when this runs unprivileged: chown is a recording shim and stat reports this
#     user's files as root's, so "owned by root" refusals are observed only when run as root.
#
# Usage:  maran update-selftest
# Exit:   0 only when every check passed and at least UPDATE_SELFTEST_MIN_CHECKS ran; 1 on a failed
#         check; 2 (DID NOT RUN) when a tool this needs is missing.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

VERDICT_PREFIX="UPDATE-SELFTEST VERDICT"
# The vacuity floor: fewer checks than this means a section silently stopped running. It sits just
# below the real count (501 when not root, counted by hand from the loops and call sites of the six parts), so a
# section that vanishes does not slip under it; adding checks never requires raising it.
readonly UPDATE_SELFTEST_MIN_CHECKS=499
readonly CLI_LIB_DIR="${root}/installer/cli/lib"
readonly SELFTEST_PARTS_DIR="${root}/scripts/lib"

case "${1:-}" in
  "") ;;
  -h|--help) echo "usage: maran update-selftest" >&2; exit 2 ;;
  *) echo "REFUSED: '$1' is no update-selftest argument; it takes none." >&2; exit 2 ;;
esac

checks=0

# selftest_failed: ends the run with the FAILED verdict on stderr.
selftest_failed() {
  echo "${VERDICT_PREFIX}: FAILED: $*" >&2
  exit 1
}

# selftest_pass: counts one passed check and says which.
selftest_pass() {
  checks=$((checks + 1))
  echo "   ok ${checks}: $*"
}

# run_capture: <command...>. Runs it in a subshell WITH errexit on (a command substitution inside
# an `if` would switch it off and let a library carry on past a failure it relies on set -e to
# stop at) and leaves its combined output in RUN_OUT and its status in RUN_STATUS.
run_capture() {
  local raw
  raw="$( ( set -e; "$@" ) 2>&1; echo "__STATUS__=$?" )"
  RUN_STATUS="${raw##*__STATUS__=}"
  RUN_OUT="${raw%__STATUS__=*}"
}

# run_returning: <command...>. Runs it in a subshell with errexit OFF and leaves its combined output
# in RUN_OUT and the status it RETURNED in RUN_STATUS; RUN_RETURNED is 1 when it returned at all and
# 0 when it ended the subshell with `exit` (cli_fail) before the marker could be printed. This is how
# a function documented to "return non-zero, not cli_fail" is told apart from one that exits.
run_returning() {
  local raw
  raw="$( ( set +e; "$@"; echo "__RETURNED__=$?" ) 2>&1 )"
  if [[ "$raw" == *__RETURNED__=* ]]; then
    RUN_RETURNED=1
    RUN_STATUS="${raw##*__RETURNED__=}"
    RUN_OUT="${raw%__RETURNED__=*}"
  else
    RUN_RETURNED=0
    RUN_STATUS=1
    RUN_OUT="$raw"
  fi
}

# expect_accept: <label> <command...>. The inverse control: the command must succeed.
expect_accept() {
  local label="$1"
  shift
  run_capture "$@"
  [ "$RUN_STATUS" -eq 0 ] \
    || selftest_failed "${label} was REFUSED (status ${RUN_STATUS}) - the gate refuses everything, or the fixture is broken: ${RUN_OUT}"
  selftest_pass "${label} ACCEPTED (expected)"
}

# expect_refuse: <label> <reason pattern or empty> <command...>. The command must fail, must not be
# a missing function or program, and its output must match the pattern (a refusal for the WRONG
# reason would otherwise pass for the one under test).
expect_refuse() {
  local label="$1" pattern="$2"
  shift 2
  run_capture "$@"
  [ "$RUN_STATUS" -ne 0 ] || selftest_failed "${label} was ACCEPTED - the check behind it is decoration."
  case "$RUN_OUT" in
    *"command not found"*) selftest_failed "${label}: the refusal is a missing command, not a check: ${RUN_OUT}" ;;
  esac
  if [ -n "$pattern" ] && ! printf '%s' "$RUN_OUT" | grep -qiE -- "$pattern"; then
    selftest_failed "${label} was refused for another reason than expected (wanted /${pattern}/): ${RUN_OUT}"
  fi
  selftest_pass "${label} REFUSED (expected)"
}

# expect_returns_refusal: <label> <reason pattern> <command...>. Like expect_refuse, for a function
# that must RETURN non-zero and not end the command: an `exit` inside it fails this check, because
# the caller of such a function tests its status and goes on.
expect_returns_refusal() {
  local label="$1" pattern="$2"
  shift 2
  run_returning "$@"
  [ "$RUN_RETURNED" -eq 1 ] || selftest_failed "${label}: the function ended the command (cli_fail) instead of returning: ${RUN_OUT}"
  [ "$RUN_STATUS" -ne 0 ] || selftest_failed "${label} was ACCEPTED - the check behind it is decoration."
  if [ -n "$pattern" ] && ! printf '%s' "$RUN_OUT" | grep -qiE -- "$pattern"; then
    selftest_failed "${label} was refused for another reason than expected (wanted /${pattern}/): ${RUN_OUT}"
  fi
  selftest_pass "${label} REFUSED by a returned status (expected)"
}

# expect_equal: <label> <expected> <actual>.
expect_equal() {
  [ "$2" = "$3" ] || selftest_failed "$1: expected '$2', got '$3'"
  selftest_pass "$1"
}

# expect_true: <label> <command...>. A plain assertion run outside the capture.
expect_true() {
  local label="$1"
  shift
  "$@" || selftest_failed "${label}: assertion failed: $*"
  selftest_pass "$label"
}

# expect_false: <label> <command...>. The command must FAIL (return non-zero); used for predicates
# that answer by status and never call cli_fail.
expect_false() {
  local label="$1"
  shift
  if "$@"; then selftest_failed "${label}: the predicate accepted: $*"; fi
  selftest_pass "$label"
}

# trim: strips whitespace, for comparing what a function printed.
trim() {
  printf '%s' "$1" | tr -d '[:space:]'
}

# sha_of: <file>. Prints the file's sha256 digest.
sha_of() {
  sha256sum -- "$1" | awk '{print $1}'
}

# repeat_char: <char> <count>. Prints the character <count> times.
repeat_char() {
  printf '%*s' "$2" '' | tr ' ' "$1"
}

# ---------------------------------------------------------------------------------------------
# Tooling gate: a missing tool is "did not run", never a pass and never a red.
# ---------------------------------------------------------------------------------------------
missing=""
for tool in openssl tar gzip sha256sum sed awk grep find install ln mv readlink stat mktemp cmp chmod \
            chown id sync cp sort tr tail head wc date touch truncate mkfifo mkdir rm ls dirname basename; do
  command -v "$tool" >/dev/null 2>&1 || missing="${missing} ${tool}"
done
if [ -n "$missing" ]; then
  suite_did_not_run "$VERDICT_PREFIX" "this machine lacks:${missing}. source scripts/dev first, and see: maran check"
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi

# shellcheck source=scripts/lib/update-selftest-world.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-world.sh"
# shellcheck source=scripts/lib/update-selftest-verify.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-verify.sh"
# shellcheck source=scripts/lib/update-selftest-layout.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-layout.sh"
# shellcheck source=scripts/lib/update-selftest-install.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-install.sh"
# shellcheck source=scripts/lib/update-selftest-unpack.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-unpack.sh"
# shellcheck source=scripts/lib/update-selftest-dumps.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-dumps.sh"
# shellcheck source=scripts/lib/update-selftest-notes.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-notes.sh"
# shellcheck source=scripts/lib/update-selftest-security.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-security.sh"
# shellcheck source=scripts/lib/update-selftest-requested.sh
. "${SELFTEST_PARTS_DIR}/update-selftest-requested.sh"

echo
echo "UNOBSERVED HERE: a real systemd - no unit is restarted and no health gate polls anything (update-health.sh is not driven)."
echo "UNOBSERVED HERE: a real database - no pg_dump, pg_restore or migration ran (panel-dump.sh is not driven)."
echo "UNOBSERVED HERE: a real release host - curl is a shim serving local files, so TLS and certificate verification are not exercised."
echo "UNOBSERVED HERE: cli_update and cli_rollback themselves - their order, the shared lock, the dump, the restarts and the automatic rollback; every library function they take decisions from is driven above."
echo "UNOBSERVED HERE: file ownership when this runs unprivileged - chown is a recording shim and stat reports this user's files as root's (run as root to observe it)."
echo "UNOBSERVED HERE: a move across filesystems (adoption refuses it) and a kill at an arbitrary instant - only the states a kill leaves are built by hand and resumed."
echo "UNOBSERVED HERE: the offline tarball's integrity-manifest.json and SELinux, OpenSSL 1.1.1 and RHEL tool versions."

if [ "$checks" -lt "$UPDATE_SELFTEST_MIN_CHECKS" ]; then
  selftest_failed "only ${checks} checks ran, below the floor of ${UPDATE_SELFTEST_MIN_CHECKS}: a section stopped running, and a verdict over fewer checks than the suite holds is not evidence."
fi
echo "${VERDICT_PREFIX}: OK (${checks} checks)"
