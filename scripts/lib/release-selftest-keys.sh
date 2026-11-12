#!/usr/bin/env bash
# Selftest outcome 12 of `maran release selftest`: a shipping build needs every pinned public key (issue #544).
#
# SOURCED by scripts/lib/release-bundle.sh, which has already set `-euo pipefail`. Only definitions here:
# nothing runs when this file is sourced.

# selftest_write_pinned_key: <file>. Writes a synthetic public key shaped like the real one (licence and lease
# keys are 64 hexadecimal characters, the others PEM blocks), never a real key.
selftest_write_pinned_key() {
  case "${1##*/}" in
    licence-verification.pub|lease-verification.pub) printf '%064x\n' "$RANDOM$RANDOM" > "$1" ;;
    *) printf -- '-----BEGIN PUBLIC KEY-----\nc3ludGhldGljIHNlbGZ0ZXN0IGtleSwgbm90IGEgcmVhbCBvbmU=\n-----END PUBLIC KEY-----\n' > "$1" ;;
  esac
}

# selftest_pinned_keys_outcome: outcome 12. Called by cmd_selftest and reads its locals (work, root).
# Runs the real PinnedKeys.targets (the file the Licensing project imports) from a throwaway probe project
# in the work directory, against a fixture directory of synthetic (not real) key files, driving the
# MaranShippingBuild property the release pipeline sets: a shipping build missing any one key must fail with
# MARAN001 naming the file, a shipping build with all five must pass, and a non-shipping Release build must
# not care (CI and the OpenAPI emit build Release without a full key set). The probe imports only that file
# and runs a Build whose BeforeBuild hook the file attaches to (BeforeTargets), so it needs no NuGet restore,
# no network and writes nothing into backend/. The names are held three ways: this pipeline's list
# (MARAN_PINNED_KEYS), the targets file's items and docs/operations/licensing.md must agree. The manifest
# writer is proved to list all five with their SHA-256 and to refuse a missing one. An EMPTY or placeholder
# key file, or a hex key with whitespace inside, is refused by the MSBuild target and by the shell writer
# alike, each against an accepting control (a PEM block, 64 hexadecimal characters).
selftest_pinned_keys_outcome() {
  echo
  echo "-- outcome 12: a shipping build needs every pinned public key --"
  require_tool dotnet "runs the Licensing project's pinned-key check"
  local targets="${root}/backend/src/Maran.Modules/Licensing/PinnedKeys.targets"
  local csproj="${root}/backend/src/Maran.Modules/Licensing/Maran.Modules.Licensing.csproj"
  local probe="${work}/pinned-keys-probe/probe.proj"
  local keys_dir="${work}/pinned-keys" key out shape
  local -a names=($MARAN_PINNED_KEYS)
  mkdir -p "$keys_dir" "${probe%/*}"
  # The probe: the same file the Licensing project imports, plus the two hook targets a Microsoft.Common build
  # provides. Importing first keeps the real BeforeTargets="BeforeBuild" attachment under test.
  cat > "$probe" <<PROBE
<Project DefaultTargets="Build">
  <Import Project="${targets}" />
  <Target Name="BeforeBuild" />
  <Target Name="Build" DependsOnTargets="BeforeBuild" />
</Project>
PROBE
  grep -q '<Import Project="PinnedKeys.targets" />' "$csproj" \
    || release_failed "selftest: the Licensing project does not import PinnedKeys.targets; the shipping gate is not part of its build."
  local listed doc
  listed="$(grep -o '<PinnedKey Include="[^"]*"' "$targets" | sed 's/.*Include="//; s/"$//' | sort | tr '\n' ' ')"
  [ "$listed" = "$(printf '%s\n' "${names[@]}" | sort | tr '\n' ' ')" ] \
    || release_failed "selftest: PinnedKeys.targets names [${listed}] but MARAN_PINNED_KEYS is [${names[*]}]; one list changed without the other."
  doc="${root}/docs/operations/licensing.md"
  for key in "${names[@]}"; do
    grep -qF "$key" "$doc" || release_failed "selftest: ${doc} does not mention the pinned key ${key}."
  done
  for key in "${names[@]}"; do
    selftest_write_pinned_key "${keys_dir}/${key}"
  done
  pinned_keys_target() { # <shipping: true|false> <configuration>
    dotnet msbuild "$probe" -nologo -p:MaranShippingBuild="$1" -p:Configuration="$2" -p:PinnedKeysDirectory="${keys_dir}/" 2>&1
  }
  # A toolchain that cannot even evaluate the probe is DID NOT RUN, never a pass and never a red check.
  if ! out="$(pinned_keys_target true Release)"; then
    if ! grep -q "MARAN001" <<<"$out"; then
      suite_did_not_run "$VERDICT_PREFIX" "dotnet msbuild could not build the pinned-key probe project (no restore is involved): $(tail -n 3 <<<"$out")"
      exit "$SUITE_STATUS_DID_NOT_RUN"
    fi
  fi
  pinned_keys_target true Release >/dev/null \
    || release_failed "selftest: a shipping build with all five pinned keys present was refused — the check is broken."
  echo "${VERDICT_PREFIX}: OK, a shipping build with all five pinned keys ACCEPTED (expected)."

  local manifest_part
  manifest_part="$(manifest_pinned_keys_field "$keys_dir")"
  for key in "${names[@]}"; do
    grep -q "\"${key}\": \"$(sha256_of "${keys_dir}/${key}")\"" <<<"$manifest_part" \
      || release_failed "selftest: the manifest's pinned_keys object does not list ${key} with its SHA-256."
  done
  echo "${VERDICT_PREFIX}: OK, the manifest's pinned_keys object lists all five keys with their SHA-256 (expected)."

  for key in "${names[@]}"; do
    rm "${keys_dir}/${key}"
    if out="$(pinned_keys_target true Release)"; then
      release_failed "selftest: a shipping build ACCEPTED a missing ${key}; the pinned-key check is decoration."
    elif ! grep -q "MARAN001.*${key}" <<<"$out"; then
      release_failed "selftest: a shipping build without ${key} failed for another reason than the pinned-key check: ${out}"
    fi
    pinned_keys_target false Release >/dev/null \
      || release_failed "selftest: a non-shipping Release build without ${key} was refused; CI and the OpenAPI emit build Release without a full key set."
    pinned_keys_target false Debug >/dev/null \
      || release_failed "selftest: a Debug build without ${key} was refused; Debug and test builds must not need production keys."
    if (manifest_pinned_keys_field "$keys_dir") >/dev/null 2>&1; then
      release_failed "selftest: the manifest writer accepted a missing ${key}."
    fi
    selftest_write_pinned_key "${keys_dir}/${key}"
  done
  echo "${VERDICT_PREFIX}: OK, a shipping build missing any one pinned key REFUSED naming the file; non-shipping Release and Debug accepted (expected)."

  # An empty file, or text that is not a key, is "present" to a bare existence test and would be pinned.
  for key in "${names[@]}"; do
    for shape in empty placeholder split-hex; do
      case "$shape" in
        empty) : > "${keys_dir}/${key}" ;;
        placeholder) echo "TODO put the real key here" > "${keys_dir}/${key}" ;;
        split-hex) printf '%032x\n%032x\n' "$RANDOM" "$RANDOM" > "${keys_dir}/${key}" ;;
      esac
      if out="$(pinned_keys_target true Release)"; then
        release_failed "selftest: a shipping build ACCEPTED a ${shape} ${key}; the pinned-key check only tests existence."
      elif ! grep -q "MARAN001.*${key}" <<<"$out"; then
        release_failed "selftest: a shipping build with a ${shape} ${key} failed for another reason than the pinned-key check: ${out}"
      fi
      if (pinned_keys_lines "$keys_dir") >/dev/null 2>&1; then
        release_failed "selftest: the manifest writer pinned a ${shape} ${key}."
      fi
    done
    selftest_write_pinned_key "${keys_dir}/${key}"
  done
  # The accepting controls: both shapes, so neither refusal above is a check that refuses everything.
  pinned_keys_target true Release >/dev/null \
    || release_failed "selftest: a shipping build with a PEM block and 64-hex keys was refused after the empty-key cases."
  pinned_keys_lines "$keys_dir" >/dev/null \
    || release_failed "selftest: the manifest writer refused well-formed keys after the empty-key cases."
  echo "${VERDICT_PREFIX}: OK, an empty, placeholder or whitespace-split pinned key REFUSED by the MSBuild target and the manifest writer; PEM and 64-hex keys accepted (expected)."

  # The wiring (BeforeTargets) is proved by every call above: the probe runs Build, never the target by name.
  echo "${VERDICT_PREFIX}: OK, the shipping gate runs as part of Build through the very file the Licensing project imports (expected)."
}
