#!/usr/bin/env bash
# Selftest outcome 13 of `maran release selftest`: the commands the release workflow runs are wired to the
# publish gates (issues #544 and #552).
#
# SOURCED by scripts/lib/release-bundle.sh, which has already set `-euo pipefail`. Only definitions here:
# nothing runs when this file is sourced.
#
# The functions under test (require_pinned_keys, openapi_sign, openapi_verify, build_api's flag) are each
# proved on their own elsewhere. What a deleted CALL would leave green is the wiring, so this outcome drives
# the commands end to end on a planted bundle:
#
#   cmd_sign / cmd_verify --publish --openapi   a complete bundle is signed and verified (control); an
#       openapi.json changed or unsigned after signing is refused by verify (a removed openapi_sign or
#       openapi_verify call fails here); a missing openapi.json is refused BEFORE any signature is written;
#       a bundle built without --publish (no pinned_keys), one whose pinned_keys differ from the checkout's
#       installer/keys/, and one whose api archive's pinned-keys.txt disagrees are each refused by sign and
#       by verify.
#   build_api   passes MaranShippingBuild=true to `dotnet publish`, and writes pinned-keys.txt, only for a
#       shipping build (asserted on the composed argv through a recording shim of dotnet).
#   cmd_build   hands build_api the publish flag, and writes the manifest's pinned_keys object only with
#       --publish.
#
# UNOBSERVED HERE: a real `dotnet publish` and the real component builders; the shim records the argv.

# selftest_publish_make_root: <dir>. A fixture checkout holding installer/keys/ with the five synthetic keys.
selftest_publish_make_root() {
  local dir="$1" key
  install -d "${dir}/installer/keys"
  for key in $MARAN_PINNED_KEYS; do
    selftest_write_pinned_key "${dir}/installer/keys/${key}"
  done
}

# selftest_publish_make_bundle: <dir> <fixture root> <yes|no|stale>. A two-architecture unsigned bundle. `yes`
# is what `build --publish` writes (pinned_keys in the manifest, pinned-keys.txt in each api archive), `no`
# what a build without --publish writes (neither), `stale` has the manifest right and the api archives
# listing a different hash. Reads its caller's locals (channel, selftest_notes_url).
selftest_publish_make_bundle() {
  local dir="$1" fx_root="$2" mode="$3" arch component i first=1
  install -d "$dir"
  for arch in $MARAN_PUBLISH_ARCHES; do
    for component in $MARAN_COMPONENTS; do
      install -d "${dir}/${component}-${arch}"
      for i in $(seq 1 20); do
        echo "synthetic ${component} ${arch} file ${i} for release-bundle.sh selftest only" \
          > "${dir}/${component}-${arch}/file-${i}.txt"
      done
      if [ "$component" = "api" ]; then
        case "$mode" in
          yes) pinned_keys_lines "${fx_root}/installer/keys" > "${dir}/api-${arch}/pinned-keys.txt" ;;
          stale) printf 'licence-verification.pub %064d\n' 0 > "${dir}/api-${arch}/pinned-keys.txt" ;;
          no) : ;;
        esac
      fi
      tar_component "${dir}/${component}-${arch}" "${dir}/${component}-${arch}.tar.gz"
    done
  done
  write_integrity_manifest "$dir" "selftest"
  {
    echo "{"
    echo "  \"version\": \"selftest\","
    echo "  \"channel\": \"${channel}\","
    manifest_security_field 0
    manifest_release_fields "$selftest_notes_url"
    [ "$mode" = "no" ] || manifest_pinned_keys_field "${fx_root}/installer/keys"
    echo "  \"artifacts\": {"
    for arch in $MARAN_PUBLISH_ARCHES; do
      for component in $MARAN_COMPONENTS; do
        [ "$first" -eq 1 ] || echo "    },"
        first=0
        echo "    \"${component}-${arch}\": {"
        echo "      \"url\": \"https://releases.maran.innovayse.com/${channel}/${component}-${arch}.tar.gz\","
        echo "      \"sha256\": \"$(sha256_of "${dir}/${component}-${arch}.tar.gz")\""
      done
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${dir}/manifest.json"
}

