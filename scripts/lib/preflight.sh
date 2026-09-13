#!/usr/bin/env bash
# Verifies the developer toolchain for Maran. Exit 0 iff everything required is present.
set -euo pipefail

# Pick up user-local toolchains (dotnet 9, cargo, protoc) if installed; see scripts/dev.
# shellcheck disable=SC1091
[ -f "$(dirname "$0")/../dev" ] && . "$(dirname "$0")/../dev"

fail=0
need() { # name, command, version-args, minimum-major
  local name="$1" cmd="$2" args="$3" min="$4"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "MISSING  $name ($cmd)"; fail=1; return
  fi
  local ver major
  ver="$($cmd $args 2>/dev/null | head -1)"
  major="$(echo "$ver" | grep -oE '[0-9]+' | head -1)"
  if [ "${major:-0}" -lt "$min" ]; then
    echo "TOO OLD  $name: '$ver' (need >= $min)"; fail=1
  else
    echo "OK       $name: $ver"
  fi
}

need "dotnet SDK" dotnet "--version" 9
need "cargo"      cargo  "--version" 1
need "rustc"      rustc  "--version" 1
need "node"       node   "--version" 20
need "npm"        npm    "--version" 10
need "protoc"     protoc "--version" 3
need "docker"     docker "--version" 24

# Rust links through the system C toolchain, and several of the agent's dependencies build a C
# helper at compile time. Without a linker `cargo` reports "linker `cc` not found" and cannot build
# even a hello-world — a state this script used to call ready, because cargo itself was present.
#
# `cc` and NOT `cmake`. The workspace does pull the `cmake` build-helper crate (through
# aws-lc-sys, itself reached from object_store and rustls), which is what makes "install cmake"
# a believable thing to tell a developer — but aws-lc-sys picks its CcBuilder on Linux and only
# reaches for the cmake BINARY on a FIPS build, under AWS_LC_SYS_NO_ASM, under a sanitizer, or
# when AWS_LC_SYS_CMAKE_BUILDER is set explicitly. Measured: with no `cmake` on PATH at all,
# `cargo clean -p aws-lc-sys && cargo build -p aws-lc-sys` finishes in 5.9s and produces
# libaws_lc_0_45_0_crypto.a from 377 object files. So this script checks the compiler that is
# genuinely required and does not ask for a tool the build never runs.
if command -v cc >/dev/null 2>&1; then
  echo "OK       C linker: $(cc --version 2>/dev/null | head -1)"
else
  echo "MISSING  C linker (cc) — cargo cannot build; install with:"
  echo "         Debian family: sudo apt install -y build-essential"
  echo "         RHEL family:   sudo dnf groupinstall -y 'Development Tools'"
  fail=1
fi

exit "$fail"
