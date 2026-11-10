#!/usr/bin/env bash
# `maran test spa --shard i/N --out DIR`, `maran test spa --combine DIR` and
# `maran test spa --shards-selftest` (issue #289).
#
# Why: the SPA suite is one Playwright process with `workers: 1` in CI (frontend/playwright.config.ts)
# and took 12.8 of the frontend job's 16.4 minutes. The pull request lane runs it in N parallel
# SHARDS and then COMBINES their rows into one verdict against scripts/test-baseline.txt, so the
# census guarantee of `maran test spa` is kept: one row per spec file, every baselined file still
# compared, a vanished file or a dropped total still fails, and the verdict line is the same
# `TEST VERDICT:` line every lane gates on.
#
# HOW A SHARD IS CHOSEN, deterministically and exactly once:
#   1. The ENUMERATION is every spec file Playwright collects (`playwright test --list`, so the
#      config's `testMatch` and `testIgnore` decide; frontend/e2e/install has its own stage), each
#      with a WEIGHT.
#   2. The weight is the number of tests the committed baseline records for that file (passed +
#      failed + ignored). A file the baseline does not know yet weighs 1. Test count is a proxy for
#      duration, chosen because it is committed and therefore identical on every runner; a measured
#      duration would differ run to run and so would the partition.
#   3. Files are packed greedily into N shards by weight, heaviest first, ties broken by path, so
#      every shard computes the same partition from the same tree.
#   4. Shard i runs only its files (suite_run reads SUITE_SPA_SPECS) and writes its rows, its named
#      failures, its file list and the whole enumeration. It prints no verdict.
#
# The combine step PROVES the partition before it believes a count: all N shards are present and
# ran, all carry the same enumeration, their file lists are pairwise disjoint, their union is the
# enumeration, and every assigned file produced a row. Then it sums the rows and compares them with
# every `spa` row of the baseline through suite_compare, so a spec file that stopped being collected
# is named VANISHED exactly as in an unsharded run.
#
# Usage (through `maran test`, never directly):
#   maran test spa --shard 2/3 --out DIR
#   maran test spa --combine DIR
#   maran test spa --shards-selftest
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

# The baseline the weights and the census are read from. Overridable for the selftest only.
baseline_file="${SPA_SHARDS_BASELINE:-$root/scripts/test-baseline.txt}"
mode="${1:-}"
shift || true

# spa_enumerate: prints `weight<TAB>file` for every spec file Playwright collects, files relative to
# the testDir. Playwright itself answers (`--list`), so `testMatch` and `testIgnore` of
# frontend/playwright.config.ts decide what is a spec file and this script never keeps a second
# opinion; a file that fails to load makes the listing carry an error, and that aborts the shard.
spa_enumerate() {
  local listing
  listing="$(mktemp)"
  if ! (cd "$root/frontend" && CI=true npx playwright test --list --reporter=json) >"$listing" 2>/dev/null; then
    rm -f "$listing"
    echo "spa_enumerate: 'playwright test --list' failed." >&2
    return 1
  fi
  python3 - "$listing" "$baseline_file" <<'PYENUM'
"""Prints `weight<TAB>file` for each collected spec file; the weight is the baselined test count."""
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    payload = json.load(handle)
if payload.get("errors"):
    print(f"spa_enumerate: the listing carries {len(payload['errors'])} error(s)", file=sys.stderr)
    sys.exit(1)
files = set()


def walk(node):
    """Adds every spec file under a suite node, recursing into `describe` blocks."""
    for spec in node.get("specs", []):
        files.add(spec["file"])
    for child in node.get("suites", []):
        walk(child)


for top in payload.get("suites", []):
    walk(top)
weights = {}
with open(sys.argv[2], encoding="utf-8") as handle:
    for line in handle:
        fields = line.rstrip("\n").split("\t")
        if len(fields) == 5 and fields[0] == "spa":
            weights[fields[1]] = max(1, int(fields[2]) + int(fields[3]) + int(fields[4]))
for name in sorted(files):
    print(f"{weights.get(name, 1)}\t{name}")
PYENUM
  local status=$?
  rm -f "$listing"
  return $status
}

