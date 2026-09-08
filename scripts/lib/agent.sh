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

# lock_for_writing / probe_for_reading: this command's two relationships to the tree lock
# (scripts/lib/tree-lock.sh). `fmt` REWRITES every Rust source file in place, so it is a writer and
# takes the exclusive lock — a formatter running over a planted mutant produces a verdict about
# source that existed for a moment, and the mutation harness's byte-exact restore then discards
# whatever the formatter wrote to the rest of that file. Everything else here only reads, so it
# probes: a clippy finding or a red test that belongs to somebody else's experiment is the most
# believable false regression this repository produces.
lock_for_writing() {
  tree_lock_acquire "$root" 0 "agent $1" || exit 1
  trap tree_lock_release EXIT INT TERM
  tree_lock_refuse_pending "$root" readonly || exit 1
}

probe_for_reading() {
  tree_lock_probe "$root" || exit 1
  tree_lock_refuse_pending "$root" readonly || exit 1
}

rust_version="$(rustc --version 2>/dev/null | awk '{print $2}')"
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
