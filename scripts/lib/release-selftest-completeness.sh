#!/usr/bin/env bash
# Selftest outcomes 7 and 8 of `maran release selftest`: the per-component floor and the two-architecture publish check.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

# selftest_floor_outcome: outcome 7. Called by cmd_selftest and reads its locals (work).
selftest_floor_outcome() {
  echo
  echo "-- outcome 7: the vacuity floor is PER COMPONENT, not a total (issue #473) --"
  # Each broken bundle is a fully staged one with exactly ONE defect, so a refusal is attributable to
  # that defect. Every one has well over the old 50-file total, which is the point: the total passed
  # all of them.
  local floor_dir comp_under_test other
  make_floor_fixture() {
    local dir="$1" comp n
    install -d "$dir"
    for comp in $MARAN_COMPONENTS; do
      install -d "${dir}/${comp}"
      for n in $(seq 1 60); do echo "f${n}" > "${dir}/${comp}/file-${n}.txt"; done
    done
  }
  floor_dir="${work}/floor-accepted"
  make_floor_fixture "$floor_dir"
  ( write_integrity_manifest "$floor_dir" "selftest-floor" ) >/dev/null 2>&1 \
    || release_failed "selftest: write_integrity_manifest REFUSED a bundle with 60 files in every component — the per-component floor refuses everything."
  echo "${VERDICT_PREFIX}: OK — a bundle with every component well above its floor was ACCEPTED (expected)."
  for comp_under_test in $MARAN_COMPONENTS; do
    # (a) the component's directory is present but EMPTY.
    floor_dir="${work}/floor-empty-${comp_under_test}"
    make_floor_fixture "$floor_dir"
    rm -rf -- "${floor_dir:?}/${comp_under_test}"
    install -d "${floor_dir}/${comp_under_test}"
    if ( write_integrity_manifest "$floor_dir" "selftest-floor" ) >/dev/null 2>&1; then
      release_failed "selftest: write_integrity_manifest ACCEPTED a bundle whose ${comp_under_test} directory is empty (the other components carry 120 files) — the floor is a total again."
    fi
    # (b) the component has no directory at all.
    floor_dir="${work}/floor-missing-${comp_under_test}"
    make_floor_fixture "$floor_dir"
    rm -rf -- "${floor_dir:?}/${comp_under_test}"
    if ( write_integrity_manifest "$floor_dir" "selftest-floor" ) >/dev/null 2>&1; then
      release_failed "selftest: write_integrity_manifest ACCEPTED a bundle with no ${comp_under_test} directory at all."
    fi
    echo "${VERDICT_PREFIX}: OK — a bundle with ${comp_under_test} empty, and one with ${comp_under_test} absent, were both REFUSED (expected)."
  done
  # (c) one architecture's directory of one component is missing: the other components are staged
  # for two architectures, the frontend for one.
  floor_dir="${work}/floor-arch-mismatch"
  install -d "$floor_dir"
  for comp_under_test in $MARAN_COMPONENTS; do
    for other in x86_64 aarch64; do
      [ "$comp_under_test" = "frontend" ] && [ "$other" = "aarch64" ] && continue
      install -d "${floor_dir}/${comp_under_test}-${other}"
      for n in $(seq 1 20); do echo "f${n}" > "${floor_dir}/${comp_under_test}-${other}/file-${n}.txt"; done
    done
  done
  if ( write_integrity_manifest "$floor_dir" "selftest-floor" ) >/dev/null 2>&1; then
    release_failed "selftest: write_integrity_manifest ACCEPTED a bundle whose frontend is missing for aarch64 only."
  fi
  echo "${VERDICT_PREFIX}: OK — a bundle with one component missing one architecture was REFUSED (expected)."
  # (d) a file name that would forge JSON is refused, not signed.
  floor_dir="${work}/floor-hostile-name"
  make_floor_fixture "$floor_dir"
  echo x > "${floor_dir}/frontend/evil\".txt"
  if ( write_integrity_manifest "$floor_dir" "selftest-floor" ) >/dev/null 2>&1; then
    release_failed "selftest: write_integrity_manifest ACCEPTED a file name containing a double quote."
  fi
  echo "${VERDICT_PREFIX}: OK — a staged file name carrying a double quote was REFUSED (expected)."
}

