#!/usr/bin/env bash
# Runs the agent's Rust toolchain — build, clippy, tests, fmt — against the crates in agent/.
#
# It prefers the machine's own cargo and falls back to a container when the machine cannot link.
# Rust needs a C linker for its build scripts (libc, rustix, getrandom all have one), and a
# workstation without build-essential cannot compile the agent at all. Docker already carries this
# repository's dev dependencies (rules/architecture.md: docker is dev-only, production is native),
# so a toolchain container is the same kind of dependency as the dev database — not a new runtime.
#
# The container image is pinned to the same Rust version the workstation and CI use. A "latest"
# image would silently change the compiler under the code between two runs, and the first person to
# see the difference would be whoever's build broke.
#
# Usage:
#   scripts/maran agent build      compile every crate
#   scripts/maran agent test       run the unit and integration tests
#   scripts/maran agent lint       clippy with warnings denied
#   scripts/maran agent fmt        apply rustfmt
#   scripts/maran agent check      fmt --check, clippy, test and doc — what CI runs
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

# shellcheck disable=SC1091
. "$root/scripts/lib/tree-lock.sh"

# For `suite_did_not_run` and `$SUITE_STATUS_DID_NOT_RUN` only: this command runs cargo itself and
# parses nothing, but it refuses for exactly the reasons `maran test` refuses, and a reader deserves
# one vocabulary for that across the harness rather than one per script.
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

# lock_for_writing / probe_for_reading: this command's two relationships to the tree lock
# (scripts/lib/tree-lock.sh). `fmt` REWRITES every Rust source file in place, so it is a writer and
# takes the exclusive lock — a formatter running over a planted mutant produces a verdict about
# source that existed for a moment, and the mutation harness's byte-exact restore then discards
# whatever the formatter wrote to the rest of that file. Everything else here only reads, so it
# probes: a clippy finding or a red test that belongs to somebody else's experiment is the most
# believable false regression this repository produces.
#
# Neither of them FAILS on a refusal: they report DID NOT RUN and return the status this harness
# reserves for a run that never happened (scripts/lib/suite.sh). A peer holding the lock has not
# found a defect in the agent, and a developer who cannot tell that from a red clippy run learns to
# ignore both.
lock_for_writing() {
  tree_lock_acquire "$root" 0 "agent $1" || agent_did_not_run "the tree lock is held (named above)."
  trap tree_lock_release EXIT INT TERM
  tree_lock_refuse_pending "$root" readonly ||
    agent_did_not_run "a killed mutation run left its mutant in this tree (named above)."
}

probe_for_reading() {
  tree_lock_probe "$root" ||
    agent_did_not_run "a mutation run holds this tree's write lock (named above)."
  tree_lock_refuse_pending "$root" readonly ||
    agent_did_not_run "a killed mutation run left its mutant in this tree (named above)."
}

# agent_did_not_run: says which of the three answers this is, on stdout, and exits with the status
# that carries it.
agent_did_not_run() {
  suite_did_not_run "AGENT VERDICT" "$*"
  exit "$SUITE_STATUS_DID_NOT_RUN"
}

# `|| true`, and it is not decoration. This file runs under `set -euo pipefail`, and with rustc
# absent from PATH the pipeline's first command exits 127, pipefail makes that the pipeline's
# status, and `set -e` then killed the whole script HERE — at the third line of work, with not one
# byte on stdout or stderr and a status of 127. Measured: `maran agent check` on a sanitized PATH
# printed nothing at all. That is the worst shape in rules/testing.md wearing no clothes: a caller
# keeping stdout is handed an empty file, and 127 is neither this harness's 0, 1 nor 2. The status
# has to survive long enough for `require_agent_toolchain` below to say which answer this is, so the
# absence of rustc is tolerated here and reported there.
rust_version="$(rustc --version 2>/dev/null | awk 'NR==1 {print $2}' || true)"
# And the parse is checked, not trusted. Whatever `rustc` resolves to on the caller's PATH is not
# guaranteed to be rustc; field 2 of line 1 of something else is an arbitrary string, and it was
# going straight into a container tag. Measured with a decoy on PATH: the refusal above printed
# `neither natively nor in rust:(GNU\n(C)\nGPLv3+:\n...-slim` — six lines of a licence notice
# inside an image name. A tag that cannot be a version is not a pin, so it falls back to the pin
# this file states, which is the one thing here known to exist.
case "$rust_version" in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) rust_version="" ;;
esac
image="rust:${rust_version:-1.98.0}-slim"

