#!/usr/bin/env bash
# CycloneDX SBOMs for `maran release sbom`: one `<component>.cdx.json` per shipped component.
#
# SOURCED by scripts/lib/release-bundle.sh, which supplies release_failed, release_ok, require_tool,
# sha256_of and VERDICT_PREFIX, and dispatches `sbom` to release_sbom_main. Its selftest is
# scripts/lib/release-sbom-selftest.sh (`maran release sbom-selftest`).
#
#   maran release sbom --print-tools                     the pinned generators, one row each
#   maran release sbom --out DIR [--component C|all]     generate, then verify what was written
#   maran release sbom --verify DIR                      verify an existing directory only
#
# WHAT EACH SBOM DESCRIBES (issue #154, threat note item 4). An SBOM is evidence about dependencies,
# not a signature over them: a compromised generator can emit a wrong one, and everything in it can
# be re-derived from the lockfiles by a reviewer.
#   api       backend/src/Maran.Host: the NuGet closure of the project that is published into
#             api.tar.gz, not of the test projects. For SDK-style projects its restored
#             `obj/project.assets.json` already holds the whole closure of the referenced modules (the
#             generator says so itself, and measured: the same 145 packages without `--recursive`),
#             and `--disable-package-restore` makes it read that file: the release build has restored
#             it already, and a missing restore fails the generator loudly instead of reaching for
#             the network. (The tool may still look up package metadata in the local NuGet cache.)
#   agent     the shipped `maran-agent` BINARY, from Cargo.lock, via `--describe binaries` on the
#             agent crate. ONE merged document, not one per crate: the four other workspace crates are
#             linked into that one binary and are listed in it as components, so a per-crate SBOM would
#             describe libraries nobody ships separately and leave the reader to merge them. `--target
#             all` lists dependencies for every platform in the lock, a superset of what either
#             architecture links, which is the safe direction for an inventory.
#   frontend  the production dependencies of the SPA from frontend/package-lock.json
#             (`--package-lock-only --omit dev`), so no node_modules directory is consulted.
#
# NOT REPRODUCIBLE. Every document carries a timestamp and a random serial number, so two runs are
# never byte-identical and nothing here claims they are. Only the component list is stable.
#
# NEVER INSTALLS ANYTHING, and never degrades. A missing or wrongly versioned generator ends the run
# with `DID NOT RUN` naming the tool and its install command (rules/testing.md: a toolchain gap is a
# refusal, never a verdict about source it did not read). No fallback writes an empty or partial SBOM.

# The ONE table of generators. Fields, `|`-separated:
#   component | tool | exact version | component floor | version probe | install command
# Versions were looked up on 2026-11-04 against the registries (nuget flat container index for
# `cyclonedx`, crates.io API for `cargo-cyclonedx`, registry.npmjs.org dist-tags for
# `@cyclonedx/cyclonedx-npm`) and each was run against this repository.
#
# THE FLOOR is the vacuity guard, on the axis that can go blind: the number of NAMED components in the
# document. It sits near half of what the generator measured here, so an empty or stub document
# cannot pass while an honest shrinking of the dependency graph can. Measured 2026-11-04:
#   api       145 package libraries in backend/src/Maran.Host/obj/project.assets.json
#             (python3 -I, counting libraries of type "package"); the generator emitted the same 145
#                                                                                      -> floor 70
#   agent     264 [[package]] entries in agent/Cargo.lock (grep -c '^name = ' agent/Cargo.lock); the
#             generator emitted 263 (every entry but maran-agent itself)               -> floor 130
#   frontend  107 non-dev entries in frontend/package-lock.json (python3 -I over "packages");
#             the generator emitted 92 after merging duplicates                        -> floor 50
# What a floor cannot see: a document missing a few real dependencies. It proves the generator read
# something of the right size, not that it read all of it.
sbom_table() {
  cat <<'TABLE'
api|CycloneDX|6.3.1|70|dotnet-CycloneDX --version|dotnet tool install --global CycloneDX --version 6.3.1
agent|cargo-cyclonedx|0.5.9|130|cargo cyclonedx --version|cargo install cargo-cyclonedx --version =0.5.9 --locked
frontend|@cyclonedx/cyclonedx-npm|6.0.1|50|cyclonedx-npm --version|npm install --global @cyclonedx/cyclonedx-npm@6.0.1
TABLE
}

