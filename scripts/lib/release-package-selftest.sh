#!/usr/bin/env bash
# Selftest for `maran release package` (issue #310): `maran release package-selftest`.
#
# SOURCED by scripts/lib/release-bundle.sh after scripts/lib/release-package.sh. It plants a signed
# two-architecture bundle in the shape `maran release build --arch both` writes, runs the real
# cmd_package against it, and then checks the output WITHOUT the packager's own verifier: it extracts
# each tarball and runs `openssl dgst` directly, and compares the planted per-architecture marker
# inside the agent archive, so a packager that picked the wrong architecture's archive or shipped an
# unverified bundle cannot agree with itself.
#
# Every refusal is paired with the inverse control rules/testing.md asks for: the same command on an
# untouched bundle ACCEPTS. A packager mutated to refuse everything passes a test that only hands it
# broken input.
#
# UNOBSERVED HERE: the real toolchains. The components are synthetic, clearly marked files; this
# proves the packaging and verification logic, not that dotnet, cargo or npm produced good output.
# A packaged bundle's acceptance by the installer itself is exercised by `maran installer`.

# plant_signed_bundle: <dir> <key> <arches> [extra marker]. Stages one directory per component and
# architecture with its entry file (the marker names the architecture), tars them, writes both
# manifests and signs them with the throwaway key — the same writers cmd_build uses.
plant_signed_bundle() {
  local dir="$1" key="$2" arches="$3" arch component staged entry i first=1
  install -d "$dir"
  for arch in $arches; do
    for component in $MARAN_COMPONENTS; do
      staged="${dir}/${component}-${arch}"
      install -d "$staged"
      entry="$(package_entry_file "$component")"
      echo "synthetic ${component} for ${arch} — selftest only" > "${staged}/${entry}"
      for i in $(seq 1 20); do
        echo "filler ${i} ${component} ${arch}" > "${staged}/file-${i}.txt"
      done
      tar_component "$staged" "${dir}/${component}-${arch}.tar.gz"
    done
  done
  write_integrity_manifest "$dir" "selftest" >/dev/null
  {
    echo "{"
    echo "  \"version\": \"selftest\","
    echo "  \"artifacts\": {"
    for arch in $arches; do
      for component in $MARAN_COMPONENTS; do
        [ "$first" -eq 1 ] || echo "    },"
        first=0
        echo "    \"${component}-${arch}\": {"
        echo "      \"url\": \"https://releases.maran.innovayse.com/stable/${component}-${arch}.tar.gz\","
        echo "      \"sha256\": \"$(sha256_of "${dir}/${component}-${arch}.tar.gz")\""
      done
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${dir}/manifest.json"
  MARAN_RELEASE_SIGNING_KEY="" cmd_sign --key "$key" --bundle "$dir" >/dev/null
}

# package_must_refuse: <label> <output dir that must stay without a bundle> <reason pattern>
# <cmd_package args...>. Runs cmd_package in a subshell (release_failed exits), requires a non-zero
# status whose output matches the pattern (a refusal for the WRONG reason, such as a typo in the
# fixture, would otherwise pass for the one under test), and requires that no tarball was left behind — a refused bundle that still exists on disk would be shipped.
package_must_refuse() {
  local label="$1" leftover="$2" pattern="$3" output
  shift 3
  if output="$( ( cmd_package "$@" ) 2>&1 )"; then
    release_failed "package selftest: ${label} was ACCEPTED — the check behind it is decoration."
  fi
  printf '%s' "$output" | grep -qE -- "$pattern" \
    || release_failed "package selftest: ${label} was refused for another reason than expected (wanted /${pattern}/): ${output}"
  if compgen -G "${leftover}/*.tar.gz" >/dev/null; then
    release_failed "package selftest: ${label} was refused but left a tarball in ${leftover}."
  fi
  echo "${VERDICT_PREFIX}: OK — ${label} REFUSED, nothing left behind (expected)."
}

cmd_package_selftest() {
  require_tool openssl "generates the throwaway key and verifies every bundle"
  require_tool tar "writes and extracts the bundles"
  local work
  work="$(mktemp -d)"
  trap 'rm -rf -- "$work"' RETURN

  local keydir="${work}/keys" pub other_pub
  install -d -m 0700 "$keydir"
  openssl ecparam -name secp384r1 -genkey -noout -out "${keydir}/a.key" >/dev/null 2>&1
  openssl pkey -in "${keydir}/a.key" -pubout -out "${keydir}/a.pub" >/dev/null 2>&1
  openssl ecparam -name secp384r1 -genkey -noout -out "${keydir}/b.key" >/dev/null 2>&1
  openssl pkey -in "${keydir}/b.key" -pubout -out "${keydir}/b.pub" >/dev/null 2>&1
  pub="${keydir}/a.pub"
  other_pub="${keydir}/b.pub"

  local good="${work}/good"
  plant_signed_bundle "$good" "${keydir}/a.key" "x86_64 aarch64"

  echo "-- case 1: a two-architecture build yields one verified bundle per architecture --"
  local out="${work}/out" arch tarball check
  cmd_package --bundle "$good" --out "$out" --pubkey "$pub" >/dev/null \
    || release_failed "package selftest: a complete, signed two-architecture bundle was refused — the packager refuses everything, or the fixture is broken."
  for arch in x86_64 aarch64; do
    tarball="${out}/maran-offline-selftest-${arch}.tar.gz"
    [ -f "$tarball" ] || release_failed "package selftest: no ${tarball##*/} was written."
    check="${work}/check-${arch}"
    install -d "$check"
    tar -xzf "$tarball" -C "$check"
    # Independent of verify_offline_tarball: openssl called directly, and the marker in the unsuffixed
    # agent archive must name THIS architecture and not the other.
    openssl dgst -sha384 -verify "$pub" -signature "${check}/manifest.json.sig" "${check}/manifest.json" >/dev/null 2>&1 \
      || release_failed "package selftest: openssl does not verify the ${arch} bundle's manifest."
    tar -xzf "${check}/agent.tar.gz" -C "$check" ./maran-agent
    grep -q "for ${arch} " "${check}/maran-agent" \
      || release_failed "package selftest: the ${arch} bundle carries another architecture's agent."
    [ "$(sha256_of "${check}/agent.tar.gz")" = "$(sha256_of "${good}/agent-${arch}.tar.gz")" ] \
      || release_failed "package selftest: the ${arch} bundle's agent.tar.gz is not the staged agent-${arch}.tar.gz."
    echo "${VERDICT_PREFIX}: OK — ${tarball##*/}: signature verified by openssl, agent is the ${arch} one (expected)."
  done

  echo
  echo "-- case 2: a tampered aarch64 archive is refused, and the untouched x86_64 half is not --"
  local tampered="${work}/tampered"
  cp -r "$good" "$tampered"
  printf 'TAMPERED' >> "${tampered}/agent-aarch64.tar.gz"
  package_must_refuse "an aarch64 bundle whose agent archive changed after signing" "${work}/o-tamper" "does not match manifest entry agent-aarch64" \
    --bundle "$tampered" --arch aarch64 --out "${work}/o-tamper" --pubkey "$pub"
  cmd_package --bundle "$tampered" --arch x86_64 --out "${work}/o-tamper-ok" --pubkey "$pub" >/dev/null \
    || release_failed "package selftest: the x86_64 bundle was refused over damage to the aarch64 archive — the refusal is not attributable."
  echo "${VERDICT_PREFIX}: OK — the same tampered bundle still yields a good x86_64 bundle (inverse control, expected)."

  echo
  echo "-- case 3: unsigned, wrong key, incomplete architecture, unrunnable layout --"
  local unsigned="${work}/unsigned"
  cp -r "$good" "$unsigned"
  rm -f "${unsigned}/manifest.json.sig"
  package_must_refuse "a bundle with no manifest.json.sig" "${work}/o-unsigned" "no manifest.json.sig" \
    --bundle "$unsigned" --out "${work}/o-unsigned" --pubkey "$pub"
  package_must_refuse "a bundle signed by a key the installer does not trust" "${work}/o-key" "does NOT verify" \
    --bundle "$good" --out "${work}/o-key" --pubkey "$other_pub"
  local x86_only="${work}/x86-only"
  plant_signed_bundle "$x86_only" "${keydir}/a.key" "x86_64"
  package_must_refuse "a request for aarch64 from an x86_64-only build" "${work}/o-missing" "not complete" \
    --bundle "$x86_only" --arch aarch64 --out "${work}/o-missing" --pubkey "$pub"
  package_must_refuse "one output file for two architectures" "${work}/o-ambiguous" "names one file" \
    --bundle "$good" --out "${work}/o-ambiguous/both.tar.gz" --pubkey "$pub"
  # Signed over a correct checksum, so only the layout check can see that nothing would run.
  local hollow="${work}/hollow"
  cp -r "$good" "$hollow"
  rm -rf -- "${hollow}/agent-x86_64"
  install -d "${hollow}/agent-x86_64"
  echo "not the agent" > "${hollow}/agent-x86_64/readme.txt"
  tar_component "${hollow}/agent-x86_64" "${hollow}/agent-x86_64.tar.gz"
  plant_resign "$hollow" "${keydir}/a.key"
  package_must_refuse "an agent archive with no maran-agent at its root (valid signature and checksum)" "${work}/o-hollow" "holds no maran-agent" \
    --bundle "$hollow" --arch x86_64 --out "${work}/o-hollow" --pubkey "$pub"

  echo
  echo "-- case 4: a machine without openssl answers DID NOT RUN, status 2, not a verdict --"
  local empty_path="${work}/empty-path" refusal status=0
  install -d "$empty_path"
  refusal="$( ( PATH="$empty_path"; cmd_package --bundle "$good" --out "${work}/o-noopenssl" --pubkey "$pub" ) 2>&1 )" || status=$?
  if [ "$status" -eq "$SUITE_STATUS_DID_NOT_RUN" ] && printf '%s' "$refusal" | grep -q 'DID NOT RUN'; then
    echo "${VERDICT_PREFIX}: OK — no openssl on PATH printed DID NOT RUN and returned ${status} (expected; the inverse control is case 1, same arguments, openssl present)."
  else
    release_failed "package selftest: with no openssl the packager returned ${status} and said: ${refusal}"
  fi

  echo
  release_ok "package selftest: a two-architecture build packaged into one openssl-verified bundle per architecture, each carrying its own architecture's archives; tampering, a missing signature, an untrusted key, an absent architecture, an ambiguous --out and an unrunnable archive were all refused with nothing left on disk, and a missing openssl refused with DID NOT RUN."
}

# plant_resign: <bundle dir> <key>. Rewrites the manifest's agent-x86_64 checksum to the bundle's
# current archive and signs again, which is what a build that produced a hollow archive would have
# done — every check except the layout one then passes.
plant_resign() {
  local dir="$1" key="$2" new old
  new="$(sha256_of "${dir}/agent-x86_64.tar.gz")"
  old="$(manifest_sha256 "${dir}/manifest.json" agent x86_64)"
  sed -i "s/${old}/${new}/" "${dir}/manifest.json"
  MARAN_RELEASE_SIGNING_KEY="" cmd_sign --key "$key" --bundle "$dir" >/dev/null
}