usage() {
  echo "usage: maran agent {build|test|lint|fmt|check}" >&2
  exit 1
}

# has_linker: whether cargo can build here. Without a C linker every build script fails, which is
# a confusing wall of "could not compile libc" rather than one clear message.
has_linker() {
  command -v cc >/dev/null 2>&1 || command -v gcc >/dev/null 2>&1
}

# run_cargo: runs one cargo invocation, natively when possible and in the pinned container
# otherwise. The container runs as the invoking user so target/ does not end up owned by root, and
# the crate cache lives in a named Docker volume rather than a directory in the repository: a
# working tree is for the product's own files, and a cache sitting in it shows up in every file
# listing, every editor sidebar and every "what is this?" from the next person to clone.
run_cargo() {
  if has_linker; then
    (cd "$root/agent" && cargo "$@")
    return
  fi

  # Both families are named, because the product supports both (rules/architecture.md
  # "Supported systems"). A message that offers only the Debian package tells half the
  # supported audience to run a command their machine does not have, and a remediation naming
  # the wrong tool is a defect of the same severity as the behaviour it describes.
  echo "no C linker on this machine — using $image to build in a container instead."
  echo "  to build natively: Debian family  sudo apt install -y build-essential"
  echo "                     RHEL family    sudo dnf groupinstall -y 'Development Tools'"
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -v "$root:/repo" \
    -w /repo/agent \
    -v maran-cargo-cache:/cargo \
    -e CARGO_HOME=/cargo \
    -e "RUSTDOCFLAGS=${RUSTDOCFLAGS:-}" \
    "$image" \
    sh -c 'command -v protoc >/dev/null 2>&1 || {
             apt-get update -qq >/dev/null 2>&1
             apt-get install -y -qq protobuf-compiler >/dev/null 2>&1
           }
           exec cargo "$@"' -- "$@"
}

command_name="${1:-}"
[ -z "$command_name" ] && usage

# THE TOOLCHAIN, before any stage runs. rules/testing.md: "a gate you cannot run is not a gate that
# passed" — and this command failed that in the direction that manufactures findings rather than
# confidence. Measured with cargo off PATH:
#
#   $ maran agent check
#   scripts/lib/agent.sh: line 92: cargo: command not found     <- once per stage, four times
#   AGENT-CHECK FAILED — stages that failed: fmt clippy test doc
#
# Four named stages reported as failing, about source not one of them read. `cargo: command not
# found` is not a defect in the agent; it is this run not happening, and this harness already has a
# word for that (scripts/lib/suite.sh). So every tool the CHOSEN execution path needs is proved
# before the first stage, and a missing one refuses the whole invocation rather than producing a
# finding per stage.
#
# Deliberately NOT `suite_require_toolchain rust`: that one also demands a C linker, and this
# command's whole point is that it falls back to a container when the machine cannot link — so
# refusing on `cc` would refuse a run this script can complete. A guard that refuses what the
# command can do teaches the reader to ignore refusals. The two execution paths are therefore
# proved separately, each against what it actually needs:
#
#   native     cargo, and protoc — agent/crates/agent/build.rs generates the proto contract at
#              compile time, so without protoc every stage stops inside that build script.
#   container  docker, which is the whole of that path: the image carries cargo and installs
#              protoc itself.
require_agent_toolchain() {
  if has_linker; then
    command -v cargo >/dev/null 2>&1 || {
      echo "REFUSED: cargo is not on PATH — no stage of this command would run." >&2
      echo "         source scripts/dev first, and see: maran check" >&2
      agent_did_not_run "cargo, which every stage of this command runs, is not on PATH."
    }
    command -v protoc >/dev/null 2>&1 || {
      echo "REFUSED: protoc is not on PATH — agent/crates/agent/build.rs generates the proto" >&2
      echo "         contract at compile time, so every stage would stop in that build script." >&2
      echo "         source scripts/dev first, and see: maran check" >&2
      agent_did_not_run "protoc, which the agent's build script runs, is not on PATH."
    }
  else
    command -v docker >/dev/null 2>&1 || {
      echo "REFUSED: this machine has no C linker AND no docker, so there is no path on which" >&2
      echo "         cargo could run — neither natively nor in $image." >&2
      echo "         Debian family: sudo apt install -y build-essential" >&2
      echo "         RHEL family:   sudo dnf groupinstall -y 'Development Tools'" >&2
      agent_did_not_run "neither a native nor a containerised cargo is available (named above)."
    }
  fi
}

