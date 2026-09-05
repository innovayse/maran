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

  echo "no C linker on this machine — using $image (install build-essential to build natively)"
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -v "$root:/repo" \
    -w /repo/agent \
    -v maran-cargo-cache:/cargo \
    -e CARGO_HOME=/cargo \
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
    run_cargo build
    ;;
  test)
    run_cargo test
    ;;
  lint)
    run_cargo clippy --all-targets -- -D warnings
    ;;
  fmt)
    run_cargo fmt --all
    ;;
  check)
    run_cargo fmt --all -- --check
    run_cargo clippy --all-targets -- -D warnings
    run_cargo test
    # The documentation build, with warnings denied, is a gate CI has always enforced and this
    # command did not run — so a public doc comment linking a private item passed every local
    # check and failed the pull request. A gate a developer cannot reproduce is a gate that
    # reports its findings in the most expensive place available.
    RUSTDOCFLAGS="-D warnings" run_cargo doc --no-deps --workspace
    ;;
  *)
    usage
    ;;
esac
