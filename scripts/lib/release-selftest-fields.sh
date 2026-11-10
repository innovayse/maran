#!/usr/bin/env bash
# Selftest outcomes 9 to 11 of `maran release selftest`: the signed channel, notes_url, released and security fields.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

# selftest_channel_outcome: outcome 9. Called by cmd_selftest and reads its locals (work, keydir, bundle).
selftest_channel_outcome() {
  echo
  echo "-- outcome 9: the signed channel field --"
  # Each broken copy differs from the untouched bundle in the channel and nothing else, and the
  # untouched bundle (outcome 1) and a correctly re-signed beta bundle (below) are the inverse
  # controls: a check mutated to refuse every manifest fails the beta case.
  local ch_dir ch_out
  ch_dir="${work}/channel-tampered"
  cp -r "$bundle" "$ch_dir"
  sed -i 's/"channel": "stable"/"channel": "beta"/' "${ch_dir}/manifest.json"
  if cmp -s "${bundle}/manifest.json" "${ch_dir}/manifest.json"; then
    release_failed "selftest: outcome 9's sed did not change the channel, so the tamper step is broken" \
      "and this proves nothing."
  fi
  if ch_out="$(verify_bundle "$ch_dir" "${keydir}/throwaway.pub" 2>&1)"; then
    release_failed "selftest: verify ACCEPTED a manifest whose channel was edited after signing;" \
      "the channel is not covered by the signature."
  elif grep -q 'signature does NOT verify' <<<"$ch_out"; then
    echo "${VERDICT_PREFIX}: OK, a channel edited after signing REFUSED by the signature (expected)."
  else
    release_failed "selftest: a tampered channel was refused for another reason than the signature: ${ch_out}"
  fi

  ch_dir="${work}/channel-missing"
  cp -r "$bundle" "$ch_dir"
  sed -i '/"channel"/d' "${ch_dir}/manifest.json"
  sign_release_file "${keydir}/throwaway.key" "${ch_dir}/manifest.json" "${ch_dir}/manifest.json.sig"
  if ch_out="$(verify_bundle "$ch_dir" "${keydir}/throwaway.pub" 2>&1)"; then
    release_failed "selftest: verify ACCEPTED a correctly signed manifest with no channel field;" \
      "the presence check is decoration."
  elif grep -q 'carries no "channel" field' <<<"$ch_out"; then
    echo "${VERDICT_PREFIX}: OK, a signed manifest without a channel REFUSED," \
      "naming the missing field (expected)."
  else
    release_failed "selftest: a manifest without a channel was refused for another reason: ${ch_out}"
  fi

  ch_dir="${work}/channel-mismatch"
  cp -r "$bundle" "$ch_dir"
  sed -i 's/"channel": "stable"/"channel": "beta"/' "${ch_dir}/manifest.json"
  sign_release_file "${keydir}/throwaway.key" "${ch_dir}/manifest.json" "${ch_dir}/manifest.json.sig"
  if ch_out="$(verify_bundle "$ch_dir" "${keydir}/throwaway.pub" 2>&1)"; then
    release_failed "selftest: verify ACCEPTED a signed manifest whose channel (beta) disagrees with its stable urls."
  elif grep -q 'artifact urls name' <<<"$ch_out"; then
    echo "${VERDICT_PREFIX}: OK, a signed manifest whose channel disagrees with its urls REFUSED (expected)."
  else
    release_failed "selftest: a channel/url mismatch was refused for another reason: ${ch_out}"
  fi

  ch_dir="${work}/channel-beta"
  cp -r "$bundle" "$ch_dir"
  sed -i 's/"channel": "stable"/"channel": "beta"/; s#/stable/#/beta/#' "${ch_dir}/manifest.json"
  sign_release_file "${keydir}/throwaway.key" "${ch_dir}/manifest.json" "${ch_dir}/manifest.json.sig"
  if verify_bundle "$ch_dir" "${keydir}/throwaway.pub"; then
    echo "${VERDICT_PREFIX}: OK, a correctly signed beta manifest with beta urls ACCEPTED" \
      "(inverse control, expected)."
  else
    release_failed "selftest: a correctly signed beta bundle was refused; the channel check" \
      "refuses everything, or the fixture is broken."
  fi
}