# spa_partition: reads `weight<TAB>file` lines and prints `shard<TAB>file` for N shards.
spa_partition() {
  python3 - "$1" "$2" <<'PYPART'
"""Greedy, deterministic packing of spec files into N shards by weight."""
import sys

path, shards = sys.argv[1], int(sys.argv[2])
files = []
with open(path, encoding="utf-8") as handle:
    for line in handle:
        weight, name = line.rstrip("\n").split("\t")
        files.append((int(weight), name))
load = [0] * shards
for weight, name in sorted(files, key=lambda item: (-item[0], item[1])):
    target = min(range(shards), key=lambda index: (load[index], index))
    load[target] += weight
    print(f"{target + 1}\t{name}")
PYPART
}

# shard_run: enumerates, runs shard i of N, and writes its result files into DIR.
shard_run() {
  local spec="$1" out="$2" index total
  index="${spec%/*}"
  total="${spec#*/}"
  case "$index/$total" in
    *[!0-9/]*|/*|*/) echo "REFUSED: --shard takes i/N, got '$spec'" >&2; return 2 ;;
  esac
  if [ "$index" -lt 1 ] || [ "$index" -gt "$total" ]; then
    echo "REFUSED: shard $index is outside 1..$total" >&2
    return 2
  fi
  mkdir -p "$out"
  local prefix="$out/shard-$index"
  local log="$prefix.log"

  if ! spa_enumerate >"$prefix.enumeration" || [ ! -s "$prefix.enumeration" ]; then
    echo "SHARD $index/$total: ABORTED — Playwright listed no spec file."
    echo "FAILED enumeration" >"$prefix.status"
    return 1
  fi
  spa_partition "$prefix.enumeration" "$total" | awk -F'\t' -v i="$index" '$1==i {print $2}' |
    LC_ALL=C sort >"$prefix.files"
  if [ ! -s "$prefix.files" ]; then
    echo "SHARD $index/$total: ABORTED — this shard was assigned nothing; use fewer shards."
    echo "FAILED empty" >"$prefix.status"
    return 1
  fi
  echo "harness: shard $index/$total: $(wc -l <"$prefix.files") of $(wc -l <"$prefix.enumeration") spec files" >&2

  SUITE_SPA_SPECS="$(cat "$prefix.files")" suite_run spa "$root" "$log"
  suite_parse spa "$log" >"$prefix.tsv"
  suite_failures spa "$log" >"$prefix.failures"
  if [ ! -s "$prefix.tsv" ]; then
    tail -20 "$log" >&2
    echo "SHARD $index/$total: ABORTED — the run produced no result. 'No tests found' is a failure."
    echo "FAILED no-rows" >"$prefix.status"
    return 1
  fi
  sed 's/^/  /' "$prefix.tsv"
  local failed
  failed="$(awk -F'\t' '{f+=$3} END{print f+0}' "$prefix.tsv")"
  if ! suite_reconcile_failures "$failed" "$prefix.failures" spa "$log"; then
    echo "SHARD $index/$total: UNRECONCILED"
    echo "FAILED unreconciled" >"$prefix.status"
    return 1
  fi
  echo "ran $index/$total" >"$prefix.status"
  echo "SHARD $index/$total: ran — $(awk -F'\t' '{p+=$2} END{print p+0}' "$prefix.tsv") passed / $failed failed."
  echo "  This is not a verdict: only 'maran test spa --combine' compares against the baseline."
}

