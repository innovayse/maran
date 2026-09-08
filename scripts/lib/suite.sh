#!/usr/bin/env bash
# A LIBRARY, not a command: `maran test` and `maran mutate` both source this file.
#
# It exists because those two commands must agree, to the test, on what "the suite" is. A mutation
# score is a comparison between two runs of the same thing; if the harness and the baseline command
# disagree about which targets are in scope, the difference between them is attributed to the
# mutation. rules/testing.md's "score against the WHOLE suite, never a subset" is therefore not a
# flag either command passes — it is the fact that neither of them can express a subset at all.
#
# What lives here:
#
#   suite_require_toolchain  refuses a run whose tool is absent or too old
#   suite_refuse_concurrency refuses a run taken while another build holds the outputs
#   suite_run                runs one stack's whole suite, unfiltered, into a log
#   suite_parse              renders a log as one `key<TAB>passed<TAB>failed<TAB>ignored` row per target
#   suite_failures           names every test that went red
#   suite_selftest_parsers   proves those two parsers can still see a PLANTED failure and total
#   suite_reconcile_failures refuses a run whose totals and whose named failures disagree
#   suite_compare            compares parsed rows against a baseline and names what vanished
#
# Nothing here reads an exit code as a verdict. Every function reports named findings and a
# collected total, because both of the failures rules/testing.md records — `dotnet test` exiting 0
# with sixteen failures, and exiting 0 with a project that never ran — are invisible to `$?` and
# loud in the totals.

# suite_note: an honest line about what this harness cannot see. Printed by every command that
# depends on the observation, because a check that reads like a runtime probe and is a `pgrep` is
# the exact shape rules/testing.md calls decoration.
suite_note() {
  echo "UNOBSERVED HERE: $*"
}

# suite_require_toolchain: proves the tool for a stack exists and is the right major version.
#
# This is the "exited 0 having run nothing" guard. Both halves have happened in this repository on
# one day: `cargo` absent from PATH, and the bare-PATH `dotnet` being an 8.0 snap asked to run
# net9.0 test projects. Neither produced a test-result line and neither produced a non-zero exit,
# so a reader taking the exit code got a pass out of a run that measured nothing.
suite_require_toolchain() {
  local stack="$1"
  case "$stack" in
    rust)
      if ! command -v cargo >/dev/null 2>&1; then
        echo "REFUSED: cargo is not on PATH — this run would measure nothing." >&2
        echo "         source scripts/dev first, and see: maran check" >&2
        return 1
      fi
      if ! command -v cc >/dev/null 2>&1 && ! command -v gcc >/dev/null 2>&1; then
        echo "REFUSED: no C linker (cc) — cargo cannot link a test binary, so no test would run." >&2
        echo "         Debian family: sudo apt install -y build-essential" >&2
        return 1
      fi
      ;;
    backend)
      if ! command -v dotnet >/dev/null 2>&1; then
        echo "REFUSED: dotnet is not on PATH — this run would measure nothing." >&2
        return 1
      fi
      local version major
      version="$(dotnet --version 2>/dev/null | head -1)"
      major="${version%%.*}"
      case "$major" in
        ''|*[!0-9]*)
          echo "REFUSED: cannot read a version out of 'dotnet --version' (got '$version')." >&2
          return 1
          ;;
      esac
      if [ "$major" -lt 9 ]; then
        echo "REFUSED: dotnet $version cannot run this repository's net9.0 test projects." >&2
        echo "         The run would end with no test-result line at all. Fix PATH: source scripts/dev" >&2
        return 1
      fi
      ;;
    *)
      echo "REFUSED: unknown stack '$stack' (expected: rust, backend)" >&2
      return 1
      ;;
  esac
  return 0
}