# selftest_publish_run: <label> <ok|refused> <needle> <fixture root> <command...>. Runs the command in a
# subshell whose `root` is the fixture checkout (so installer/keys/ is the fixture's), and checks the outcome;
# a refusal must also print <needle>, so it is the intended check that refused and not another.
selftest_publish_run() {
  local label="$1" expect="$2" needle="$3" fx_root="$4" status=0 log="${work}/publish-wiring.log"
  shift 4
  # Not `( ... ) || status=$?`: a subshell on the left of `||` runs with errexit suspended for everything
  # inside it, and `set -e` inside cannot re-enable it, so a silently failing intermediate command would
  # not abort. The status is taken with errexit off in THIS shell only, around a plain subshell that keeps it.
  set +e
  ( set -e; root="$fx_root"; "$@" ) >"$log" 2>&1
  status=$?
  set -e
  case "$expect" in
    ok)
      [ "$status" -eq 0 ] || { cat "$log" >&2; release_failed "selftest: ${label}: was REFUSED (status ${status}) — the gate refuses everything, or the fixture is broken."; }
      ;;
    refused)
      [ "$status" -ne 0 ] || release_failed "selftest: ${label}: was ACCEPTED — the gate is decoration."
      grep -q -- "$needle" "$log" \
        || { cat "$log" >&2; release_failed "selftest: ${label}: was refused, but not by the check it is meant to exercise (no '${needle}' in the output)."; }
      ;;
  esac
  echo "${VERDICT_PREFIX}: OK, ${label} (expected)."
}