# sbom_field: <component> <tool|version|floor|probe|install>. One field of the component's table row,
# or nothing for an unknown component (callers check the component first).
sbom_field() {
  local column
  case "$2" in
    tool) column=2 ;; version) column=3 ;; floor) column=4 ;; probe) column=5 ;; install) column=6 ;;
    *) return 1 ;;
  esac
  sbom_table | awk -F'|' -v c="$1" -v n="$column" '$1 == c { print $n }'
}

# sbom_components: every component name in the table, one per line, in table order.
sbom_components() {
  sbom_table | awk -F'|' '{ print $1 }'
}

# sbom_extend_path: puts the two directories the install commands write to on PATH when they exist.
# `dotnet tool install --global` and `cargo install` both put the executable somewhere a fresh CI
# shell has not been told about, and the version probe would otherwise report a tool that was just
# installed as missing. Blind spot: a tool installed anywhere else must already be on PATH.
sbom_extend_path() {
  local dir
  for dir in "${HOME:-}/.dotnet/tools" "${HOME:-}/.cargo/bin"; do
    [ -d "$dir" ] || continue
    case ":${PATH}:" in *":${dir}:"*) ;; *) PATH="${dir}:${PATH}" ;; esac
  done
  export PATH
}

# sbom_require_generator: <component>. Refuses (DID NOT RUN, status 2) unless the pinned generator
# answers its version probe with the pinned version as a whole word. The probe runs the real tool, so
# a cargo with no `cyclonedx` subcommand is "missing" and not mistaken for present because `cargo`
# is. Blind spot: a version string is read from the tool's own output, not from its binary.
sbom_require_generator() {
  local component="$1" tool version probe install out
  local -a words
  tool="$(sbom_field "$component" tool)"
  version="$(sbom_field "$component" version)"
  probe="$(sbom_field "$component" probe)"
  install="$(sbom_field "$component" install)"
  read -r -a words <<< "$probe"
  if ! out="$("${words[@]}" 2>&1)"; then
    suite_did_not_run "$VERDICT_PREFIX" "${tool} ${version} (the ${component} SBOM generator) is not installed; install it with: ${install}"
    exit "$SUITE_STATUS_DID_NOT_RUN"
  fi
  grep -qFw -- "$version" <<< "$out" || {
    suite_did_not_run "$VERDICT_PREFIX" "${tool} is installed but is not the pinned ${version} (it answered: ${out%%$'\n'*}); install the pin with: ${install}"
    exit "$SUITE_STATUS_DID_NOT_RUN"
  }
}