# suite_refuse_concurrency: refuses to measure while another build is writing the outputs this run
# reads. rules/testing.md: "a count taken while another build is running is not a measurement."
#
# Two independent observations, because each is blind where the other is not:
#
#   1. The build lock. cargo takes an exclusive flock on `agent/target/<profile>/.cargo-lock` for
#      the whole of a build, so a non-blocking attempt on it is the build system's OWN answer to
#      "is someone else compiling", not a guess. This sees a concurrent cargo run by any user, in
#      any terminal, that shares this target directory.
#   2. Process names. `dotnet` has no equivalent lock — concurrent `dotnet test` runs simply share
#      `bin/Debug` and corrupt each other — so for the backend the only available observation is
#      that another build process exists. Our own descendants are excluded by walking parentage;
#      without that this command would always refuse itself.
#
# Blind spots are printed, not hidden.
suite_refuse_concurrency() {
  local stack="$1" root="$2"
  local busy=""

  if [ "$stack" = rust ]; then
    local lock="$root/agent/target/debug/.cargo-lock"
    if [ -e "$lock" ]; then
      if ! python3 - "$lock" <<'PYLOCK'
"""Exits 0 when cargo's build lock is free, 1 when another cargo build holds it."""
import fcntl
import sys

with open(sys.argv[1], "a") as handle:
    try:
        fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        sys.exit(1)
sys.exit(0)
PYLOCK
      then
        busy="$busy
  cargo holds the build lock on agent/target/debug/.cargo-lock (another cargo build is running)"
      fi
    fi
  fi

  local others
  others="$(python3 - "$$" "$stack" "$root" <<'PYPROCS'
"""Names build processes that are not descendants of this shell.

Reads /proc rather than shelling out to pgrep so that the parent chain is available: the harness
itself runs cargo, and a detector that could not tell its own child from a stranger would refuse
every run it started.
"""
import os
import sys

MINE, STACK, ROOT = int(sys.argv[1]), sys.argv[2], os.path.realpath(sys.argv[3])
# Only the builders that share THIS stack's output directory are refusals. A dotnet build cannot
# corrupt agent/target, and a harness that refused every Rust run while any dotnet process existed
# would be unusable in this repository, where several agents build at once — and an unusable gate is
# routed around, which is worse than a narrow one. Foreign builders are reported as LOAD instead,
# because rules/testing.md asks for the load to be stated when timing-sensitive results are quoted.
BUILDERS = {"rust": ("cargo", "rustc"), "backend": ("dotnet", "MSBuild", "msbuild")}[STACK]
FOREIGN = {"rust": ("dotnet", "MSBuild", "msbuild"), "backend": ("cargo", "rustc")}[STACK]
NOT_A_BUILD = ("--version", "--list-sdks", "--list-runtimes")

parents, commands = {}, {}
for entry in os.listdir("/proc"):
    if not entry.isdigit():
        continue
    pid = int(entry)
    try:
        with open(f"/proc/{pid}/stat") as handle:
            stat = handle.read()
        with open(f"/proc/{pid}/cmdline", "rb") as handle:
            argv = handle.read().split(b"\0")
    except OSError:
        continue
    parents[pid] = int(stat[stat.rfind(")") + 2:].split()[1])
    commands[pid] = [part.decode("utf-8", "replace") for part in argv if part]


def is_mine(pid):
    """Whether pid descends from this shell (or is it), so it is our own build."""
    seen = set()
    while pid > 1 and pid not in seen:
        if pid == MINE:
            return True
        seen.add(pid)
        pid = parents.get(pid, 1)
    return False


for pid, argv in sorted(commands.items()):
    if not argv or pid == os.getpid():
        continue
    name = os.path.basename(argv[0])
    if name not in BUILDERS and name not in FOREIGN:
        continue
    if any(flag in argv for flag in NOT_A_BUILD):
        continue
    if is_mine(pid):
        continue
    # Whether it shares OUR output directory, which is the only thing that makes it a corruption
    # risk rather than merely noisy. The process's own working directory is the observation: a
    # cargo running in a container against CARGO_TARGET_DIR=/tmp/target is visible in this host's
    # /proc and does NOT touch agent/target, and refusing on it would be a false alarm that trains
    # people to bypass the gate. When the cwd cannot be read at all — another user, or a container
    # running as root — that is stated rather than guessed.
    try:
        where = os.path.realpath(os.readlink(f"/proc/{pid}/cwd"))
        shares = where == ROOT or where.startswith(ROOT + os.sep)
        why = f"cwd {where}"
    except OSError:
        shares, why = False, "cwd unreadable (another user or a container) — assumed not to share this tree"
    kind = "BUSY" if (shares and name in BUILDERS) else "LOAD"
    print(f"{kind}\t  pid {pid}: {' '.join(argv)[:120]}\n{kind}\t      {why}")
PYPROCS
)"
  local load
  load="$(echo "$others" | sed -n 's/^LOAD\t//p')"
  others="$(echo "$others" | sed -n 's/^BUSY\t//p')"
  if [ -n "$others" ]; then
    busy="$busy