# selftest_publish_commands_outcome: the cmd_sign / cmd_verify half. Reads the caller's locals (work, keydir).
selftest_publish_commands_outcome() {
  local key="${keydir}/throwaway.key" pub="${keydir}/throwaway.pub" base="${work}/publish-wiring" doc
  doc='{"openapi":"3.0.1","info":{"title":"t","version":"1"},"paths":{"/api/v1/ping":{"get":{"responses":{"200":{"description":"ok"}}}}}}'
  install -d "$base"
  signed_openapi() { # <dir>: a planted, signed openapi.json
    install -d "$1"
    printf %s "$doc" > "$1/openapi.json"
    openapi_sign "$key" "$1" || release_failed "selftest: could not sign the planted openapi.json"
  }

  # The control: a complete, correctly built bundle is signed and verified, and both signatures exist.
  local a="${base}/a"
  selftest_publish_make_root "$a"
  selftest_publish_make_bundle "$a/bundle" "$a" yes
  install -d "$a/openapi"
  printf %s "$doc" > "$a/openapi/openapi.json"
  selftest_publish_run "sign --publish --openapi accepts a complete bundle" ok "" "$a" \
    cmd_sign --key "$key" --bundle "$a/bundle" --publish --openapi "$a/openapi"
  [ -s "$a/bundle/manifest.json.sig" ] && [ -s "$a/openapi/openapi.json.sig" ] \
    || release_failed "selftest: sign --publish --openapi accepted but did not write both signatures (openapi_sign is not called?)"
  selftest_publish_run "verify --publish --openapi accepts the signed bundle" ok "" "$a" \
    cmd_verify --bundle "$a/bundle" --pubkey "$pub" --publish --openapi "$a/openapi"

  # The document changed after signing: only the openapi_verify call in cmd_verify can refuse this.
  install -d "$a/openapi-tampered"
  cp "$a/openapi/openapi.json" "$a/openapi/openapi.json.sig" "$a/openapi-tampered/"
  printf ' ' >> "$a/openapi-tampered/openapi.json"
  selftest_publish_run "verify --publish refuses an openapi.json changed after signing" refused "openapi.json" "$a" \
    cmd_verify --bundle "$a/bundle" --pubkey "$pub" --publish --openapi "$a/openapi-tampered"
  install -d "$a/openapi-unsigned"
  cp "$a/openapi/openapi.json" "$a/openapi-unsigned/"
  selftest_publish_run "verify --publish refuses an openapi.json with no signature" refused "UNSIGNED" "$a" \
    cmd_verify --bundle "$a/bundle" --pubkey "$pub" --publish --openapi "$a/openapi-unsigned"

  # No document: refused before the first signature exists, so no half-signed bundle is left behind.
  local b="${base}/b"
  selftest_publish_make_root "$b"
  selftest_publish_make_bundle "$b/bundle" "$b" yes
  install -d "$b/openapi"
  selftest_publish_run "sign --publish refuses a missing openapi.json" refused "no openapi.json" "$b" \
    cmd_sign --key "$key" --bundle "$b/bundle" --publish --openapi "$b/openapi"
  [ ! -e "$b/bundle/manifest.json.sig" ] && [ ! -e "$b/bundle/integrity-manifest.json.sig" ] \
    || release_failed "selftest: a missing openapi.json was refused only AFTER a signature had been written."

  # M1: a bundle built without --publish pins nothing. Plain sign works (the same fixture a local build
  # makes); publishing it is refused by sign and by verify, which the accepted control above rules out as
  # a check that refuses everything.
  local c="${base}/c"
  selftest_publish_make_root "$c"
  selftest_publish_make_bundle "$c/bundle" "$c" no
  signed_openapi "$c/openapi"
  selftest_publish_run "sign --publish refuses a bundle with no pinned_keys" refused "pinned_keys" "$c" \
    cmd_sign --key "$key" --bundle "$c/bundle" --publish --openapi "$c/openapi"
  [ ! -e "$c/bundle/manifest.json.sig" ] || release_failed "selftest: an unpinned bundle was signed for publishing."
  selftest_publish_run "plain sign still signs a bundle built without --publish" ok "" "$c" \
    cmd_sign --key "$key" --bundle "$c/bundle"
  selftest_publish_run "verify --publish refuses a signed bundle with no pinned_keys" refused "pinned_keys" "$c" \
    cmd_verify --bundle "$c/bundle" --pubkey "$pub" --publish --openapi "$c/openapi"

  # A pinned_keys object that is not the checkout's keys (a key replaced after the build).
  local d="${base}/d"
  selftest_publish_make_root "$d"
  selftest_publish_make_bundle "$d/bundle" "$d" yes
  signed_openapi "$d/openapi"
  printf '%064d\n' 7 > "$d/installer/keys/licence-verification.pub"
  selftest_publish_run "sign --publish refuses pinned_keys that differ from installer/keys" refused "pinned_keys" "$d" \
    cmd_sign --key "$key" --bundle "$d/bundle" --publish --openapi "$d/openapi"
  selftest_publish_run "plain sign of that bundle succeeds" ok "" "$d" cmd_sign --key "$key" --bundle "$d/bundle"
  selftest_publish_run "verify --publish refuses pinned_keys that differ from installer/keys" refused "pinned_keys" "$d" \
    cmd_verify --bundle "$d/bundle" --pubkey "$pub" --publish --openapi "$d/openapi"

  # The api archive's pinned-keys.txt disagrees with the manifest.
  local e="${base}/e"
  selftest_publish_make_root "$e"
  selftest_publish_make_bundle "$e/bundle" "$e" stale
  signed_openapi "$e/openapi"
  selftest_publish_run "sign --publish refuses an api archive whose pinned-keys.txt disagrees" refused "pinned-keys.txt" "$e" \
    cmd_sign --key "$key" --bundle "$e/bundle" --publish --openapi "$e/openapi"
  selftest_publish_run "plain sign of that bundle succeeds (so verify below is what refuses)" ok "" "$e" \
    cmd_sign --key "$key" --bundle "$e/bundle"
  selftest_publish_run "verify --publish refuses an api archive whose pinned-keys.txt disagrees" refused "pinned-keys.txt" "$e" \
    cmd_verify --bundle "$e/bundle" --pubkey "$pub" --publish --openapi "$e/openapi"
}