# The command is validated before the toolchain is proved, so `maran agent bogus` still gets the
# usage message: a typo is not a refusal to measure.
case "$command_name" in
  build|test|lint|fmt|check) require_agent_toolchain ;;
  *) usage ;;
esac

case "$command_name" in
  build)
    probe_for_reading
    run_cargo build
    ;;
  test)
    probe_for_reading
    # --no-fail-fast, always. Without it cargo stops after the first target that fails, so the
    # run reports one failure and says nothing at all about the twenty-three targets it never
    # started — and the number a reader takes from it is smaller than the baseline for a reason
    # the output does not state. rules/testing.md is explicit: a verdict is named failures plus
    # a collected total, never an exit code, and a total that dropped is a failed run whatever
    # the exit code says.
    run_cargo test --workspace --no-fail-fast
    ;;
  lint)
    probe_for_reading
    run_cargo clippy --all-targets -- -D warnings
    ;;
  fmt)
    lock_for_writing fmt
    run_cargo fmt --all
    ;;
  check)
    # `check` runs `cargo fmt --all -- --check`, which writes nothing, so this reads rather
    # than locks.
    probe_for_reading
    # Every stage runs, then the failures are named together. This used to be four commands under
    # `set -e`, which meant a formatting nit ended the run before clippy, the tests or the doc
    # build had been asked anything — the developer fixed one line, ran again, and met the next
    # gate one round trip later. Worse, the verdict was the exit code of whichever stage stopped
    # first, so "it failed" never said how much had been measured (rules/testing.md "a verdict is
    # named failures plus a collected total").
    failed_stages=""
    run_stage() {
      stage_name="$1"
      shift
      if ! "$@"; then
        failed_stages="$failed_stages $stage_name"
      fi
    }

    run_stage fmt run_cargo fmt --all -- --check
    run_stage clippy run_cargo clippy --all-targets -- -D warnings
    run_stage test run_cargo test --workspace --no-fail-fast
    # The documentation build, with warnings denied, is a gate CI has always enforced and this
    # command did not run — so a public doc comment linking a private item passed every local
    # check and failed the pull request. A gate a developer cannot reproduce is a gate that
    # reports its findings in the most expensive place available.
    doc_stage() {
      RUSTDOCFLAGS="-D warnings" run_cargo doc --no-deps --workspace
    }
    run_stage doc doc_stage

    echo
    if [ -n "$failed_stages" ]; then
      echo "AGENT-CHECK FAILED — stages that failed:$failed_stages (of: fmt clippy test doc)"
      exit 1
    fi
    echo "AGENT-CHECK OK — fmt, clippy, test and doc all ran and all passed"
    ;;
  *)
    usage
    ;;
esac
