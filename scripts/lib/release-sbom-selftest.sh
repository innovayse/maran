#!/usr/bin/env bash
# Selftest for `maran release sbom` (issue #154): `maran release sbom-selftest`.
#
# SOURCED by scripts/lib/release-bundle.sh after scripts/lib/release-sbom.sh. It proves the VERIFY
# half: release_sbom_verify_dir, which both `--verify` and the end of generation run. It plants a
# minimal CycloneDX document per component and drives that function directly.
#
# Every refusal is paired with the inverse control rules/testing.md asks for. Case 0 plants all three
# documents exactly at their floor and requires ACCEPTANCE, so a verifier mutated to refuse
# everything fails here and not only in a pipeline. Each refusal then changes ONE file of that same
# accepted directory and must name that file, so it is refused for the reason under test and not for
# a typo in the fixture, and the two components left untouched must still be reported as accepted.
#
# UNOBSERVED HERE: the generator tools themselves are not run. The documents are synthetic; this
# proves the acceptance rules, not that dotnet-CycloneDX, cargo-cyclonedx or cyclonedx-npm produce
# a document that clears them. The first release run is where that is observed.

# sbom_plant_py: the fixture writer, run with `python3 -I`. Argv: <file> <count> <bomFormat>
# <specVersion or -> <shape named|nameless>. `-` omits specVersion; `nameless` writes entries with
# no name, which must not count toward the floor.
sbom_plant_py() {
  cat <<'PY'
import json, sys

path, count, bom_format, spec, shape = sys.argv[1], int(sys.argv[2]), sys.argv[3], sys.argv[4], sys.argv[5]
doc = {"bomFormat": bom_format, "components": []}
if spec != "-":
    doc["specVersion"] = spec
for index in range(count):
    entry = {"type": "library", "version": "1.0.0"}
    if shape == "named":
        entry["name"] = "selftest-package-%d" % index
    doc["components"].append(entry)
with open(path, "w", encoding="utf-8") as handle:
    json.dump(doc, handle)
PY
}

# sbom_plant: <dir> <component> <count> [bomFormat] [specVersion or -] [named|nameless]. Writes
# `<dir>/<component>.cdx.json` as a synthetic document with that many components.
sbom_plant() {
  python3 -I -c "$(sbom_plant_py)" "$1/$2.cdx.json" "$3" "${4:-CycloneDX}" "${5:-1.5}" "${6:-named}"
}

# sbom_plant_good: <dir>. All three components, each exactly at its floor, so the accepted control
# also proves the boundary: a floor that was off by one would refuse it.
sbom_plant_good() {
  local component
  install -d -- "$1"
  for component in $(sbom_components); do
    sbom_plant "$1" "$component" "$(sbom_field "$component" floor)"
  done
}

# sbom_must_refuse: <label> <component that must be named> <reason pattern> <dir> [components...].
# Runs release_sbom_verify_dir in a subshell and requires a non-zero status whose output names the
# component and matches the reason, and requires that every OTHER component was still reported OK.
sbom_must_refuse() {
  local label="$1" named="$2" pattern="$3" dir="$4" output other
  shift 4
  if output="$( ( release_sbom_verify_dir "$dir" "$@" ) 2>&1 )"; then
    release_failed "sbom selftest: ${label} was ACCEPTED — the check behind it is decoration."
  fi
  printf '%s' "$output" | grep -qE -- "sbom: ${named}\.cdx\.json .*(${pattern})" \
    || release_failed "sbom selftest: ${label} was refused for another reason than expected (wanted ${named} /${pattern}/): ${output}"
  for other in $(sbom_components); do
    [ "$other" = "$named" ] && continue
    printf '%s' "$output" | grep -qF "sbom: ${other}.cdx.json OK" \
      || release_failed "sbom selftest: ${label} hid the untouched ${other} document, so a bad file masks a good one: ${output}"
  done
  echo "ok: ${label} REFUSED (expected)."
}

# sbom_must_refuse_raw: <label> <reason pattern> <dir> [components...]. Like sbom_must_refuse for
# refusals that name no component file (an unknown component, a path that is no directory).
sbom_must_refuse_raw() {
  local label="$1" pattern="$2" output
  shift 2
  if output="$( ( release_sbom_verify_dir "$@" ) 2>&1 )"; then
    release_failed "sbom selftest: ${label} was ACCEPTED — the check behind it is decoration."
  fi
  printf '%s' "$output" | grep -qE -- "$pattern" \
    || release_failed "sbom selftest: ${label} was refused for another reason than expected (wanted /${pattern}/): ${output}"
  echo "ok: ${label} REFUSED (expected)."
}

