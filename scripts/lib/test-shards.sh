#!/usr/bin/env bash
# `maran test backend --shard i/N --out DIR` and `maran test backend --combine DIR` (issue #289).
#
# Why: the backend suite took 25 of its 28 minutes in one assembly, Maran.Host.IntegrationTests,
# whose tests run one after another against one PostgreSQL. The pull request lane runs that assembly
# in N parallel SHARDS and then COMBINES their rows into one verdict against
# scripts/test-baseline.txt, so the census guarantee of `maran test` is kept: every baselined row is
# still compared, a vanished target or a dropped total still fails, and the verdict line is the same
# `TEST VERDICT:` line every lane gates on.
#
# HOW A SHARD IS CHOSEN, deterministically and exactly once:
#   1. The solution is built, and the integration assembly's tests are LISTED (`--list-tests`). That
#      listing is the ENUMERATION: every test class with its count of listed tests.
#   2. The classes are packed greedily into N shards by listed count, biggest first, ties broken by
#      name — so every shard, on every runner, computes the same partition from the same tree.
#   3. Shard i runs its classes; shard 1 also runs every OTHER test assembly in full. Each shard
#      runs the main part only (suite.sh SUITE_BACKEND_PARTS): the PerformanceBudget part runs on the
#      push-to-dev and nightly lanes, unsharded.
#   4. Each shard writes its rows, its named failures, its class list and the whole enumeration.
#
# The combine step PROVES the partition before it believes a count: all N shards are present, all
# carry the same enumeration, their class lists are pairwise disjoint and their union is the
# enumeration. Then it sums the rows per target and compares them against the baseline rows of the
# main part — and requires the integration assembly's summed declared total not to exceed what the
# enumeration allows, which is the axis an overlap would show on (growth is otherwise silent).
#
# Usage (through `maran test`, never directly):
#   maran test backend --shard 2/4 --out DIR
#   maran test backend --combine DIR
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

integration="Maran.Host.IntegrationTests"
mode="${1:-}"
shift || true

# shard_partition: reads `count<TAB>class` lines and prints `shard<TAB>class` for N shards.
shard_partition() {
  python3 - "$1" "$2" <<'PYPART'
"""Greedy, deterministic packing of test classes into N shards by listed test count."""
import sys

path, shards = sys.argv[1], int(sys.argv[2])
classes = []
with open(path, encoding="utf-8") as handle:
    for line in handle:
        count, name = line.rstrip("\n").split("\t")
        classes.append((int(count), name))
load = [0] * shards
for count, name in sorted(classes, key=lambda item: (-item[0], item[1])):
    target = min(range(shards), key=lambda index: (load[index], index))
    load[target] += count
    print(f"{target + 1}\t{name}")
PYPART
}

# shard_run: builds, enumerates, runs shard i of N, and writes its result files into DIR.
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

  echo "harness: shard $index/$total: building the solution" >&2
  if ! (cd "$root/backend" && dotnet build Maran.sln -nodeReuse:false) >"$log" 2>&1; then
    tail -40 "$log" >&2
    echo "SHARD $index/$total: ABORTED — the solution did not build."
    echo "FAILED build" >"$prefix.status"
    return 1
  fi

  # The enumeration. A theory case is listed as `Ns.Class.Method(args)`, so the arguments are cut
  # before the method is, and a dot inside an argument cannot shift the class name.
  (cd "$root/backend" && dotnet test "tests/$integration" --no-build --list-tests) 2>&1 |
    sed -nE 's/^    (Maran\..*)$/\1/p' | sed -E 's/\(.*$//; s/\.[^.]+$//' | sort | uniq -c |
    awk '{print $1 "\t" $2}' >"$prefix.enumeration"
  if [ ! -s "$prefix.enumeration" ]; then
    echo "SHARD $index/$total: ABORTED — the integration assembly listed no tests."
    echo "FAILED enumeration" >"$prefix.status"
    return 1
  fi
  shard_partition "$prefix.enumeration" "$total" | awk -F'\t' -v i="$index" '$1==i {print $2}' \
    >"$prefix.classes"

  # The filter: the main part, AND (this shard's classes, or — on shard 1 — any test outside the
  # integration assembly). A trailing dot after each class keeps `FooTests` from matching `FooTestsX`.
  local selection=""
  if [ "$index" -eq 1 ]; then
    selection="FullyQualifiedName!~$integration."
  fi
  local class
  while IFS= read -r class; do
    selection="${selection:+$selection|}FullyQualifiedName~$class."
  done <"$prefix.classes"
  if [ -z "$selection" ]; then
    echo "SHARD $index/$total: ABORTED — this shard was assigned nothing; use fewer shards."
    echo "FAILED empty" >"$prefix.status"
    return 1
  fi
  local filter
  filter="($(suite_backend_filter main))&($selection)"
  echo "harness: shard $index/$total: $(wc -l <"$prefix.classes") integration classes" >&2

  (cd "$root/backend" && dotnet test Maran.sln --no-build -m:1 -nodeReuse:false --filter "$filter") 2>&1 |
    tee -a "$log" >&2 || true
  suite_parse backend "$log" >"$prefix.tsv"
  suite_failures backend "$log" >"$prefix.failures"
  sed 's/^/  /' "$prefix.tsv"
  local failed
  failed="$(awk -F'\t' '{f+=$3} END{print f+0}' "$prefix.tsv")"
  if ! suite_reconcile_failures "$failed" "$prefix.failures" backend "$log"; then
    echo "SHARD $index/$total: UNRECONCILED"
    echo "FAILED unreconciled" >"$prefix.status"
    return 1
  fi
  echo "ran $index/$total" >"$prefix.status"
  echo "SHARD $index/$total: ran — $(awk -F'\t' '{p+=$2} END{print p+0}' "$prefix.tsv") passed / $failed failed."
  echo "  This is not a verdict: only 'maran test backend --combine' compares against the baseline."
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
  local index
  for index in $(seq 1 "${total:-0}"); do
    [ -e "$dir/shard-$index.tsv" ] || note "shard $index/$total: MISSING — its result never arrived."
  done

  # The partition proof.
  if ! python3 - "$dir" "${total:-0}" >"$work/proof" <<'PYPROOF'