$others"
  fi
  if [ -n "$load" ]; then
    # Summarised, not listed: a backend build spawns a dozen MSBuild nodes, and a screen of them
    # buries the refusal underneath. The count is the load; the first few name what it is.
    echo "LOAD: $(echo "$load" | grep -c 'pid ') other build processes are running on this machine,"
    echo "      outside this stack's output directory. The first few:"
    echo "$load" | head -6
    echo "  They cannot corrupt this run's outputs, but they compete for CPU: discount or re-run"
    echo "  any timing-sensitive result, and state the load when quoting one."
  fi

  if [ -n "$busy" ]; then
    echo "REFUSED: another build is running — a count taken now is not a measurement." >&2
    echo "$busy" >&2
    echo "  (rules/testing.md: concurrent builds share the output directory and produce" >&2
    echo "   uniform, plausible failures. Wait for it to finish and run again.)" >&2
    return 1
  fi

  suite_note "this harness classifies another build by its working directory. A build whose cwd"
  suite_note "it cannot read — another user, or a container running as root — is reported as LOAD"
  suite_note "and NOT refused, so a container writing into this tree through a bind mount would be"
  if [ "$stack" = rust ]; then
    suite_note "missed by the process observation — but NOT by the cargo build lock checked above,"
    suite_note "which is taken on the shared target directory itself whoever holds it."
  else
    suite_note "missed entirely: dotnet takes no lock on bin/Debug, so for the backend the process"
    suite_note "list is the ONLY observation there is, and this is its blind spot."
  fi
  return 0
}

# suite_recheck_concurrency: the same observation, taken AFTER the run.
#
# Checking only at the start answers "was anyone building when I began", which is not the question.
# A `dotnet test` started by another agent thirty seconds into a ten-minute solution run shares
# bin/Debug for the rest of it, and the failures that produces are uniform and plausible. Measured
# here while building this command: two vstest processes were running the same
# Maran.Host.IntegrationTests.dll from two different sessions, the second having started after this
# harness had already passed its opening check. So the check is taken twice and a run that was
# joined mid-flight is reported as CONTAMINATED rather than scored.
suite_recheck_concurrency() {
  local stack="$1" root="$2"
  if suite_refuse_concurrency "$stack" "$root" >/dev/null 2>&1; then
    return 0
  fi
  echo "CONTAMINATED: another build STARTED while this run was in progress:"
  suite_refuse_concurrency "$stack" "$root" 2>&1 | grep -E '^\s+(pid|cargo)' || true
  echo "  The numbers below were taken against an output directory someone else was writing."
  return 1
}

# suite_run: runs one stack's ENTIRE suite into a log file.
#
# There is deliberately no scope parameter. `cargo test -p` and `dotnet test --filter` are the same
# defect wearing two hats, and the answer a filtered mutation run produces is SURVIVED — the
# direction that manufactures confidence. The only way to narrow this is to edit this function,
# in front of a reviewer.
#
# `--no-fail-fast` for cargo, and NOT for dotnet: `dotnet test` already continues across projects,
# and `--no-fail-fast` is rejected by VSTest as MSB1001, producing a run with no result line at all.
suite_run() {
  local stack="$1" root="$2" log="$3"
  case "$stack" in
    rust)
      (cd "$root/agent" && cargo test --workspace --no-fail-fast) >"$log" 2>&1 || true
      ;;
    backend)
      (cd "$root/backend" && dotnet test Maran.sln) >"$log" 2>&1 || true
      ;;
  esac
  return 0
}