# selftest_publish_outcome: outcome 8. Called by cmd_selftest and reads its locals (work, keydir, channel,
# selftest_notes_url, i).
selftest_publish_outcome() {
  echo
  echo "-- outcome 8: a published release must carry BOTH architectures --"
  # Two-arch synthetic bundle: each staged dir holds 20 files, so every directory clears its component's floor.
  # Built twice from one writer so the only difference between the accepted and the refused bundle is
  # the aarch64 keys themselves, which is what makes the refusal attributable to the check.
  local pub_arch pub_bundle pub_component pub_first
  make_two_arch_fixture() {
    local dir="$1" arches="$2"
    install -d "$dir"
    for pub_arch in $arches; do
      for pub_component in $MARAN_COMPONENTS; do
        install -d "${dir}/${pub_component}-${pub_arch}"
        for i in $(seq 1 20); do
          echo "synthetic ${pub_component} ${pub_arch} file ${i} for release-bundle.sh selftest only" \
            > "${dir}/${pub_component}-${pub_arch}/file-${i}.txt"
        done
        tar_component "${dir}/${pub_component}-${pub_arch}" "${dir}/${pub_component}-${pub_arch}.tar.gz"
      done
    done
    write_integrity_manifest "$dir" "selftest"
    pub_first=1
    {
      echo "{"
      echo "  \"version\": \"selftest\","
      echo "  \"channel\": \"${channel}\","
      manifest_security_field 0
      manifest_release_fields "$selftest_notes_url"
      echo "  \"artifacts\": {"
      for pub_arch in $arches; do
        for pub_component in $MARAN_COMPONENTS; do
          [ "$pub_first" -eq 1 ] || echo "    },"
          pub_first=0
          echo "    \"${pub_component}-${pub_arch}\": {"
          echo "      \"url\": \"https://releases.maran.innovayse.com/${channel}/${pub_component}-${pub_arch}.tar.gz\","
          echo "      \"sha256\": \"$(sha256_of "${dir}/${pub_component}-${pub_arch}.tar.gz")\""
        done
      done
      echo "    }"
      echo "  }"
      echo "}"
    } > "${dir}/manifest.json"
    MARAN_RELEASE_SIGNING_KEY="" cmd_sign --key "${keydir}/throwaway.key" --bundle "$dir" >/dev/null
  }

  pub_bundle="${work}/two-arch"
  make_two_arch_fixture "$pub_bundle" "x86_64 aarch64"
  if verify_bundle "$pub_bundle" "${keydir}/throwaway.pub" && require_publishable_manifest "${pub_bundle}/manifest.json"; then
    echo "${VERDICT_PREFIX}: OK — two-architecture bundle ACCEPTED by verify and by the publish check (positive control, expected)."
  else
    release_failed "selftest: a complete two-architecture bundle was refused — the publish check refuses everything, or the fixture is broken."
  fi

  local one_arch="${work}/one-arch"
  make_two_arch_fixture "$one_arch" "x86_64"
  # The signature and every checksum are GOOD here, so plain verify accepting it proves the refusal
  # below comes from the publish check and from nothing else.
  verify_bundle "$one_arch" "${keydir}/throwaway.pub" \
    || release_failed "selftest: the x86_64-only fixture failed plain verify, so the next check would prove nothing."
  local refusal
  if refusal="$(require_publishable_manifest "${one_arch}/manifest.json" 2>&1)"; then
    release_failed "selftest: the publish check ACCEPTED a bundle with no aarch64 keys — it is decoration."
  elif grep -q 'api-aarch64' <<<"$refusal" && grep -q 'agent-aarch64' <<<"$refusal" \
       && grep -q 'frontend-aarch64' <<<"$refusal"; then
    echo "${VERDICT_PREFIX}: OK — bundle without aarch64 keys REFUSED, naming api-aarch64, agent-aarch64 and frontend-aarch64 (inverse control, expected)."
  else
    release_failed "selftest: the publish check refused the x86_64-only bundle but did not name the missing aarch64 keys: ${refusal}"
  fi

  if ( cmd_build --publish --arch x86_64 --out "${work}/never-built" ) >/dev/null 2>&1; then
    release_failed "selftest: 'build --publish --arch x86_64' was ACCEPTED — the build-side refusal is decoration."
  elif [ -e "${work}/never-built" ]; then
    release_failed "selftest: 'build --publish --arch x86_64' was refused only AFTER it started writing output."
  else
    echo "${VERDICT_PREFIX}: OK — 'build --publish --arch x86_64' REFUSED before any output was written (expected)."
  fi
  echo "UNOBSERVED HERE: the build-side refusal of a --arch both build is not exercised, because that would run the real toolchains."
}