# selftest_publish_build_outcome: the build half. Reads the caller's locals (work).
selftest_publish_build_outcome() {
  local base="${work}/build-wiring" fx="${work}/build-wiring/root" rec="${work}/build-wiring/argv" shipping out
  install -d "$base"
  selftest_publish_make_root "$fx"

  # build_api: the 4th argument decides whether `dotnet publish` gets MaranShippingBuild=true and whether
  # the keys' fingerprints are written beside the apphost. dotnet is a recording shim that stages the files
  # build_api checks for, so only the composed command line and the staged files are observed.
  for shipping in 1 0; do
    out="${base}/api-${shipping}"
    install -d "$out"
    : > "$rec"
    (
      root="$fx"
      # shellcheck disable=SC2317
      require_tool() { :; }
      # shellcheck disable=SC2317
      dotnet() {
        local arg prev=""
        printf '%s\n' "$*" >> "$rec"
        for arg in "$@"; do
          if [ "$prev" = "-o" ]; then install -d "$arg"; : > "$arg/Maran.Host"; : > "$arg/libhostfxr.so"; fi
          prev="$arg"
        done
      }
      build_api "$out" x86_64 1.2.3 "$shipping"
    ) >"${base}/build_api.log" 2>&1 || { cat "${base}/build_api.log" >&2; release_failed "selftest: build_api failed against the recording dotnet shim."; }
    grep -q -- '-p:Version=1.2.3' "$rec" || release_failed "selftest: the dotnet shim did not see build_api's command line, so the checks below prove nothing."
    if [ "$shipping" -eq 1 ]; then
      grep -q -- '-p:MaranShippingBuild=true' "$rec" \
        || release_failed "selftest: build_api did not pass MaranShippingBuild=true for a shipping build; the pinned-key gate is off."
      [ "$(cat "$out/api-x86_64/pinned-keys.txt")" = "$(pinned_keys_lines "$fx/installer/keys")" ] \
        || release_failed "selftest: a shipping build_api did not stage pinned-keys.txt."
    else
      ! grep -q -- 'MaranShippingBuild' "$rec" \
        || release_failed "selftest: build_api passed MaranShippingBuild for a non-shipping build."
      [ ! -e "$out/api-x86_64/pinned-keys.txt" ] \
        || release_failed "selftest: a non-shipping build_api staged pinned-keys.txt."
    fi
  done
  echo "${VERDICT_PREFIX}: OK, build_api passes MaranShippingBuild=true and stages pinned-keys.txt only for a shipping build (expected)."

  # cmd_build: --publish reaches build_api as 1 and puts pinned_keys in the manifest; without it, 0 and none.
  local publish_flag arch_arg line
  for publish_flag in "--publish" ""; do
    arch_arg="x86_64"; [ -n "$publish_flag" ] && arch_arg="both"
    out="${base}/cmd-build${publish_flag:+-publish}"
    : > "$rec"
    (
      root="$fx"
      # shellcheck disable=SC2317
      tree_lock_acquire() { return 0; }
      # shellcheck disable=SC2317
      tree_lock_release() { :; }
      # shellcheck disable=SC2317
      build_api() { printf '%s\n' "$*" >> "$rec"; }
      # shellcheck disable=SC2317
      build_agent() { :; }
      # shellcheck disable=SC2317
      build_frontend() { :; }
      # shellcheck disable=SC2317
      tar_component() { : > "$2"; }
      # shellcheck disable=SC2317
      write_integrity_manifest() { echo '{}' > "$1/integrity-manifest.json"; }
      # shellcheck disable=SC2086
      cmd_build --version 1.2.3 --arch "$arch_arg" $publish_flag --out "$out"
    ) >"${base}/cmd_build.log" 2>&1 || { cat "${base}/cmd_build.log" >&2; release_failed "selftest: cmd_build failed against the stubbed component builders."; }
    [ -s "$rec" ] || release_failed "selftest: cmd_build never called build_api, so the checks below prove nothing."
    while read -r line; do
      if [ -n "$publish_flag" ]; then
        [ "${line##* }" = "1" ] || release_failed "selftest: 'build --publish' called build_api without the shipping flag: ${line}"
      else
        [ "${line##* }" = "0" ] || release_failed "selftest: a build without --publish called build_api with the shipping flag: ${line}"
      fi
    done <"$rec"
    if [ -n "$publish_flag" ]; then
      [ "$(sed -n '/^[[:space:]]*"pinned_keys"[[:space:]]*:/,/^[[:space:]]*},/p' "$out/manifest.json")" = "$(manifest_pinned_keys_field "$fx/installer/keys")" ] \
        || release_failed "selftest: 'build --publish' did not write the pinned_keys object into the manifest."
    else
      ! grep -q '"pinned_keys"' "$out/manifest.json" \
        || release_failed "selftest: a build without --publish wrote pinned_keys into the manifest."
    fi
  done
  echo "${VERDICT_PREFIX}: OK, cmd_build hands build_api the shipping flag and writes pinned_keys only with --publish (expected)."
}

# selftest_publish_wiring_outcome: outcome 13. Called by cmd_selftest and reads its locals (work, keydir,
# channel, selftest_notes_url).
selftest_publish_wiring_outcome() {
  echo
  echo "-- outcome 13: the release commands are wired to the publish gates --"
  selftest_publish_commands_outcome
  selftest_publish_build_outcome
  echo "UNOBSERVED HERE: a real dotnet publish and the real component builders; the shim records the argv."
}