# suite_parse: renders a run's log as one row per test target: key, passed, failed, ignored.
#
# The key is stable across runs and across machines — a cargo target's compiled binary carries a
# content hash that changes with every edit, so keying on it would make every target look as if it
# had vanished and been replaced the moment anything was mutated.
suite_parse() {
  local stack="$1" log="$2"
  python3 - "$stack" "$log" <<'PYPARSE'
"""Renders a test run's log as `key<TAB>passed<TAB>failed<TAB>ignored` rows, one per target."""
import collections
import re
import sys

stack, path = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8", errors="replace") as handle:
    lines = handle.read().splitlines()

rows = []
if stack == "rust":
    # The build directory is NOT matched at all, only the `deps/<binary>-<hash>` tail. It was once
    # anchored on a literal `target/`, and that is exactly the shape of check this repository keeps
    # paying for: a run with CARGO_TARGET_DIR elsewhere — which every polygon lane and every snapshot
    # lane uses, so that no lane can write into the tree's own agent/target — prints an absolute path
    # that need not contain the word "target" anywhere. Measured: a whole workspace run parsed as
    # five doc-test rows and nineteen VANISHED targets, which the baseline comparison caught.
    #
    # The KEY is unaffected by any of this: it is the binary name plus the SOURCE path, both the same
    # on a developer's machine, in a snapshot and inside a container, so a baseline recorded in one
    # place is comparable with a run taken in another.
    #
    # The banner is SEARCHED for, not anchored at the start of the line, and that is not laxness:
    # cargo writes it to stderr while the test binary writes `test <name> ... ` to stdout without a
    # trailing newline, so under `2>&1` the banner is regularly spliced into the MIDDLE of a test
    # line. Measured on ubuntu24: three of eleven polygon suites announced themselves as
    # `test deleting_an_account_... ...      Running tests/backup_on_a_real_host.rs (...deps/...)`,
    # produced no row here at all, and were then reported by the polygon lane as never having
    # started AND as having executed zero of their baselined tests — a real kill scored as ABORTED.
    # What still holds the match honest is the rest of the pattern, which the anchor never provided:
    # the word `Running`, the source path, and the `(<dir>/deps/<binary>-<hex>)` tail that closes it.
    # `Finished`, the other word cargo prints while naming every suite executable — which is what a
    # container that executed nothing produces — does not match this pattern and cannot.
    #
    # Banners are QUEUED and each `test result:` line consumes the oldest one, rather than the last
    # banner seen winning. That is what the splice forces, and it is exact rather than a tolerance:
    # cargo runs test binaries one at a time, so banners appear in start order and result lines in
    # finish order, and the two sequences are the same sequence. When the banner for the NEXT target
    # lands inside the CURRENT target's unterminated `test <name> ... ` line — which is precisely the
    # measured shape — a last-banner-wins reader files the current target's result under the next
    # target's name and shifts every row by one. Measured on the ubuntu24 log: account_deletion's two
    # tests were filed under backup, backup's eleven under cron, and so on down the run.
    running = re.compile(r"\s+Running\s+(.*?)\s+\(\S*deps/([A-Za-z0-9_]+)-[0-9a-f]+\)(?=\s|$)")
    doctests = re.compile(r"\s+Doc-tests\s+(\S+)(?=\s|$)")
    result = re.compile(
        r"^test result: \w+\. (\d+) passed; (\d+) failed; (\d+) ignored;"
    )
    started = collections.deque()
    for line in lines:
        banners = list(running.finditer(line))
        if banners:
            started.extend(f"{one.group(2)} [{one.group(1)}]" for one in banners)
            continue
        match = doctests.search(line)
        if match:
            started.append(f"doc-tests {match.group(1)}")
            continue
        match = result.match(line)
        if match and started:
            rows.append((started.popleft(), *match.groups()))
elif stack == "backend":
    # VSTest's per-project summary. The dll name is the project identity; the wording before it
    # ("Passed!" / "Failed!") is not read, because a verdict is the numbers, not the adjective.
    result = re.compile(
        r"^\s*\w+!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+).*?-\s+(\S+\.dll)"
    )
    for line in lines:
        match = result.match(line)
        if match:
            failed, passed, skipped, _total, dll = match.groups()
            rows.append((dll, passed, failed, skipped))

for key, passed, failed, ignored in rows:
    print(f"{key}\t{passed}\t{failed}\t{ignored}")
PYPARSE
}

# suite_failures: names every test that went red, one per line, sorted and deduplicated. A verdict
# is named failures, so a run that reports a count without them is half a verdict.
#
# A `[Theory]` case is named WITH its inline argument attached — VSTest prints
# `Failed Ns.Class.The_case(candidate: "/tmpfiles") [< 1 ms]` — so one theory contributes one line
# per case, and a caller looking for a test by name must match a SUBSTRING, never a whole line.
suite_failures() {
  suite_failures_raw "$1" "$2" | sort -u
  return 0
}