# release_sbom_selftest: see the file header.
release_sbom_selftest() {
  require_tool python3 "plants and reads the synthetic documents"
  local work good case_dir floor stubs output
  work="$(mktemp -d)"
  trap 'rm -rf -- "$work"' RETURN
  good="${work}/good"

  echo "-- case 0 (inverse control): three well-formed documents at their floors are ACCEPTED --"
  sbom_plant_good "$good"
  release_sbom_verify_dir "$good" >/dev/null \
    || release_failed "sbom selftest: well-formed documents at their floors were refused — the verifier refuses everything, or the fixture is broken."
  echo "ok: well-formed documents at their floors ACCEPTED (control)."

  echo "-- case 1: an empty components array --"
  case_dir="${work}/empty"; cp -r -- "$good" "$case_dir"
  sbom_plant "$case_dir" agent 0
  sbom_must_refuse "an empty components array" agent "below the floor of $(sbom_field agent floor)" "$case_dir"

  echo "-- case 2: a wrong bomFormat --"
  case_dir="${work}/format"; cp -r -- "$good" "$case_dir"
  sbom_plant "$case_dir" api "$(sbom_field api floor)" SPDX
  sbom_must_refuse "a wrong bomFormat" api 'bomFormat' "$case_dir"

  echo "-- case 3: not JSON --"
  case_dir="${work}/notjson"; cp -r -- "$good" "$case_dir"
  echo 'this is not json {' > "${case_dir}/frontend.cdx.json"
  sbom_must_refuse "a file that is not JSON" frontend 'not readable JSON' "$case_dir"

  echo "-- case 4: a component file missing --"
  case_dir="${work}/missing"; cp -r -- "$good" "$case_dir"
  rm -f -- "${case_dir}/agent.cdx.json"
  sbom_must_refuse "a missing component file" agent 'is missing from' "$case_dir"

  echo "-- case 5: one component below its floor --"
  case_dir="${work}/below"; cp -r -- "$good" "$case_dir"
  floor="$(sbom_field frontend floor)"
  sbom_plant "$case_dir" frontend "$((floor - 1))"
  sbom_must_refuse "a count one below the floor" frontend "holds $((floor - 1)) named components" "$case_dir"

  echo "-- case 6: no specVersion --"
  case_dir="${work}/nospec"; cp -r -- "$good" "$case_dir"
  sbom_plant "$case_dir" api "$(sbom_field api floor)" CycloneDX -
  sbom_must_refuse "a document with no specVersion" api 'specVersion' "$case_dir"

  echo "-- case 7: a specVersion older than 1.5 --"
  case_dir="${work}/oldspec"; cp -r -- "$good" "$case_dir"
  sbom_plant "$case_dir" api "$(sbom_field api floor)" CycloneDX 1.4
  sbom_must_refuse "specVersion 1.4" api 'older than 1.5' "$case_dir"

  echo "-- case 8: a count that is met only by components with no name --"
  case_dir="${work}/nameless"; cp -r -- "$good" "$case_dir"
  sbom_plant "$case_dir" agent "$(sbom_field agent floor)" CycloneDX 1.5 nameless
  sbom_must_refuse "components with no name" agent 'holds 0 named components' "$case_dir"

  echo "-- case 9: a subset check reads only what it was asked for --"
  case_dir="${work}/subset"; cp -r -- "$good" "$case_dir"
  rm -f -- "${case_dir}/agent.cdx.json"
  release_sbom_verify_dir "$case_dir" api frontend >/dev/null \
    || release_failed "sbom selftest: verifying only api and frontend failed because agent is absent, so --component cannot work."
  echo "ok: verifying api and frontend ACCEPTED with agent absent (expected)."

  echo "-- case 10: an unknown component and a non-directory --"
  sbom_must_refuse_raw "an unknown component" 'not a component this table knows' "$good" nonesuch
  sbom_must_refuse_raw "a path that is not a directory" 'is not a directory' "${good}/api.cdx.json"
  release_sbom_verify_dir "$good" api >/dev/null \
    || release_failed "sbom selftest: the same directory and a known component were refused, so the two refusals above prove nothing."
  echo "ok: the same directory with a known component ACCEPTED (control)."

  echo "-- case 11: a missing generator is DID NOT RUN, a present one is not --"
  stubs="${work}/stubs"; install -d -- "$stubs"
  printf '#!/bin/sh\necho "dotnet-CycloneDX stub %s"\n' "$(sbom_field api version)" > "${stubs}/dotnet-CycloneDX"
  chmod +x "${stubs}/dotnet-CycloneDX"
  # A PATH holding only the utilities the table lookup needs, and no generator.
  install -d -- "${work}/bare"
  for tool in awk cat grep; do ln -s -- "$(command -v "$tool")" "${work}/bare/${tool}"; done
  if output="$( ( PATH="${work}/bare"; sbom_require_generator api ) 2>&1 )"; then
    release_failed "sbom selftest: a missing generator was accepted."
  fi
  printf '%s' "$output" | grep -q 'DID NOT RUN' && printf '%s' "$output" | grep -qF "$(sbom_field api install)" \
    || release_failed "sbom selftest: the missing-generator refusal did not say DID NOT RUN with the install command: ${output}"
  echo "ok: a missing generator REFUSED with DID NOT RUN and its install command (expected)."
  ( PATH="${stubs}:${PATH}"; sbom_require_generator api ) >/dev/null 2>&1 \
    || release_failed "sbom selftest: a stub generator of the pinned version was refused."
  echo "ok: a stub generator of the pinned version ACCEPTED (control)."
  printf '#!/bin/sh\necho "dotnet-CycloneDX stub 0.0.1"\n' > "${stubs}/dotnet-CycloneDX"
  if ( PATH="${stubs}:${PATH}"; sbom_require_generator api ) >/dev/null 2>&1; then
    release_failed "sbom selftest: a generator of the wrong version was accepted."
  fi
  echo "ok: a generator of another version REFUSED (expected)."

  echo "UNOBSERVED HERE: the generator tools themselves are not run"
  release_ok "sbom selftest: acceptance control passed and every refusal named its own cause"
}