# sbom_check_py: the document check, as Python run with `python3 -I` (isolated: no cwd or user-site
# import). Argv: <file> <floor>. Prints the named-component count on success; on refusal prints one
# reason to stderr and exits 1. It reads the file as untrusted data. A component counts only when it
# is an object with a non-empty string `name`, so a document padded with empty objects does not clear
# the floor. Blind spot: it does not validate the document against the CycloneDX schema, only the
# four facts the release depends on.
sbom_check_py() {
  cat <<'PY'
import json, re, sys

path, floor = sys.argv[1], int(sys.argv[2])


def refuse(reason):
    print(reason, file=sys.stderr)
    sys.exit(1)


try:
    with open(path, "rb") as handle:
        doc = json.loads(handle.read().decode("utf-8"))
except (OSError, ValueError, RecursionError) as error:
    refuse("is not readable JSON (%s)" % type(error).__name__)
if not isinstance(doc, dict):
    refuse("is JSON but not an object at the top level")
if doc.get("bomFormat") != "CycloneDX":
    refuse("has bomFormat %r, not \"CycloneDX\"" % (doc.get("bomFormat"),))
spec = doc.get("specVersion")
if not isinstance(spec, str) or not re.fullmatch(r"[0-9]+\.[0-9]+", spec):
    refuse("has no usable specVersion (got %r)" % (spec,))
if tuple(int(part) for part in spec.split(".")) < (1, 5):
    refuse("has specVersion %s, older than 1.5" % spec)
components = doc.get("components")
if not isinstance(components, list):
    refuse("has no components array")
named = sum(1 for c in components if isinstance(c, dict) and isinstance(c.get("name"), str) and c["name"])
if named < floor:
    refuse("holds %d named components, below the floor of %d" % (named, floor))
print(named)
PY
}

