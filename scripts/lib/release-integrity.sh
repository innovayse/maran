#!/usr/bin/env bash
# The signed code-integrity hash list of a release bundle: its per-component floor and its writer.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

# integrity_manifest_min_files: the vacuity floor of ONE component's staged directory in
# integrity-manifest.json (docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 1;
# issue #473). An empty or near-empty hash list would sign successfully, verify successfully, and
# report Clean forever on every installed host — the single most dangerous failure mode this
# feature has, because it looks identical to health.
#
# PER COMPONENT, never a total. The API is published self-contained and stages hundreds of files by
# itself, so a floor summed across components is cleared by the API alone while the frontend or the
# agent contributes nothing: the signed list then names no file of that component, a planted asset
# can never differ from it, and the closed comparison reports Clean indefinitely. Each component
# must be present and must meet its own minimum, in every architecture directory staged:
#   api       10 — a self-contained .NET publish is hundreds; 10 is "not an empty or stub publish"
#   frontend  10 — the built SPA is dozens of assets plus index.html
#   agent      1 — the single static binary `maran-agent` (build_agent); more would be invented
# A fixed number per component rather than a percentage of the previous release: this script has no
# access to "the previous release" at build time (open question left to the owner, per the plan).
integrity_manifest_min_files() {
  case "$1" in
    api) echo 10 ;;
    frontend) echo 10 ;;
    agent) echo 1 ;;
    *) release_failed "integrity_manifest_min_files: '$1' is not a release component (${MARAN_COMPONENTS})" ;;
  esac
}

# write_integrity_manifest: the code-integrity hash list, a SEPARATE artefact from
# manifest.json (docs/superpowers/specs/2026-09-19-maran-code-integrity.md §3) — one
# sha256 per installed FILE rather than per component archive, read continuously after
# install by the closed PluginLoader, never by this installer's own transport check.
# Walks the SAME staged output directories build_api/build_agent/build_frontend already
# populate and tar_component already tars — never the installed filesystem (§4: this is a
# positive list of what a release CONTAINS, not a live filesystem scan) — and keys each
# entry by "<component>/<path relative to that component's staged root>", matching
# the installer's own unpack targets (releases/<version>/<component>, made by
# cli_layout_install_release), so api/, agent/, frontend/ entries cannot collide.
write_integrity_manifest() {
  local out_dir="$1" version="$2"
  local manifest="${out_dir}/integrity-manifest.json"
  local tmp_entries
  tmp_entries="$(mktemp)"

  # The staged directories are DISCOVERED rather than assumed, because there are now two shapes:
  # a real build stages `api-x86_64/`, `agent-aarch64/` and so on (one set per architecture, issue
  # #53), while the selftest stages plain `api/`, `agent/`, `frontend/`. Walking what is present
  # keeps one writer for both instead of a second copy for the second shape. Each entry is prefixed
  # with the directory's OWN name, so a two-architecture bundle's hashes cannot collide.
  local comp_dirs=() d
  for d in "${out_dir}"/api "${out_dir}"/api-* \
           "${out_dir}"/agent "${out_dir}"/agent-* \
           "${out_dir}"/frontend "${out_dir}"/frontend-*; do
    [ -d "$d" ] && comp_dirs+=("$d")
  done
  [ "${#comp_dirs[@]}" -gt 0 ] \
    || release_failed "write_integrity_manifest: no staged component directories under ${out_dir} — the build functions must run first"

  # The per-component floor, checked BEFORE anything is written. Three things must hold, and each
  # is on the axis that can go blind (rules/testing.md): every component is present at all; every
  # component is staged for the SAME set of architectures (a missing `frontend-aarch64` is a
  # frontend that is unlisted on every ARM64 host); and every staged directory holds at least its
  # component's minimum number of files.
  local component comp_dir suffix arch_set="" first_set=1 seen_set min found
  for component in $MARAN_COMPONENTS; do
    seen_set=""
    for comp_dir in "${comp_dirs[@]}"; do
      d="$(basename "$comp_dir")"
      case "$d" in
        "$component") suffix="" ;;
        "$component"-*) suffix="${d#"$component"-}" ;;
        *) continue ;;
      esac
      seen_set="${seen_set} ${suffix:-_}"
      min="$(integrity_manifest_min_files "$component")"
      found="$(find "$comp_dir" -type f | wc -l)"
      if [ "$found" -lt "$min" ]; then
        release_failed "write_integrity_manifest: ${d} holds ${found} file(s), below the ${component} floor of ${min} — refusing to sign a hash list that leaves ${component} (almost) unlisted (it would report 'clean' forever)."
      fi
    done
    [ -n "$seen_set" ] \
      || release_failed "write_integrity_manifest: component '${component}' has no staged directory under ${out_dir} — the signed list would name none of its files and any change to it would read as clean."
    seen_set="$(printf '%s\n' $seen_set | sort -u | tr '\n' ' ')"
    if [ "$first_set" -eq 1 ]; then
      arch_set="$seen_set"; first_set=0
    elif [ "$seen_set" != "$arch_set" ]; then
      release_failed "write_integrity_manifest: '${component}' is staged for [${seen_set}] but the other components for [${arch_set}] — every component must be staged for the same architectures."
    fi
  done

  local file rel_path sha
  for comp_dir in "${comp_dirs[@]}"; do
    component="$(basename "$comp_dir")"
    while IFS= read -r -d '' file; do
      rel_path="${file#"${comp_dir}"/}"
      # The path is written into a JSON string without escaping, so one that could close the string
      # or forge a key is refused instead of signed.
      case "$rel_path" in
        *[\"\\]*|*[[:cntrl:]]*)
          rm -f -- "$tmp_entries"
          release_failed "write_integrity_manifest: ${component}/${rel_path} contains a quote, backslash or control character — refusing to write it into the signed JSON."
          ;;
      esac
      sha="$(sha256_of "$file")"
      printf '%s\0%s\0' "${component}/${rel_path}" "$sha" >> "$tmp_entries"
    done < <(find "$comp_dir" -type f -print0)
  done

  local count=0
  {
    echo "{"
    echo "  \"version\": \"${version}\","
    echo "  \"files\": {"
    local first=1 key val
    while IFS= read -r -d '' key && IFS= read -r -d '' val; do
      [ "$first" -eq 1 ] || echo ","
      first=0
      count=$((count + 1))
      printf '    "%s": "%s"' "$key" "$val"
    done < "$tmp_entries"
    echo
    echo "  }"
    echo "}"
  } > "$manifest"
  rm -f "$tmp_entries"

  echo "Wrote ${manifest} (${count} files, per-component floors met for: ${MARAN_COMPONENTS})."
}