# suite_failures_raw: the same names, in log order and WITHOUT dedup.
#
# It exists so the COUNT can be reconciled against the totals. `sort -u` is right for a report and
# wrong for a count: two projects may legitimately fail a test of the same fully-qualified name, and
# a deduped list would then be SHORTER than the totals and read as a partial parse. The strict count
# axis in suite_reconcile_failures reads this function; every human-facing list reads the sorted one.
#
# `|| true` on both greps, deliberately: a clean run has no failure lines, grep exits 1 for that,
# and under `set -euo pipefail` that ended the caller before it printed anything. The first green
# run this function ever saw is what found it — which is the inverse control rules/testing.md asks
# for, arriving uninvited.
suite_failures_raw() {
  local stack="$1" log="$2"
  case "$stack" in
    rust)
      # `- should panic` is stripped, and it is the rust lane's version of the theory argument:
      # libtest writes a `#[should_panic]` test as `test tests::x - should panic ... FAILED`, so the
      # marker does NOT follow the test name. The greedy group still matches the line, so the count
      # reconciles — what it produces is a NAME that no source file contains, which every consumer
      # comparing names exactly (rather than by substring) reads as a different test. The agent has
      # no `#[should_panic]` today (`grep -rn should_panic agent/crates` is empty), so this is a
      # latent hole rather than an active one; it is closed now because the day it opens is a day a
      # verdict is already being read. Shape measured, not guessed: a scratch crate on this
      # repository's toolchain printed exactly that line for both a test that failed to panic and
      # one that panicked with the wrong message.
      { grep -E '^test .* \.\.\. FAILED$' "$log" || true; } |
        sed -E 's/^test (.*) \.\.\. FAILED$/\1/; s/ - should panic$//'
      ;;
    backend)
      # NO bracket expression carries an escaped bracket. POSIX gives backslash no meaning inside
      # `[...]`, so `[A-Za-z0-9_.+<>,()\[\]-]` — which is what this line used to be — ends at the
      # `]` that was meant to be a member: GNU grep read it as a one-character class followed by a
      # literal `-]`, which matches nothing a test runner has ever printed. The backend lane
      # therefore named ZERO failures on every run it ever made, and `score_lane` read the empty
      # list as GREEN over 95 real failures. It was "verified by hand" and passed, because the
      # interactive shell it was verified in resolves `grep` to a wrapper that accepts the escapes
      # and `/usr/bin/grep` does not — which is why the verification below is a fixture the harness
      # runs itself, in its own shell, and not a thing an author checks once.
      #
      # The name is taken up to the duration VSTest always prints, so the `  Error Message:` header
      # that follows every failure no longer contributes a phantom test called "Message".
      { grep -a -E '^[[:space:]]*(Failed|Error)[[:space:]]' "$log" || true; } |
        sed -nE 's/^[[:space:]]*(Failed|Error)[[:space:]]+(.*[^[:space:]])[[:space:]]+\[[^][]*\]$/\2/p'
      ;;
  esac
  return 0
}

# suite_selftest_parsers: the POSITIVE CONTROL for this file's two log parsers.
#
# Both of them are searches, and a search that has silently stopped matching reports the reassuring
# answer: no failures named, no targets counted, GREEN. That is not hypothetical here — the backend
# failure pattern contained `[...\[\]...]`, POSIX gives backslash no meaning inside a bracket
# expression, and so it matched nothing on any run ever taken. Every reader who checked it by hand
# checked it in an interactive shell whose `grep` was a wrapper that accepts the escape.
#
# So the check is a fixture instead: a log fragment in exactly the shape the runner prints, planted
# with a name and a total the parsers MUST find, run through the SAME functions the verdict uses, in
# the SAME shell. If a parser can no longer see a failure it is handed on purpose, it certainly
# cannot see a real one, and no verdict taken with it is worth printing.
suite_selftest_parsers() {
  local stack="$1" sample names rows expected_names expected_row failed_total
  sample="$(mktemp)"
  case "$stack" in
    rust)
      # The third and fourth lines are the SPLICE, planted deliberately: cargo writes its
      # `Running` banner to stderr while a test binary writes `test <name> ... ` to stdout without a
      # newline, so under `2>&1` the next target's banner lands inside the current target's line.
      # It is not a curiosity — measured on a real ubuntu24 polygon run, three of eleven suites
      # announced themselves this way, produced no row at all under the old line-anchored pattern,
      # and the lane scored a real mutation kill as ABORTED. So the fixture carries the shape, and
      # the assertions below require BOTH targets, in the right order, with the right totals.
      printf '%s\n' \
        '   Running unittests src/lib.rs (/tmp/x/deps/selftest_control-0123456789abcdef)' \
        'test selftest::a_planted_failure_name ... FAILED' \
        'test selftest::a_planted_should_panic_case - should panic ... FAILED' \
        'test selftest::a_planted_spliced_line ...      Running tests/selftest_spliced.rs (/tmp/x/deps/selftest_spliced-0123456789abcdef)' \
        'ok' \
        'test result: FAILED. 2 passed; 2 failed; 3 ignored; 0 measured; 0 filtered out' \
        'test result: ok. 4 passed; 0 failed; 5 ignored; 0 measured; 0 filtered out' \
        >"$sample"
      # The second line is the rust lane's answer to the theory argument: libtest puts
      # `- should panic` between the name and the marker, so a fixture without it would let the
      # extractor go on producing a name no source file contains.
      expected_names='selftest::a_planted_failure_name