# release_sbom_verify_dir: <dir> [component...]. Checks `<component>.cdx.json` for each component
# named (all three when none is) and returns 1 after naming EVERY problem found, so one bad file does
# not hide another. This is the single verification: `--verify` runs it, and generation runs it over
# what it just wrote. Returns 0 only when every file exists, parses and clears its floor.
release_sbom_verify_dir() {
  local dir="$1" component file floor result failed=0
  shift
  [ -d "$dir" ] || { echo "sbom: ${dir} is not a directory" >&2; return 1; }
  if [ $# -eq 0 ]; then
    local -a every
    read -r -a every <<< "$(sbom_components | tr '\n' ' ')"
    set -- "${every[@]}"
  fi
  for component in "$@"; do
    file="${dir}/${component}.cdx.json"
    floor="$(sbom_field "$component" floor)"
    if [ -z "$floor" ]; then
      echo "sbom: ${component} is not a component this table knows" >&2; failed=1; continue
    fi
    if [ ! -f "$file" ]; then
      echo "sbom: ${component}.cdx.json is missing from ${dir}" >&2; failed=1; continue
    fi
    if ! result="$(python3 -I -c "$(sbom_check_py)" "$file" "$floor" 2>&1)"; then
      echo "sbom: ${component}.cdx.json ${result}" >&2; failed=1; continue
    fi
    echo "sbom: ${component}.cdx.json OK, ${result} named components (floor ${floor})"
  done
  return "$failed"
}

# sbom_generate_api: <output file>. The Host's NuGet closure, from its restored assets.
sbom_generate_api() {
  local dir
  dir="$(dirname "$1")"
  dotnet-CycloneDX "$root/backend/src/Maran.Host/Maran.Host.csproj" \
    --disable-package-restore --output-format Json --spec-version 1.5 \
    --output "$dir" --filename "$(basename "$1")" || return 1
}

# NETWORK, stated exactly: cargo-cyclonedx runs `cargo metadata` and offers neither `--locked` nor
# `--offline`, so on a runner whose cargo cache lacks the crates of Cargo.lock it MAY download them
# (and refresh the registry index) into the cargo cache. What is held true is the result, not the
# silence: the SBOM is taken from the committed graph because Cargo.lock is compared byte for byte
# before and after, and a changed lock fails the run. Nothing is claimed about the cache.
#
# sbom_generate_agent: <output file>. cargo-cyclonedx writes beside the crate's Cargo.toml and offers
# no output directory, so any stale file is removed first (a failed run must not leave an old one to
# be picked up), the fresh one is moved out of the source tree, and Cargo.lock must be byte-identical
# afterwards: a `cargo metadata` that rewrote the lock would mean the SBOM describes a graph that is
# not the committed one.
sbom_generate_agent() {
  local produced="$root/agent/crates/agent/maran-agent_bin.cdx.json" lock="$root/agent/Cargo.lock" before
  rm -f -- "$produced"
  before="$(sha256_of "$lock")"
  cargo cyclonedx --manifest-path "$root/agent/crates/agent/Cargo.toml" \
    --format json --spec-version 1.5 --describe binaries --target all --quiet || return 1
  [ -f "$produced" ] || { echo "sbom: cargo-cyclonedx wrote no ${produced##*/}" >&2; return 1; }
  mv -- "$produced" "$1" || return 1
  [ "$(sha256_of "$lock")" = "$before" ] \
    || { echo "sbom: agent/Cargo.lock changed while the SBOM was generated, so it does not describe the committed graph" >&2; return 1; }
}

# sbom_generate_frontend: <output file>. Lockfile only; no node_modules is read.
sbom_generate_frontend() {
  cyclonedx-npm --package-lock-only --omit dev --spec-version 1.5 --of JSON \
    --output-file "$1" "$root/frontend/package.json" || return 1
}

# sbom_generate_all: <out dir> <component...>. Generates into a private temporary directory and moves
# each document into place only after its generator succeeded, so DIR never holds a half-written
# file (the release workflow attaches every file it finds there). Runs in a subshell so the EXIT trap
# removes the temporary directory on every path, including release_failed's exit.
sbom_generate_all() {
  local out="$1" component work
  shift
  install -d -- "$out"
  (
    work="$(mktemp -d)"
    trap 'rm -rf -- "$work"' EXIT
    for component in "$@"; do
      rm -f -- "${out}/${component}.cdx.json"
      echo "sbom: generating ${component} with $(sbom_field "$component" tool) $(sbom_field "$component" version)"
      "sbom_generate_${component}" "${work}/${component}.cdx.json" \
        || release_failed "the ${component} SBOM generator failed (its output is above); nothing was written for ${component}"
      mv -- "${work}/${component}.cdx.json" "${out}/${component}.cdx.json"
    done
  )
}

# release_sbom_main: see the file header. Argument errors and refusals end the script; success prints
# the verdict line the workflow and the selftest look for.
release_sbom_main() {
  local out="" verify="" component_arg="all" print_tools=0 component
  local -a components=()
  while [ $# -gt 0 ]; do
    case "$1" in
      --out) out="${2:?--out requires a value}"; shift 2 ;;
      --verify) verify="${2:?--verify requires a value}"; shift 2 ;;
      --component) component_arg="${2:?--component requires a value}"; shift 2 ;;
      --print-tools) print_tools=1; shift ;;
      *) echo "release sbom: unknown argument: $1" >&2; usage ;;
    esac
  done

  if [ "$print_tools" -eq 1 ]; then
    sbom_table | awk -F'|' '{ print $1, $2, $3, $6 }'
    return 0
  fi

  require_tool python3 "verifies every SBOM"
  if [ -n "$verify" ]; then
    release_sbom_verify_dir "$verify" || release_failed "the SBOMs in ${verify} did not verify (reason printed above)"
    release_ok "every SBOM in ${verify} verified"
    return 0
  fi

  [ -n "$out" ] || { echo "release sbom: --out DIR is required (or --print-tools, or --verify DIR)" >&2; usage; }
  if [ "$component_arg" = "all" ]; then
    read -r -a components <<< "$(sbom_components | tr '\n' ' ')"
  else
    [ -n "$(sbom_field "$component_arg" floor)" ] \
      || release_failed "unknown component '${component_arg}'; choose one of: $(sbom_components | tr '\n' ' ')or all"
    components=("$component_arg")
  fi

  sbom_extend_path
  for component in "${components[@]}"; do
    sbom_require_generator "$component"
  done
  sbom_generate_all "$out" "${components[@]}" || exit "$?"
  release_sbom_verify_dir "$out" "${components[@]}" \
    || release_failed "a generated SBOM in ${out} did not verify (reason printed above)"
  release_ok "${#components[@]} SBOM(s) written to ${out} and verified: ${components[*]}"
}
