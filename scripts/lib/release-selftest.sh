#!/usr/bin/env bash
# `maran release selftest`: cmd_selftest builds a synthetic signed bundle and runs the phases below it.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

# cmd_selftest: the inverse-control demonstration rules/testing.md requires of any
# refusing gate — sign with a THROWAWAY key (never committed, never a production key),
# verify OK, then prove verify REFUSES a tampered component and REFUSES an unsigned
# bundle. A verify that only ever sees a good bundle proves nothing.
#
# This exercises the MANIFEST/SIGNATURE/CHECKSUM machinery only — the same
# manifest-writing and verify_bundle code cmd_build and cmd_verify use — against
# synthetic, clearly-fake component archives, NOT a real build. It does not call
# cmd_build: at the time this was written, `cargo build --release -p maran-agent` does
# not compile (crates/agent/src/services/accounts/accounts_service.rs:473, `no field
# quota_bytes on type AccountUsage` — the concurrent quota-enforceability lane's
# in-progress work, out of scope here and not this script's to fix). That failure is
# reported as its own NOT DONE; it must not block proving the signing/verification logic
# is correct, and a selftest that quietly swapped in fake bytes and called them a real
# release would be exactly the decoration rules/testing.md warns against — so this
# synthetic bundle is never written where cmd_build's or cmd_package's real output goes,
# and every line below says "selftest" or "synthetic", never "release".
cmd_selftest() {
  require_tool openssl "generates the throwaway key and runs every check below"
  local work
  work="$(mktemp -d)"
  trap 'rm -rf -- "$work"' RETURN

  # The selftest builds its own manifest inline rather than calling cmd_build, so it needs the
  # same `channel` the real builder has — without it the URL would interpolate an empty segment
  # and this test would be asserting against a shape no release ever has.
  local channel="stable" selftest_notes_url="https://github.com/innovayse/maran/releases/tag/vselftest"
  local keydir="${work}/keys" bundle="${work}/bundle"
  install -d -m 0700 "$keydir"
  # The SAME curve and digest the real key uses, because a selftest that signs with a different
  # algorithm than production would verify an invocation nobody ships (rules/testing.md).
  openssl ecparam -name secp384r1 -genkey -noout -out "${keydir}/throwaway.key" >/dev/null 2>&1
  openssl pkey -in "${keydir}/throwaway.key" -pubout -out "${keydir}/throwaway.pub" >/dev/null 2>&1

  install -d "$bundle"
  local component arch
  arch="$(arch_name)"

  # Synthetic per-component STAGED DIRECTORIES (not just a single payload file), so
  # write_integrity_manifest has something real to walk — 20 files per component, which
  # clears every component's own floor (integrity_manifest_min_files). Named "selftest", never
  # "release", per this function's own header comment.
  local i
  for component in api agent frontend; do
    install -d "${bundle}/${component}"
    for i in $(seq 1 20); do
      echo "synthetic ${component} file ${i} for release-bundle.sh selftest only — not a real artifact" \
        > "${bundle}/${component}/file-${i}.txt"
    done
    tar_component "${bundle}/${component}" "${bundle}/${component}.tar.gz"
  done

  write_integrity_manifest "$bundle" "selftest"
  {
    echo "{"
    echo "  \"version\": \"selftest\","
    echo "  \"channel\": \"${channel}\","
    manifest_security_field 0
    manifest_release_fields "$selftest_notes_url"
    echo "  \"artifacts\": {"
    local first=1
    for component in api agent frontend; do
      [ "$first" -eq 1 ] || echo "    },"
      first=0
      echo "    \"${component}-${arch}\": {"
      echo "      \"url\": \"https://releases.maran.innovayse.com/${channel}/${component}-${arch}.tar.gz\","
      echo "      \"sha256\": \"$(sha256_of "${bundle}/${component}.tar.gz")\""
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${bundle}/manifest.json"
  MARAN_RELEASE_SIGNING_KEY="" cmd_sign --key "${keydir}/throwaway.key" --bundle "$bundle"

  selftest_signature_outcomes
  selftest_floor_outcome
  selftest_publish_outcome
  selftest_channel_outcome
  selftest_notes_outcome
  selftest_security_outcome
  selftest_pinned_keys_outcome
  selftest_publish_wiring_outcome

  echo
  release_ok "selftest: build -> sign -> verify accepted a good bundle, refused a tampered one and" \
    "an unsigned one, wrote a complete integrity-manifest.json across all three components," \
    "verified and refused it exactly like manifest.json, documented the" \
    "signature-does-not-re-derive-hashes gap, refused a build below the vacuity floor, held a" \
    "published release to both architectures, and held the signed channel field (tamper, absence" \
    "and url disagreement refused; a correct beta accepted), and held the signed notes_url and" \
    "released fields (tamper refused, hostile build-time URLs refused), and held the signed" \
    "security flag (tamper refused by the signature, a correctly signed one accepted), and refused a" \
    "shipping build of the panel with a pinned public key missing or empty (a non-shipping Release build accepted) while accepting one with all five, listed the five in the manifest, and held the" \
    "sign and verify commands to the publish gates end to end (an unpinned bundle, a changed key, a disagreeing api archive and a changed or unsigned openapi.json refused; the shipping flag reaching the build)."
}

# selftest_signature_outcomes: outcomes 1 to 6. Called by cmd_selftest and reads its locals (work, keydir, bundle).
selftest_signature_outcomes() {
  echo
  echo "-- outcome 1: untouched bundle --"
  if verify_bundle "$bundle" "${keydir}/throwaway.pub"; then
    echo "${VERDICT_PREFIX}: OK — untouched bundle ACCEPTED (expected)."
  else
    release_failed "selftest: a freshly built and signed bundle did not verify — the builder or the verifier is broken."
  fi

  echo
  echo "-- outcome 2: one byte flipped in a component archive --"
  local tampered="${work}/tampered"
  cp -r "$bundle" "$tampered"
  printf 'TAMPERED' >> "${tampered}/agent.tar.gz"
  if verify_bundle "$tampered" "${keydir}/throwaway.pub"; then
    release_failed "selftest: verify ACCEPTED a bundle with a tampered agent.tar.gz — the checksum check is decoration."
  else
    echo "${VERDICT_PREFIX}: OK — tampered bundle REFUSED (expected)."
  fi

  echo
  echo "-- outcome 3: signature file removed --"
  local unsigned="${work}/unsigned"
  cp -r "$bundle" "$unsigned"
  rm -f "${unsigned}/manifest.json.sig"
  if verify_bundle "$unsigned" "${keydir}/throwaway.pub"; then
    release_failed "selftest: verify ACCEPTED a bundle with no manifest.json.sig — the signature check is decoration."
  else
    echo "${VERDICT_PREFIX}: OK — unsigned bundle REFUSED (expected)."
  fi

  echo
  echo "-- outcome 4: integrity-manifest.json names every synthetic file across ALL three components --"
  local api_count agent_count frontend_count total_count
  api_count="$(grep -c '"api/file-' "${bundle}/integrity-manifest.json")"
  agent_count="$(grep -c '"agent/file-' "${bundle}/integrity-manifest.json")"
  frontend_count="$(grep -c '"frontend/file-' "${bundle}/integrity-manifest.json")"
  total_count=$((api_count + agent_count + frontend_count))
  if [ "$api_count" -eq 20 ] && [ "$agent_count" -eq 20 ] && [ "$frontend_count" -eq 20 ] && [ "$total_count" -eq 60 ]; then
    echo "${VERDICT_PREFIX}: OK — integrity-manifest.json has 20 api + 20 agent + 20 frontend entries (60 total), not just one component (expected)."
  else
    release_failed "selftest: integrity-manifest.json has api=${api_count} agent=${agent_count} frontend=${frontend_count} — write_integrity_manifest silently omitted at least one component."
  fi

  echo
  echo "-- outcome 5: untouched integrity-manifest.json verifies against integrity-manifest.json.sig --"
  if verify_release_file "${keydir}/throwaway.pub" "${bundle}/integrity-manifest.json" \
       "${bundle}/integrity-manifest.json.sig"; then
    echo "${VERDICT_PREFIX}: OK — untouched integrity-manifest.json ACCEPTED against its own signature (expected)."
  else
    release_failed "selftest: a freshly written and signed integrity-manifest.json did not verify against its own signature — the signing or verification invocation is broken."
  fi

  echo
  echo "-- outcome 6: a build-time defect (one wrong hex character in one entry's sha256, BEFORE signing) --"
  # This is deliberately NOT a tamper-after-signing case (outcomes 2/3 already cover that,
  # and cover it correctly: any byte changed after signing makes the signature refuse). This
  # models the honest gap the plan calls out instead: a manifest that was ALREADY WRONG at
  # the moment it was signed — one entry's sha256 corrupted by a build-time defect, the rest
  # of the file untouched and internally consistent. Once signed, that (defective) file's
  # signature verifies exactly like a correct one, because the signature only ever attests
  # "these bytes were signed by this key" — it does not re-derive any hash inside them.
  local defective="${work}/defective-manifest.json"
  cp "${bundle}/integrity-manifest.json" "$defective"
  # Whatever the first hex digit is, it becomes a DIFFERENT one (0 becomes 1, anything else becomes 0).
  # The earlier form only changed a leading 0 or 1, so it changed nothing whenever the first
  # entry's hash began with another digit, which depends on the order `find` lists files in and
  # failed on a CI runner while passing on a workstation.
  sed -i '0,/": "[0-9a-f]\{64\}"/{s/": "0/": "\x01/; s/": "\([1-9a-f]\)/": "0/; s/\x01/1/}' "$defective"
  if cmp -s "${bundle}/integrity-manifest.json" "$defective"; then
    release_failed "selftest: outcome 6's sed did not actually change any byte — the corruption step itself is broken, so this proves nothing."
  fi
  sign_release_file "${keydir}/throwaway.key" "$defective" "${defective}.sig" \
    || release_failed "selftest: could not sign the outcome-6 fixture"
  if verify_release_file "${keydir}/throwaway.pub" "$defective" "${defective}.sig"; then
    echo "${VERDICT_PREFIX}: OK — a manifest with one entry's sha256 corrupted BEFORE signing still verifies once signed (expected, and the honest boundary this feature has: the signature proves the bytes were signed by this key, never that any hash inside them is correct — the build's own count/floor check does not catch this either, and this outcome documents that gap rather than hiding it)."
  else
    release_failed "selftest: a manifest signed with a valid key over its own (defective) bytes did NOT verify — the sign/verify invocation pairing itself is broken, which is a bigger problem than the gap this outcome exists to document."
  fi
}