selftest::a_planted_should_panic_case'
      expected_row='	2	2	3'
      failed_total=2
      ;;
    backend)
      # Lines 4-7 are the `[Theory]` shapes, and they are planted for the same reason the rust
      # splice is. A harness scoring a real mutation printed SURVIVED over three genuinely red
      # tests because its matcher expected the marker to follow the test NAME, and xUnit puts the
      # inline argument in between: the case is written
      # `The_case(candidate: "/tmpfiles")`, not `The_case`. The second theory carries a BRACKETED
      # argument (`values: [1, 2]`), because the extraction below anchors on the duration VSTest
      # prints in brackets at end of line, and an argument containing brackets is exactly what
      # would break that anchor. Both shapes are copied from a real `dotnet test` run on this
      # repository's own toolchain (SDK 9.0.317, xunit 2.9.3, Microsoft.NET.Test.Sdk 18.9.0);
      # `[< 1 ms]` is the duration a sub-millisecond case actually gets, so it is planted too.
      printf '%s\n' \
        '  Failed Selftest.Control.A_planted_failure_name [3 ms]' \
        '  Error Message:' \
        '   Assert.True() Failure' \
        '  Failed Selftest.Control.A_planted_theory_case(candidate: "/tmpfiles") [< 1 ms]' \
        '  Error Message:' \
        '  Failed Selftest.Control.A_planted_theory_case(values: [1, 2]) [2 ms]' \
        '  Error Message:' \
        'Failed!  - Failed:     3, Passed:     2, Skipped:     3, Total:     8, Duration: 1 ms - Selftest.Control.dll (net9.0)' \
        >"$sample"
      expected_names='Selftest.Control.A_planted_failure_name
Selftest.Control.A_planted_theory_case(candidate: "/tmpfiles")
Selftest.Control.A_planted_theory_case(values: [1, 2])'
      expected_row='	2	3	3'
      failed_total=3
      ;;
    *)
      rm -f "$sample"
      echo "SELF-TEST REFUSED: unknown stack '$stack'" >&2
      return 1
      ;;
  esac

  names="$(suite_failures "$stack" "$sample")"
  rows="$(suite_parse "$stack" "$sample")"

  if [ "$names" != "$expected_names" ]; then
    rm -f "$sample"
    echo "SELF-TEST FAILED: the $stack failure parser was handed $failed_total planted failure" >&2
    echo "                  line(s) and named:" >&2
    printf '                    [%s]\n' "$names" >&2
    echo "                  instead of the planted tests:" >&2
    printf '                    [%s]\n' "$expected_names" >&2
    echo "                  It is blind to at least one line shape, so every GREEN and every" >&2
    echo "                  NOT-A-KILL it would produce below is meaningless. Fix suite_failures." >&2
    return 1
  fi
  case "$rows" in
    *"$expected_row"*) : ;;
    *)
      rm -f "$sample"
      echo "SELF-TEST FAILED: the $stack totals parser was handed a planted result line reading" >&2
      echo "                  2 passed / $failed_total failed / 3 ignored and produced [$rows]." >&2
      echo "                  It is blind, so the collected total below would be an invention." >&2
      echo "                  Fix suite_parse." >&2
      return 1
      ;;
  esac
  if [ "$stack" = rust ]; then
    local expected
    expected="selftest_control [unittests src/lib.rs]	2	2	3
selftest_spliced [tests/selftest_spliced.rs]	4	0	5"
    if [ "$rows" != "$expected" ]; then
      rm -f "$sample"
      echo "SELF-TEST FAILED: the rust totals parser was handed two targets, the second announced" >&2
      echo "                  by a banner spliced into the first one's test line, and produced" >&2
      echo "                  [$rows] instead of both rows in start order. A parser that loses a" >&2
      echo "                  spliced target reports it as never started and as zero tests run," >&2
      echo "                  which scores a real kill as ABORTED. Fix suite_parse." >&2
      return 1
    fi
  fi

  # The reconciler is a guard, and a guard owes the same proof its parsers do: it must ACCEPT a
  # consistent pair and REFUSE an inconsistent one. A reconciler that has quietly become
  # unconditional-return-0 is indistinguishable from a correct one on every green run.
  local list
  list="$(mktemp)"
  printf '%s\n' "$names" >"$list"
  if ! suite_reconcile_failures "$failed_total" "$list" "$stack" "$sample" >/dev/null; then
    rm -f "$sample" "$list"
    echo "SELF-TEST FAILED: the $stack reconciler REFUSED a consistent pair — $failed_total failed" >&2
    echo "                  in the totals and the same $failed_total names. A guard that refuses" >&2
    echo "                  everything passes every test that only hands it broken input, and it" >&2
    echo "                  would abort every honest run. Fix suite_reconcile_failures." >&2
    return 1
  fi
  : >"$list"
  if suite_reconcile_failures "$failed_total" "$list" "$stack" "$sample" >/dev/null; then
    rm -f "$sample" "$list"
    echo "SELF-TEST FAILED: the $stack reconciler ACCEPTED $failed_total failure(s) in the totals" >&2
    echo "                  against an EMPTY name list. That is the shape that reads GREEN over a" >&2
    echo "                  red suite. Fix suite_reconcile_failures." >&2
    return 1
  fi
  printf '%s\n' "$expected_names" | head -n 1 >"$list"
  if [ "$failed_total" -gt 1 ] &&
     suite_reconcile_failures "$failed_total" "$list" "$stack" "$sample" >/dev/null; then
    rm -f "$sample" "$list"
    echo "SELF-TEST FAILED: the $stack reconciler ACCEPTED a PARTIAL list — $failed_total failed in" >&2
    echo "                  the totals, one name given. A partial list looks like a complete one," >&2
    echo "                  so the harness would name the wrong culprit with full confidence." >&2
    echo "                  Fix suite_reconcile_failures." >&2
    return 1
  fi
  rm -f "$sample" "$list"

  echo "self-test: the $stack log parsers named every planted failure, including the shapes that"
  echo "self-test: put text between the test name and the marker, read a planted total, and the"
  echo "self-test: reconciler refused an empty list and a partial one."
  return 0
}