# combine_run: proves the partition, sums the shards and prints the TEST VERDICT.
combine_run() {
  local dir="$1" findings="" work
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN
  note() { findings="$findings
  $1"; }

  local statuses=("$dir"/shard-*.status)
  if [ ! -e "${statuses[0]}" ]; then
    suite_did_not_run "TEST VERDICT" "no shard results under $dir."
    return "$SUITE_STATUS_DID_NOT_RUN"
  fi
  local status
  for status in "${statuses[@]}"; do
    grep -q '^ran ' "$status" || note "$(basename "$status" .status): $(cat "$status")"
  done
  local total
  total="$(sed -nE 's/^ran [0-9]+\/([0-9]+)$/\1/p' "${statuses[@]}" | sort -u)"
  if [ "$(printf '%s\n' "$total" | grep -c .)" -gt 1 ]; then
    note "the shards disagree on how many shards there are: $(printf '%s' "$total" | tr '\n' ' ')"
    total="$(printf '%s\n' "$total" | sort -n | tail -1)"
  fi
  local index
  for index in $(seq 1 "${total:-0}"); do
    [ -e "$dir/shard-$index.tsv" ] || note "shard $index/$total: MISSING — its result never arrived."
  done

  # The partition proof.
  if ! python3 - "$dir" "${total:-0}" >"$work/proof" <<'PYPROOF'
"""Requires one enumeration, disjoint file lists, a union equal to it, and a row per assigned file."""
import glob
import sys

directory, total = sys.argv[1], int(sys.argv[2])
enumerations = {open(path).read() for path in glob.glob(f"{directory}/shard-*.enumeration")}
problems = []
if len(enumerations) != 1:
    problems.append(f"the shards listed {len(enumerations)} different enumerations")
lines = [line for line in next(iter(enumerations), "").splitlines() if line]
expected = {line.split("\t")[1] for line in lines}
seen = {}
for index in range(1, total + 1):
    try:
        names = [line for line in open(f"{directory}/shard-{index}.files").read().splitlines() if line]
        rows = {line.split("\t")[0] for line in open(f"{directory}/shard-{index}.tsv").read().splitlines() if line}
    except OSError:
        continue
    for name in names:
        if name in seen:
            problems.append(f"{name} ran in shard {seen[name]} AND shard {index}")
        seen[name] = index
        if name not in rows:
            problems.append(f"{name} was assigned to shard {index} but produced no row")
    for key in sorted(rows - set(names) - {"<collection>"}):
        problems.append(f"{key} reported a row in shard {index} but was not assigned to it")
for name in sorted(expected - set(seen)):
    problems.append(f"{name} was assigned to no shard")
for name in sorted(set(seen) - expected):
    problems.append(f"{name} ran but is not in the enumeration")
for problem in problems:
    print(f"PARTITION: {problem}")
if not problems:
    print(f"partition: {len(expected)} spec files, {total} shards, each file exactly once", end="")
sys.exit(1 if problems else 0)
PYPROOF
  then
    while IFS= read -r line; do note "$line"; done < <(grep '^PARTITION' "$work/proof")
  fi
  cat "$work/proof"; echo

  # Sum the rows per target: a file appears in one shard only, so a repeat would be a finding above.
  cat "$dir"/shard-*.tsv 2>/dev/null |
    awk -F'\t' '{p[$1]+=$2; f[$1]+=$3; i[$1]+=$4} END{for (k in p) print k "\t" p[k] "\t" f[k] "\t" i[k]}' |
    LC_ALL=C sort >"$work/run.tsv"
  sed 's/^/  /' "$work/run.tsv"
  cat "$dir"/shard-*.failures 2>/dev/null | sort -u >"$work/failures"
  while IFS= read -r name; do
    if [ -n "$name" ]; then note "spa: FAILED $name"; fi
  done <"$work/failures"

  # The census, against every spa row of the baseline.
  { grep -E '^spa	' "$baseline_file" || true; } | sed -E 's/^spa	//' >"$work/baseline.tsv"
  if [ ! -s "$work/baseline.tsv" ]; then
    note "spa: no committed baseline — record one with 'maran test spa --accept'."
  else
    while IFS= read -r finding; do
      if [ -n "$finding" ]; then note "spa: ${finding#FINDING: }"; fi
    done < <(suite_compare "$work/baseline.tsv" "$work/run.tsv")
  fi

  echo "== verdict =="
  echo "collected $(wc -l <"$work/run.tsv") targets: $(awk -F'\t' '{p+=$2} END{print p+0}' "$work/run.tsv") passed / $(awk -F'\t' '{f+=$3} END{print f+0}' "$work/run.tsv") failed, from ${total:-0} shards"
  if [ -n "$findings" ]; then
    echo "TEST VERDICT: FAILED — findings:$findings"
    return 1
  fi
  echo "TEST VERDICT: OK — every shard reported, each spec file exactly once, no failures, no total dropped."
}

# selftest_plant: writes three healthy shards and a baseline that agrees with them into DIR.
selftest_plant() {
  local dir="$1" i
  mkdir -p "$dir"
  printf '3\ta/x.spec.ts\n2\tb/y.spec.ts\n1\tc/z.spec.ts\n' >"$dir/enumeration"
  spa_partition "$dir/enumeration" 3 >"$dir/partition"
  for i in 1 2 3; do
    cp "$dir/enumeration" "$dir/shard-$i.enumeration"
    awk -F'\t' -v i="$i" '$1==i {print $2}' "$dir/partition" >"$dir/shard-$i.files"
    : >"$dir/shard-$i.failures"
    echo "ran $i/3" >"$dir/shard-$i.status"
  done
  printf 'a/x.spec.ts\t3\t0\t0\n' >"$dir/shard-1.tsv"
  printf 'b/y.spec.ts\t2\t0\t0\n' >"$dir/shard-2.tsv"
  printf 'c/z.spec.ts\t1\t0\t0\n' >"$dir/shard-3.tsv"
  rm -f "$dir/enumeration" "$dir/partition"
  printf 'spa\ta/x.spec.ts\t3\t0\t0\nspa\tb/y.spec.ts\t2\t0\t0\nspa\tc/z.spec.ts\t1\t0\t0\n' >"$dir/baseline"
}

# selftest_case: runs combine on a planted directory and requires `expect` to be the verdict AND, for
# a failing case, `needle` to appear in the output; prints one result line.
selftest_case() {
  local name="$1" dir="$2" expect="$3" needle="$4" output status=0
  output="$(SPA_SHARDS_BASELINE="$dir/baseline" bash "$root/scripts/lib/spa-shards.sh" --combine "$dir" 2>&1)" || status=$?
  if [ "$expect" = OK ]; then
    if ! grep -q '^TEST VERDICT: OK' <<<"$output" || [ "$status" -ne 0 ]; then
      echo "SELF-TEST FAILED: $name — the healthy plant was not accepted (status $status)." >&2
      printf '%s\n' "$output" >&2
      return 1
    fi
  else
    if grep -q '^TEST VERDICT: OK' <<<"$output" || [ "$status" -eq 0 ] ||
       ! grep -qF "$needle" <<<"$output"; then
      echo "SELF-TEST FAILED: $name — expected a failure naming '$needle' (status $status)." >&2
      printf '%s\n' "$output" >&2
      return 1
    fi
  fi
  echo "  ok: $name"
}

# selftest_run: plants every way a sharded lane could lie and requires each to be named.
selftest_run() {
  local base case_dir cases=0
  base="$(mktemp -d)"
  trap 'rm -rf "$base"' RETURN
  selftest_plant "$base/good"
  # The control comes first: every failing case below is only evidence if this one is accepted.
  selftest_case "healthy shards are accepted" "$base/good" OK "" || return 1
  cases=$((cases + 1))

  case_dir="$base/missing"; cp -r "$base/good" "$case_dir"
  rm -f "$case_dir"/shard-3.*
  selftest_case "a vanished shard is named" "$case_dir" FAIL "shard 3/3: MISSING" || return 1
  cases=$((cases + 1))

  case_dir="$base/failed-shard"; cp -r "$base/good" "$case_dir"
  echo "FAILED no-rows" >"$case_dir/shard-2.status"
  selftest_case "a shard that did not run is named" "$case_dir" FAIL "shard-2: FAILED no-rows" || return 1
  cases=$((cases + 1))

  case_dir="$base/twice"; cp -r "$base/good" "$case_dir"
  echo "c/z.spec.ts" >>"$case_dir/shard-1.files"
  selftest_case "a file in two shards is named" "$case_dir" FAIL "c/z.spec.ts ran in shard 1 AND shard 3" || return 1
  cases=$((cases + 1))

  case_dir="$base/none"; cp -r "$base/good" "$case_dir"
  : >"$case_dir/shard-3.files"
  printf '' >"$case_dir/shard-3.tsv"
  selftest_case "a file in no shard is named" "$case_dir" FAIL "c/z.spec.ts was assigned to no shard" || return 1
  cases=$((cases + 1))

  case_dir="$base/norow"; cp -r "$base/good" "$case_dir"
  printf '' >"$case_dir/shard-3.tsv"
  selftest_case "an assigned file without a row is named" "$case_dir" FAIL "c/z.spec.ts was assigned to shard 3 but produced no row" || return 1
  cases=$((cases + 1))

  case_dir="$base/enumerations"; cp -r "$base/good" "$case_dir"
  printf '1\td/w.spec.ts\n' >>"$case_dir/shard-2.enumeration"
  selftest_case "shards that listed different enumerations are named" "$case_dir" FAIL "different enumerations" || return 1
  cases=$((cases + 1))

  case_dir="$base/dropped"; cp -r "$base/good" "$case_dir"
  printf 'a/x.spec.ts\t2\t0\t0\n' >"$case_dir/shard-1.tsv"
  selftest_case "a dropped total is named" "$case_dir" FAIL "a/x.spec.ts collected 2 passed against a baseline of 3" || return 1
  cases=$((cases + 1))

  case_dir="$base/failing"; cp -r "$base/good" "$case_dir"
  printf 'b/y.spec.ts\t1\t1\t0\n' >"$case_dir/shard-2.tsv"
  echo "b/y.spec.ts > a planted failure" >"$case_dir/shard-2.failures"
  selftest_case "a named failure is carried to the verdict" "$case_dir" FAIL "spa: FAILED b/y.spec.ts > a planted failure" || return 1
  cases=$((cases + 1))

  case_dir="$base/vanished"; cp -r "$base/good" "$case_dir"
  printf 'spa\td/gone.spec.ts\t4\t0\t0\n' >>"$case_dir/baseline"
  selftest_case "a baselined file absent from the run is named" "$case_dir" FAIL "d/gone.spec.ts" || return 1
  cases=$((cases + 1))

  # The partition itself: deterministic, disjoint, complete and balanced by weight.
  printf '9\ta\n5\tb\n4\tc\n3\td\n3\te\n1\tf\n' >"$base/weights"
  spa_partition "$base/weights" 3 >"$base/p1"
  spa_partition "$base/weights" 3 >"$base/p2"
  if ! cmp -s "$base/p1" "$base/p2" || [ "$(cut -f2 "$base/p1" | sort -u | wc -l)" -ne 6 ] ||
     [ "$(awk -F'\t' '{print $1}' "$base/p1" | sort -u | tr -d '\n')" != 123 ]; then
    echo "SELF-TEST FAILED: the partition is not deterministic, disjoint and complete." >&2
    return 1
  fi
  echo "  ok: the partition is deterministic, disjoint and complete"
  cases=$((cases + 1))

  # The real tree: enumeration is non-empty, excludes the install stage, and the real partition has
  # no empty shard at the counts the workflow uses.
  if ! spa_enumerate >"$base/real" || [ ! -s "$base/real" ] || grep -q '^install/' <<<"$(cut -f2 "$base/real")"; then
    echo "SELF-TEST FAILED: the real enumeration is empty or contains the install stage." >&2
    return 1
  fi
  local shards
  for shards in 3 4; do
    spa_partition "$base/real" "$shards" >"$base/real-p"
    if [ "$(cut -f1 "$base/real-p" | sort -u | wc -l)" -ne "$shards" ]; then
      echo "SELF-TEST FAILED: the real tree leaves a shard empty at $shards shards." >&2
      return 1
    fi
  done
  echo "  ok: the real enumeration ($(wc -l <"$base/real") spec files) has no empty shard at 3 or 4"
  cases=$((cases + 1))

  echo "SPA SHARDS SELFTEST: OK ($cases cases)"
}

case "$mode" in
  --shard)
    [ "${2:-}" = --out ] && [ -n "${3:-}" ] || { echo "usage: maran test spa --shard i/N --out DIR" >&2; exit 2; }
    shard_run "$1" "$3"
    ;;
  --combine)
    [ -n "${1:-}" ] || { echo "usage: maran test spa --combine DIR" >&2; exit 2; }
    combine_run "$1"
    ;;
  --shards-selftest)
    selftest_run
    ;;
  *)
    echo "usage: maran test spa --shard i/N --out DIR | --combine DIR | --shards-selftest" >&2
    exit 2
    ;;
esac