# selftest_notes_outcome: outcome 10. Called by cmd_selftest and reads its locals (work, keydir, bundle).
selftest_notes_outcome() {
  echo
  echo "-- outcome 10: the signed notes_url and released fields --"
  local nf_dir nf_out field
  grep -qE '^  "notes_url": "https://[^"]+",$' "${bundle}/manifest.json" \
    && grep -qE '^  "released": "[0-9]{4}-[0-9]{2}-[0-9]{2}",$' "${bundle}/manifest.json" \
    || release_failed "selftest: the signed manifest lacks notes_url or released, so the checks below prove nothing."
  echo "${VERDICT_PREFIX}: OK, the signed manifest carries notes_url and released."
  for field in notes_url released; do
    nf_dir="${work}/${field}-tampered"
    cp -r "$bundle" "$nf_dir"
    sed -i "s#\"${field}\": \"\([^\"]*\)\"#\"${field}\": \"\1x\"#" "${nf_dir}/manifest.json"
    cmp -s "${bundle}/manifest.json" "${nf_dir}/manifest.json" \
      && release_failed "selftest: outcome 10's sed did not change ${field}, so the tamper step is broken and this proves nothing."
    if nf_out="$(verify_bundle "$nf_dir" "${keydir}/throwaway.pub" 2>&1)"; then
      release_failed "selftest: verify ACCEPTED a manifest whose ${field} was edited after signing; the field is not covered by the signature."
    elif grep -q 'signature does NOT verify' <<<"$nf_out"; then
      echo "${VERDICT_PREFIX}: OK, ${field} edited after signing REFUSED by the signature (expected)."
    else
      release_failed "selftest: a tampered ${field} was refused for another reason than the signature: ${nf_out}"
    fi
  done
  # The builder holds the CLI's rule: a URL the CLI would drop is refused at build, and a good one is
  # written (the inverse control: a check that refuses everything fails the second line).
  for nf_out in 'http://example.com/notes' 'https://example.com/a b' $'https://example.com/\x1b[2J' 'https://' 'ftp://example.com'; do
    if (manifest_release_fields "$nf_out") >/dev/null 2>&1; then
      release_failed "selftest: the builder accepted the notes URL '${nf_out}'."
    fi
  done
  echo "${VERDICT_PREFIX}: OK, the builder refuses a non-https, spaced, control-character or empty-host notes URL (expected)."
  manifest_release_fields "https://example.com/notes/1.0.0" | grep -q '"notes_url": "https://example.com/notes/1.0.0",' \
    || release_failed "selftest: the builder refused or mangled a plain https notes URL (inverse control)."
  SOURCE_DATE_EPOCH=86400 manifest_release_fields "https://example.com/n" | grep -q '"released": "1970-01-02",' \
    || release_failed "selftest: SOURCE_DATE_EPOCH did not fix the released date."
  echo "${VERDICT_PREFIX}: OK, a plain https notes URL is written and SOURCE_DATE_EPOCH fixes the released date (inverse control)."
}

# selftest_security_outcome: outcome 11. Called by cmd_selftest and reads its locals (work, keydir, bundle).
selftest_security_outcome() {
  echo
  echo "-- outcome 11: the signed security flag --"
  # The flag decides what the unattended timer may apply, so a network attacker who could flip it
  # would turn a feature release into an unattended one. Each broken copy differs from the untouched
  # bundle in the flag and nothing else; a correctly re-signed security bundle is the inverse control.
  local sec_dir sec_out
  grep -qE '^  "security": false,$' "${bundle}/manifest.json" \
    || release_failed "selftest: the signed manifest lacks the security flag, so the checks below prove nothing."
  sec_dir="${work}/security-tampered"
  cp -r "$bundle" "$sec_dir"
  sed -i 's/"security": false/"security": true/' "${sec_dir}/manifest.json"
  if cmp -s "${bundle}/manifest.json" "${sec_dir}/manifest.json"; then
    release_failed "selftest: outcome 11's sed did not change the security flag, so the tamper step is broken" \
      "and this proves nothing."
  fi
  if sec_out="$(verify_bundle "$sec_dir" "${keydir}/throwaway.pub" 2>&1)"; then
    release_failed "selftest: verify ACCEPTED a manifest whose security flag was edited after signing;" \
      "the flag is not covered by the signature."
  elif grep -q 'signature does NOT verify' <<<"$sec_out"; then
    echo "${VERDICT_PREFIX}: OK, a security flag edited after signing REFUSED by the signature (expected)."
  else
    release_failed "selftest: a tampered security flag was refused for another reason than the signature: ${sec_out}"
  fi
  sec_dir="${work}/security-signed"
  cp -r "$bundle" "$sec_dir"
  sed -i 's/"security": false/"security": true/' "${sec_dir}/manifest.json"
  sign_release_file "${keydir}/throwaway.key" "${sec_dir}/manifest.json" "${sec_dir}/manifest.json.sig"
  if verify_bundle "$sec_dir" "${keydir}/throwaway.pub"; then
    echo "${VERDICT_PREFIX}: OK, a correctly signed security manifest ACCEPTED (inverse control, expected)."
  else
    release_failed "selftest: a correctly signed security bundle was refused; the check refuses" \
      "everything, or the fixture is broken."
  fi
  manifest_security_field 1 | grep -qx '  "security": true,' \
    && manifest_security_field 0 | grep -qx '  "security": false,' \
    || release_failed "selftest: the builder's security line is not the shape the CLI reader scans for."
  if (manifest_security_field 2) >/dev/null 2>&1; then
    release_failed "selftest: the builder accepted a security flag that is neither 0 nor 1."
  fi
  echo "${VERDICT_PREFIX}: OK, the builder writes the security line the CLI reads and refuses any other value."
}