# suite_reconcile_failures: cross-checks the per-target failure TOTALS against the NAMED failures.
#
# This is the positive control for `suite_failures` itself, and it exists because the harness has
# already reported the safe answer when the truth was the dangerous one: a backend run printed
# `2324 passed / 7 failed` and then `GREEN — nothing died`, because the totals came from one parser
# and the verdict came from another, and nothing on the way through asked the two to agree.
#
# The parsers read different lines of the same log, so either can go blind on its own without the
# other noticing — and the direction that goes unnoticed is always the reassuring one: a verdict
# read off an empty failure list is GREEN, and GREEN is what an author quotes when arguing a check
# can be deleted. So the two observations are reconciled before either is believed, and a
# disagreement is a run that measured nothing, not a run that measured zero.
#
# It also separates "no list" from "an empty list": a caller that never produced a failure file at
# all must not be scored as a caller whose suite had no failures.
suite_reconcile_failures() {
  local failed="$1" failures="$2" stack="${3:-}" log="${4:-}" named raw
  if [ ! -e "$failures" ]; then
    echo "UNRECONCILED: no failure list exists at $failures. A lane that produced NO list is not a"
    echo "              lane that produced an EMPTY one, and only the second one is green."
    return 1
  fi
  named="$(grep -c . "$failures" || true)"
  if [ "$failed" -gt 0 ] && [ "${named:-0}" -eq 0 ]; then
    echo "UNRECONCILED: the per-target totals say $failed test(s) FAILED, and the failure parser"
    echo "              named NONE of them. One of the two is blind to this log, so this run"
    echo "              cannot be scored either way — a verdict taken from the empty list would"
    echo "              read GREEN over $failed real failures."
    return 1
  fi
  if [ "$failed" -eq 0 ] && [ "${named:-0}" -gt 0 ]; then
    echo "UNRECONCILED: the per-target totals say NOTHING failed, and the failure parser named"
    echo "              ${named} failing test(s). One of the two is reading lines that are not"
    echo "              there, so neither number is evidence."
    return 1
  fi
  # The THIRD axis, and the one the first two are blind to: a PARTIAL list. The checks above compare
  # zero against non-zero, so they pass the moment the parser names a single test — and a list that
  # names three of ninety-five reads exactly like a complete one. It is the worse case of the two,
  # because the harness then prints a confident verdict ("NOT A KILL — the suite went red, but X did
  # not") over a log in which X may be sitting unparsed. So the counts must be EQUAL, not merely
  # both non-zero. This axis needs the raw, undeduplicated names, which is why it needs the log.
  if [ -n "$stack" ] && [ -n "$log" ] && [ -e "$log" ]; then
    local unique
    raw="$(suite_failures_raw "$stack" "$log" | grep -c . || true)"
    unique="$(suite_failures "$stack" "$log" | grep -c . || true)"
    if [ "${named:-0}" -ne "${unique:-0}" ]; then
      echo "UNRECONCILED: the failure list at $failures holds ${named:-0} name(s), and the parser"
      echo "              reading the same log names ${unique:-0}. The list is not what the parser"
      echo "              produced — it was truncated, overwritten, or written from another run —"
      echo "              so the names about to be scored are not this run's names."
      return 1
    fi
    if [ "${raw:-0}" -ne "$failed" ]; then
      echo "UNRECONCILED: the per-target totals say $failed test(s) FAILED and the failure parser"
      echo "              named ${raw:-0}. A list that is neither empty nor complete is the worst"
      echo "              of the three: it looks like a finished verdict. Every unnamed failure is"
      echo "              a line shape this parser cannot see — a theory case, a spliced line, an"
      echo "              output format that changed — so nothing here can be scored."
      return 1
    fi
  fi
  return 0
}

