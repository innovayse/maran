#!/usr/bin/env bash
# `maran mutate` — the mutation harness rules/testing.md describes, as code instead of as a list to
# remember.
#
# A mutation run is how this repository checks that a protection is actually held up by a test:
# break the protection on purpose, confirm a NAMED test goes red, restore, move on. It is the
# strongest evidence we produce about security code, and it is worth exactly as much as the harness
# that produced it. Every harness defect measured here has failed in the same direction — reporting
# a protection as tested when it was not — so the nine rules in rules/testing.md live in this file
# and are not optional for the author of a run:
#
#   1. The mutation must LAND. A pattern that is absent, ambiguous, or spans lines is refused
#      before anything is written, because a silent no-op scores SURVIVED.
#   2. The mutation must COMPILE. A mutant that does not build has measured nothing; it is refused
#      and scored as nothing — never as a survivor, and never as "the test went red".
#   3. The score is taken over the WHOLE suite. There is no scope option here, and `suite_run`
#      has no scope parameter to pass one to.
#   4. A run that produces no test-result line is ABORTED, not scored. Nor is a run whose totals
#      and whose named failures disagree: those come from two parsers reading two different kinds
#      of line, and a verdict read off a silently-blind one is GREEN over real red. That is not a
#      hypothetical — the backend lane did exactly that, on every run it ever made, until the
#      bracket expression in `suite_failures` was fixed and `suite_selftest_parsers` was written to
#      plant a failure and prove the parser still finds it BEFORE the suite is run.
#   5. A run taken while another build holds the outputs is refused (see suite_refuse_concurrency).
#   6. The verdict is named failures plus a collected total, compared against the committed
#      baseline. Never an exit code.
#   7. The NAMED test must be the one that died. Anything else that went red is printed beside it,
#      and a red suite without the named test is not a kill.
#   8. A SURVIVED verdict owes a witness. Without `--witness` the verdict is WITHHELD: a mutant
#      that is a semantic no-op leaves the suite green and every other guard here sees nothing
#      wrong, so the author states what the mutation changed and why nothing died, or says nothing
#      at all.
#   9. Restore with a FRESH mtime, verify it with `cmp`, delete the backup — and keep the backup
#      OUT of the working tree. cargo and MSBuild key their caches on mtime, so a restore that puts
#      the original timestamp back leaves the MUTATED binary in the build directory and the next
#      run measures the previous experiment. Backups in the repository root are litter that outlives
#      the agent that made them; this command refuses a backup directory inside the tree.
#  11. ONE TREE, ONE MUTATION AT A TIME. The mutation goes into the shared working tree, so a
#      second run scoring at the same time is scoring somebody else's experiment — and it does not
#      fail, it prints a plausible verdict about the wrong code. Measured on 2026-09-08: a run
#      mutating only a doc COMMENT reported "KILLED — a_label_ending_with_a_hyphen_is_rejected"
#      because another agent's mutant was resident in a different file. `tree-lock.sh` holds an
#      flock for the length of the run, and a killed holder leaves a pending record rather than a
#      stale lock. Every run also gets a PRIVATE directory for its backup and logs, because the
#      shared scratchpad is what let one agent's write land inside another agent's run.
#  10. The local workspace is NOT the whole evidence. Some protections are invisible to it: moving
#      `setuid` before `setgroups` in the agent's privilege drop survives all 1396 local tests and
#      is killed only inside a polygon container, running as root on a real system. `--polygon`
#      adds those lanes; see the block below for why it ADDS them rather than replacing.
#
# `git checkout --` is not used for any of this, and never should be: it discards uncommitted work
# in the file it "restores", and this repository has lost work to it once already. The backup copy
# taken before the edit is the whole restore mechanism, and it is verified.
#
# WHY `--polygon` SUPPLEMENTS THE LOCAL SCORE AND DOES NOT REPLACE IT
#
# Three lanes are run and all three are always reported: the local workspace, and the `#[ignore]`d
# host suites as root on each family. Averaging them, or letting one stand in for the other, would
# throw away the only interesting result a two-lane run can produce — a disagreement:
#
#   killed locally, survives on the polygon  the local test asserts an argv, a rendered string or a
#                                            typed error, and nothing observes the EFFECT on a real
#                                            host. That is the shape docker/README.md records twice
#                                            (a grep of a unit file's text standing in for a runtime
#                                            directory; a fake ConfigHost standing in for nginx).
#   survives locally, killed on the polygon  the protection has no local test at all, and the only
#                                            thing holding it up is a suite that runs on two
#                                            machines a day. That is worth knowing precisely because
#                                            the local suite is what a developer runs before pushing.
#
# So the verdict is a per-lane table plus one overall line, and the overall line is KILLED if the
# named test died in ANY lane. A protection is tested if something, somewhere, catches it breaking;
# it is untested only when nothing does.
#
# In `--polygon` mode NO REPOSITORY FILE IS WRITTEN. The mutation is applied to an rsync snapshot
# outside the tree and every lane — the local one included — runs from that snapshot, with its own
# `CARGO_TARGET_DIR` outside the tree. The repository copy of the mutated file is backed up anyway
# and `cmp`ed at the end, so "the tree was not touched" is measured rather than asserted.
#
# Usage:
#   maran mutate --file <path> --find <text> --replace <text> --expect <test name> \
#                [--polygon] [--occurrence N] [--witness "what changed and why nothing died"]
#
# Example (a Rust protection):
#   maran mutate --file agent/crates/agent-core/src/validation/web/domain.rs \
#     --find 'if label.is_empty() {' --replace 'if false {' \
#     --expect domain_with_empty_label_is_rejected
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/polygon.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"

# shellcheck disable=SC1091
. "$root/scripts/lib/mutate-arguments.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/mutate-guards.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/mutate-restore.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/mutate-apply.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/mutate-lanes.sh"
# shellcheck disable=SC1091
. "$root/scripts/lib/mutate-verdict.sh"