"""Requires one enumeration across shards, disjoint class lists, and a union equal to it."""
import glob
import sys

directory, total = sys.argv[1], int(sys.argv[2])
enumerations = {open(path).read() for path in glob.glob(f"{directory}/shard-*.enumeration")}
problems = []
if len(enumerations) != 1:
    problems.append(f"the shards listed {len(enumerations)} different enumerations")
expected = {line.split("\t")[1] for line in next(iter(enumerations), "").splitlines() if line}
seen = {}
for index in range(1, total + 1):
    try:
        names = [line for line in open(f"{directory}/shard-{index}.classes").read().splitlines() if line]
    except OSError:
        continue
    for name in names:
        if name in seen:
            problems.append(f"{name} ran in shard {seen[name]} AND shard {index}")
        seen[name] = index
for name in sorted(expected - set(seen)):
    problems.append(f"{name} was assigned to no shard")
for name in sorted(set(seen) - expected):
    problems.append(f"{name} ran but is not in the enumeration")
for problem in problems:
    print(f"PARTITION: {problem}")
print(f"partition: {len(expected)} classes, {sum(int(l.split(chr(9))[0]) for l in next(iter(enumerations), '').splitlines() if l)} listed tests, {total} shards, each class exactly once"
      if not problems else "", end="")
sys.exit(1 if problems else 0)
PYPROOF
  then
    while IFS= read -r line; do note "$line"; done < <(grep '^PARTITION' "$work/proof")
  fi
  cat "$work/proof"; echo

  # Sum the rows per target.
  cat "$dir"/shard-*.tsv 2>/dev/null | awk -F'\t' '{p[$1]+=$2; f[$1]+=$3; i[$1]+=$4} END{for (k in p) print k "\t" p[k] "\t" f[k] "\t" i[k]}' |
    sort >"$work/run.tsv"
  sed 's/^/  /' "$work/run.tsv"
  cat "$dir"/shard-*.failures 2>/dev/null | sort -u >"$work/failures"
  while IFS= read -r name; do [ -n "$name" ] && note "backend: FAILED $name"; done <"$work/failures"

  # The overlap axis: the integration rows of both parts can hold at most what was listed.
  local listed ran budget
  listed="$(awk -F'\t' '{n+=$1} END{print n+0}' "$dir/shard-1.enumeration" 2>/dev/null || echo 0)"
  ran="$(awk -F'\t' -v k="$integration.dll" '$1==k {print $2+$3+$4}' "$work/run.tsv")"
  budget="$(awk -F'\t' -v k="backend	$integration.dll [PerformanceBudget]" \
    'index($0, k)==1 {print $3+$4+$5}' "$root/scripts/test-baseline.txt")"
  if [ "${ran:-0}" -gt $(( ${listed:-0} - ${budget:-0} )) ]; then
    note "COVERAGE: $integration ran ${ran:-0} tests, more than the ${listed:-0} listed minus ${budget:-0} budget tests — a test ran twice."
  fi

  # The census, against every main-part row of the baseline.
  grep -E '^backend	' "$root/scripts/test-baseline.txt" | sed -E 's/^backend	//' |
    grep -v ' \[PerformanceBudget\]	' >"$work/baseline.tsv" || true
  while IFS= read -r finding; do
    [ -n "$finding" ] && note "backend: ${finding#FINDING: }"
  done < <(suite_compare "$work/baseline.tsv" "$work/run.tsv")
  suite_note "the PerformanceBudget part is not in this lane: it is scored, with every other row, by"
  suite_note "the unsharded 'maran test backend' on the push to dev and nightly (backend.yml)."

  echo "== verdict =="
  echo "collected $(wc -l <"$work/run.tsv") targets: $(awk -F'\t' '{p+=$2} END{print p+0}' "$work/run.tsv") passed / $(awk -F'\t' '{f+=$3} END{print f+0}' "$work/run.tsv") failed, from ${total:-0} shards"
  if [ -n "$findings" ]; then
    echo "TEST VERDICT: FAILED — findings:$findings"
    return 1
  fi
  echo "TEST VERDICT: OK — every shard reported, each class exactly once, no failures, no total dropped."
}

case "$mode" in
  --shard)
    [ "${2:-}" = --out ] && [ -n "${3:-}" ] || { echo "usage: maran test backend --shard i/N --out DIR" >&2; exit 2; }
    shard_run "$1" "$3"
    ;;
  --combine)
    [ -n "${1:-}" ] || { echo "usage: maran test backend --combine DIR" >&2; exit 2; }
    combine_run "$1"
    ;;
  *)
    echo "usage: maran test backend --shard i/N --out DIR | --combine DIR" >&2
    exit 2
    ;;
esac