# suite_compare: compares parsed rows against a baseline file and prints named findings.
#
# It prints one `FINDING: ...` line per problem and nothing when the run is clean, so a caller can
# collect findings across stacks and report them together rather than exiting at the first.
#
# Two axes are compared, because one of them alone was measured going blind. The PASSED column
# catches a test that stopped succeeding or stopped existing on a normal target. The DECLARED total
# — passed + failed + ignored — catches the same loss on a target whose tests do not run on this
# machine at all: the `*_on_a_real_host` polygon suites are `#[ignore]`d off a polygon container, so
# their whole count lives in the ignored column, and a suite deleting a test moved a row from
# `0 0 10` to `0 0 9` with no finding of any kind. Only a FALL in the declared total is a finding;
# see the argument at the comparison itself.
#
# The finding that matters most is the one no exit code carries: a target present in the baseline
# and absent from the run. A test project whose build broke does not fail the run, it DISAPPEARS
# from it, and the surviving projects then print `Passed!` over a smaller total.
suite_compare() {
  local baseline="$1" observed="$2"
  python3 - "$baseline" "$observed" <<'PYCOMPARE'
"""Compares observed per-target totals against a committed baseline and names every difference."""
import sys


def load(path):
    """Reads a `key<TAB>passed<TAB>failed<TAB>ignored` file into an ordered mapping."""
    rows = {}
    try:
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                line = line.rstrip("\n")
                if not line or line.startswith("#"):
                    continue
                key, passed, failed, ignored = line.split("\t")
                rows[key] = (int(passed), int(failed), int(ignored))
    except FileNotFoundError:
        return None
    return rows


baseline, observed = load(sys.argv[1]), load(sys.argv[2])
if baseline is None:
    print("FINDING: no committed baseline — record one with `maran test --accept` and commit it")
    sys.exit(0)

for key in baseline:
    if key not in observed:
        print(f"FINDING: target VANISHED from the run (present in the baseline): {key}")
for key in observed:
    if key not in baseline:
        print(f"FINDING: target is new since the baseline (accept it if intended): {key}")
for key, (passed, _failed, ignored) in baseline.items():
    if key not in observed:
        continue
    seen = observed[key][0]
    if seen < passed:
        print(f"FINDING: {key} collected {seen} passed against a baseline of {passed}")
    # The DECLARED total — passed + failed + ignored — is the axis a polygon suite can shrink on.
    # On this host every `#[ignore]`d host test lands in the ignored column, so a row reads
    # `0 passed / 0 failed / N ignored` and the passed comparison above is blind to it: a suite
    # losing all N of its tests was measured producing no finding at all.
    #
    # Only a FALL is a finding, and only on the sum:
    #   - a new polygon test raises ignored, so the sum rises: not a finding, that is the work;
    #   - a test moving from ignored to running raises passed and lowers ignored by the same one,
    #     so the sum is unchanged: not a finding either, and it should not be — a host test that
    #     became runnable everywhere is a gain, and a gate that demanded it stay ignored would
    #     punish it;
    #   - a test moving from running to ignored (someone quietening a failure with `#[ignore]`)
    #     leaves the sum unchanged but LOWERS passed, which the check above already names;
    #   - a test deleted, renamed out of the harness, or lost with its `#[test]` attribute lowers
    #     the sum, on whichever column it lived in. That is the one this line exists for.
    declared = passed + _failed + ignored
    seen_declared = sum(observed[key])
    if seen_declared < declared:
        print(
            f"FINDING: {key} declared {seen_declared} tests against a baseline of {declared} "
            f"({declared - seen_declared} fewer; observed "
            f"{observed[key][0]} passed / {observed[key][1]} failed / {observed[key][2]} ignored "
            f"against {passed} / {_failed} / {ignored})"
        )

total_baseline = sum(row[0] for row in baseline.values())
total_observed = sum(row[0] for row in observed.values())
if total_observed < total_baseline:
    print(
        f"FINDING: collected total dropped: {total_observed} passed against "
        f"a baseline of {total_baseline}"
    )
declared_baseline = sum(sum(row) for row in baseline.values())
declared_observed = sum(sum(row) for row in observed.values())
if declared_observed < declared_baseline:
    print(
        f"FINDING: declared total dropped: {declared_observed} tests against "
        f"a baseline of {declared_baseline}"
    )
PYCOMPARE
}
