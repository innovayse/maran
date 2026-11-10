#!/usr/bin/env bash
# `maran mutate`, step 1: the usage text, the argument parser and did_not_run.
#
# SOURCED by scripts/lib/mutate.sh (split out of it, issue #355), in the order the run needs it. This
# file runs at the top level of that script and shares its variables, traps and `-euo pipefail`.

usage() {
  cat >&2 <<'USAGE'
usage: maran mutate --file <path> --find <text> --replace <text> --expect <test name>
                    [--polygon] [--occurrence N] [--witness <text>]

  --file        the source file to mutate, relative to the repository root
  --find        the exact single-line text to replace (must occur exactly once,
                or --occurrence must name which one)
  --replace     what to put there. It must COMPILE: a mutant that does not build
                has measured nothing and is refused, not scored
  --expect      the test that must go red. A red suite without THIS test is not a kill
  --occurrence  which occurrence to replace, 1-based, when --find is ambiguous
  --polygon     score three lanes instead of one: the local workspace, and every #[ignore]d
                host suite as root inside a container on BOTH families. Agent code only.
                Some protections — the privilege drop above all — are invisible to the local
                workspace, which answers SURVIVED for them. The lanes are reported separately;
                a disagreement between them is the finding, not a detail to average away.
                No repository file is written in this mode: the mutation goes to a snapshot.
  --witness     required only to publish a SURVIVED verdict: what the mutation changed in
                behaviour, and why no test noticed
  --wait N      queue for up to N seconds behind another mutation run holding the tree lock.
                The default is 0 — refuse at once and name the holder — because a silent
                block is indistinguishable from a hung harness. While waiting, a line is
                printed every 15 seconds saying who is being waited for.
  --recover     put back a mutation that a KILLED run left applied to this tree, from the
                backup that run took, verified with cmp. Takes no other argument.

The stack is chosen by path: agent/ runs the Rust workspace, backend/ runs the solution.
The score is always the whole suite; there is no way to narrow it from the command line.
USAGE
  exit 2
}

file=""
find_text=""
replace_text=""
expect_test=""
occurrence=""
witness=""
polygon=0
wait_for=0
recover=0
while [ $# -gt 0 ]; do
  case "$1" in
    --polygon)    polygon=1; shift ;;
    --file)       file="${2:-}"; shift 2 ;;
    --find)       find_text="${2:-}"; shift 2 ;;
    --replace)    replace_text="${2:-}"; shift 2 ;;
    --expect)     expect_test="${2:-}"; shift 2 ;;
    --occurrence) occurrence="${2:-}"; shift 2 ;;
    --witness)    witness="${2:-}"; shift 2 ;;
    --wait)       wait_for="${2:-0}"; shift 2 ;;
    --recover)    recover=1; shift ;;
    -h|--help)    usage ;;
    # Named explicitly so the refusal explains itself. Someone reaching for these is trying to make
    # the run finish sooner, and the answer they would get back is the one that manufactures
    # confidence.
    --filter|-p|--package|--test|--lib)
      echo "REFUSED: $1 narrows the run. A mutant scored against part of the suite is scored" >&2
      echo "         blind to the rest, and the answer that produces is SURVIVED." >&2
      echo "         (rules/testing.md: score against the WHOLE suite, never a subset.)" >&2
      exit 2
      ;;
    *) echo "unknown argument: $1" >&2; usage ;;
  esac
done

# did_not_run: this command's third answer, for a run that never started.
#
# A mutation run has always had three possible outcomes and only two statuses. KILLED, SURVIVED,
# NOT A KILL and ABORTED are all answers about a suite that RAN; a lock held by a peer, an absent
# toolchain, a blind parser or a mutant that does not compile are answers about a run that never
# happened, and returning 1 for those made them indistinguishable from "the suite went red and the
# named test did not die" — the one verdict a reader acts on. The vocabulary and the status come
# from scripts/lib/suite.sh, so `maran test`, `maran mutate` and `maran polygon verify` all say it
# the same way.
did_not_run() {
  suite_did_not_run "VERDICT" "$*"
  exit "$SUITE_STATUS_DID_NOT_RUN"
}
