#!/usr/bin/env bash
# update-selftest-verify.sh: the cases about WHAT may be trusted - release names and their order,
# the signed manifest on disk and over the (shimmed) network, the artifacts' checksums, the offline
# tarball and the outbound curl request.
#
# Sourced by update-selftest.sh after update-selftest-world.sh; never executed on its own and it sets
# no shell option. Everything here drives the library copies in the temporary root.

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 1: the architecture the artifacts are chosen by --"
# ---------------------------------------------------------------------------------------------
for pair in "x86_64:x86_64" "aarch64:aarch64" "arm64:aarch64"; do
  MARAN_SELFTEST_UNAME="${pair%%:*}" run_capture cli_release_arch
  expect_equal "uname -m ${pair%%:*} maps to ${pair##*:}" "${pair##*:}" "$(trim "$RUN_OUT")"
done
MARAN_SELFTEST_UNAME="riscv64" expect_refuse "an architecture the release does not build (riscv64)" "unsupported" cli_release_arch

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 2: release names - strict SemVer, legacy names, and precedence --"
# ---------------------------------------------------------------------------------------------
semver_64="1.0.0-$(repeat_char a 58)"
semver_65="1.0.0-$(repeat_char a 59)"
[ "${#semver_64}" -eq 64 ] && [ "${#semver_65}" -eq 65 ] || selftest_failed "fixture: the 64 and 65 byte versions have the wrong length."
for v in 0.0.1 1.0.0 10.20.30 1.0.0-beta.1 1.0.0-rc.1 1.0.0-0 1.0.0-alpha-1 1.0.0-x.7.z.92 1.2.3-0A.is.legal "$semver_64"; do
  expect_true "is_semver accepts '${v:0:40}'" cli_version_is_semver "$v"
done
for v in "1.0" "v1.0.0" "01.0.0" "1.01.0" "1.0.00" "1.0.0-01" "1.0.0-beta.01" "1.0.0+build.1" "1.0.0-" "1.0.0-beta." \
         "1.0.0-.beta" "1.0.0-beta..1" "1.0.0-alpha_1" "../1.0.0" "1.0.0 " "" "legacy-20261104T000000Z" $'1.0.0\n' "$semver_65"; do
  expect_false "is_semver refuses '${v:0:40}'" cli_version_is_semver "$v"
done
expect_true "is_legacy accepts legacy-20261104T000000Z" cli_version_is_legacy legacy-20261104T000000Z
for v in "legacy-2026" "legacy-20261104T000000" "Legacy-20261104T000000Z" "legacy-20261104T000000Zx" "1.0.0"; do
  expect_false "is_legacy refuses '${v}'" cli_version_is_legacy "$v"
done
expect_true "is_release_name accepts a SemVer version" cli_version_is_release_name 1.4.0-beta.2
expect_true "is_release_name accepts a legacy name" cli_version_is_release_name legacy-20261104T000000Z
for v in "latest" "../x" ""; do
  expect_false "is_release_name refuses '${v}'" cli_version_is_release_name "$v"
done

# A greater than B: status 0 means yes. A legacy-* name ranks below every SemVer version and two
# legacy names rank equal, so neither is greater than the other.
version_table=(
  "1.1.0|1.0.0|yes"
  "1.0.0|1.0.0|no"
  "1.0.0|1.1.0|no"
  "1.10.0|1.9.0|yes"
  "2.0.0|1.99.99|yes"
  "1.0.1|1.0.0|yes"
  "1.0.0|1.0.0-beta.1|yes"
  "1.0.0-beta.1|1.0.0|no"
  "1.0.0-beta.2|1.0.0-beta.1|yes"
  "1.0.0-beta.1|1.0.0-beta.2|no"
  "1.0.0-beta.1|1.0.0-beta.1|no"
  "1.0.0-beta.10|1.0.0-beta.2|yes"
  "1.0.0-beta.2|1.0.0-beta.10|no"
  "1.0.0-beta.1|1.0.0-alpha.1|yes"
  "1.0.0-alpha.1|1.0.0-alpha|yes"
  "1.0.0-alpha|1.0.0-alpha.1|no"
  "1.0.0-alpha.beta|1.0.0-alpha.1|yes"
  "1.0.0-alpha.1|1.0.0-alpha.beta|no"
  "1.1.0-beta.1|1.0.0|yes"
  "1.0.0|1.1.0-beta.1|no"
  "1.0.1-beta.1|1.0.0|yes"
  "1.0.0-rc.1|1.0.0-beta.11|yes"
  "1.0.0-beta.11|1.0.0-beta.2|yes"
  "1.0.0-1|1.0.0-alpha|no"
  "1.0.0-alpha|1.0.0-1|yes"
  "1.0.0-99999999999999999999|1.0.0-9|yes"
  "1.18446744073709551616.0|1.18446744073709551615.0|yes"
  "legacy-20261104T000000Z|1.0.0|no"
  "1.0.0|legacy-20261104T000000Z|yes"
  "0.0.1|legacy-20261104T000000Z|yes"
  "1.0.0-beta.1|legacy-20261104T000000Z|yes"
  "legacy-20261104T000000Z|1.0.0-beta.1|no"
  "legacy-20261105T000000Z|legacy-20261104T000000Z|no"
  "legacy-20261104T000000Z|legacy-20261105T000000Z|no"
  "legacy-20261104T000000Z|legacy-20261104T000000Z|no"
)
yes_rows=0
no_rows=0
for row in "${version_table[@]}"; do
  IFS='|' read -r a b want <<< "$row"
  run_capture cli_version_gt "$a" "$b"
  if [ "$want" = "yes" ]; then
    [ "$RUN_STATUS" -eq 0 ] || selftest_failed "version_gt ${a} ${b}: expected greater, got status ${RUN_STATUS}: ${RUN_OUT}"
    yes_rows=$((yes_rows + 1))
    selftest_pass "${a} is greater than ${b} (control row)"
  else
    [ "$RUN_STATUS" -ne 0 ] || selftest_failed "version_gt ${a} ${b}: ${a} must NOT be greater than ${b}, but was accepted"
    no_rows=$((no_rows + 1))
    selftest_pass "${a} is not greater than ${b} (refusal row)"
  fi
done
[ "$yes_rows" -gt 10 ] && [ "$no_rows" -gt 10 ] || selftest_failed "the precedence table lost one side: ${yes_rows} greater rows, ${no_rows} not-greater rows."
for hostile in "1.0" "v1.1.0" "../../etc" "1.1.0/../x" "" "1.0.0+build.1" "1.0.0 " "01.0.0" "legacy-2026"; do
  expect_refuse "version_gt with the malformed '${hostile}' first (refused as malformed, not merely 'not greater')" \
    "valid release name" cli_version_gt "$hostile" "1.0.0"
done
expect_refuse "version_gt with a path-shaped second argument" "valid release name" cli_version_gt "1.0.0" "../x"
expect_refuse "version_gt with an empty second argument" "valid release name" cli_version_gt "1.0.0" ""

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 3: the manifest on disk - signature, tampering, channel, version, key argument --"
# ---------------------------------------------------------------------------------------------
fresh_layout
good_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$good_dir/"
expect_accept "a manifest signed by the baked key" cli_release_verify_manifest "$good_dir"
run_capture cli_release_manifest_field "${good_dir}/manifest.json" version
expect_equal "the version is read out of the manifest" "1.0.0" "$(trim "$RUN_OUT")"
run_capture cli_release_manifest_field "${good_dir}/manifest.json" channel
expect_equal "the signed channel is read out of the manifest" "stable" "$(trim "$RUN_OUT")"
run_capture cli_release_manifest_field "${good_dir}/manifest.json" "api-${target_arch}.sha256"
expect_equal "an artifact digest is read from its own block (api-${target_arch}.sha256)" \
  "$(sha_of "${serve0}/stable/api-${target_arch}.tar.gz")" "$(trim "$RUN_OUT")"
run_capture cli_release_manifest_field "${good_dir}/manifest.json" "frontend-${other_arch}.url"
expect_equal "an artifact url is read from its own block (frontend-${other_arch}.url)" \
  "${release_base}/stable/frontend-${other_arch}.tar.gz" "$(trim "$RUN_OUT")"
expect_refuse "a manifest key that is not one of the fixed shapes" "bad manifest key" cli_release_manifest_field "${good_dir}/manifest.json" 'version; id'

mode_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$mode_dir/"
chmod 0755 "$mode_dir"
expect_refuse "a work directory other users can read (mode 0755)" "0700" cli_release_verify_manifest "$mode_dir"
link_dir="${staging_base}/link-to-good"
ln -s "$good_dir" "$link_dir"
expect_refuse "a work directory that is a symlink" "real directory" cli_release_verify_manifest "$link_dir"
expect_refuse "a work directory given as a relative path" "absolute" cli_release_verify_manifest "relative/dir"
if [ "$(id -u)" -eq 0 ]; then
  foreign_dir="$(new_case_dir)"
  cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$foreign_dir/"
  chown 65534 "$foreign_dir"
  expect_refuse "a work directory owned by another uid" "owned by root" cli_release_verify_manifest "$foreign_dir"
else
  echo "UNOBSERVED HERE: the refusal of a work directory owned by another uid (this run is not root, and stat reports its own files as root's)."
fi

tamper_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$tamper_dir/"
sed -i '0,/"sha256": "\([0-9a-f]\)/s//"sha256": "x/' "${tamper_dir}/manifest.json"
cmp -s "${serve0}/stable/manifest.json" "${tamper_dir}/manifest.json" && selftest_failed "fixture: the sha256 tamper changed no byte."
expect_refuse "a manifest with one sha256 character changed after signing" "signature|verif" cli_release_verify_manifest "$tamper_dir"

channel_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$channel_dir/"
sed -i 's/"channel": "stable"/"channel": "beta"/' "${channel_dir}/manifest.json"
cmp -s "${serve0}/stable/manifest.json" "${channel_dir}/manifest.json" && selftest_failed "fixture: the channel tamper changed no byte."
expect_refuse "a manifest whose channel field was flipped after signing" "signature|verif" cli_release_verify_manifest "$channel_dir"

version_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$version_dir/"
sed -i 's/"version": "1.0.0"/"version": "9.9.9"/' "${version_dir}/manifest.json"
expect_refuse "a manifest whose version was raised after signing" "signature|verif" cli_release_verify_manifest "$version_dir"

unsigned_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "$unsigned_dir/"
expect_refuse "a manifest with no signature file" "missing" cli_release_verify_manifest "$unsigned_dir"

empty_sig_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "$empty_sig_dir/"
: > "${empty_sig_dir}/manifest.json.sig"
expect_refuse "a manifest with an empty signature file" "signature|verif" cli_release_verify_manifest "$empty_sig_dir"

attacker_dir="$(new_case_dir)"
cp "${serve0}/stable/manifest.json" "$attacker_dir/"
openssl dgst -sha384 -sign "${keydir}/attacker.key" -out "${attacker_dir}/manifest.json.sig" "${attacker_dir}/manifest.json"
expect_refuse "a manifest signed by a key the CLI does not carry" "signature|verif" cli_release_verify_manifest "$attacker_dir"
# The KEYFILE argument is the only way to name another key (the installer passes its package's key).
expect_accept "the same attacker-signed manifest when its own key is named as the KEYFILE argument" \
  cli_release_verify_manifest "$attacker_dir" "${keydir}/attacker.pub"
expect_refuse "the good manifest when the attacker's key is named as the KEYFILE argument" "signature|verif" \
  cli_release_verify_manifest "$good_dir" "${keydir}/attacker.pub"
expect_refuse "a KEYFILE argument that names no file" "signing key" cli_release_verify_manifest "$good_dir" "${work}/no-such-key.pub"
printf '#!/usr/bin/env bash\n. "$1"\nprintf "%%s" "$MARAN_RELEASE_PUBLIC_KEY"\n' > "${work}/key-probe.sh"
expect_equal "an exported MARAN_RELEASE_PUBLIC_KEY does not move the baked key path" "${mar}/cli/keys/release-signing.pub" \
  "$(MARAN_RELEASE_PUBLIC_KEY="${keydir}/attacker.pub" bash "${work}/key-probe.sh" "${libdir}/release-verify.sh")"

mv "${mar}/cli/keys/release-signing.pub" "${work}/key-aside.pub"
expect_refuse "a verification with the baked key missing" "signing key" cli_release_verify_manifest "$good_dir"
ln -s "${work}/key-aside.pub" "${mar}/cli/keys/release-signing.pub"
expect_refuse "a verification with the baked key replaced by a symlink" "signing key" cli_release_verify_manifest "$good_dir"
rm -f "${mar}/cli/keys/release-signing.pub"
mv "${work}/key-aside.pub" "${mar}/cli/keys/release-signing.pub"
expect_accept "the same verification with the key back (inverse control)" cli_release_verify_manifest "$good_dir"

# Signed but unacceptable: the signature proves who wrote it, not that it is a release.
serve="$(new_serve)"
make_bundle "${serve}/stable" "1.0" "stable" "${keydir}/good.key"
d="$(new_case_dir)"
cp "${serve}/stable/manifest.json" "${serve}/stable/manifest.json.sig" "$d/"
expect_refuse "a validly signed manifest whose version is not SemVer (1.0)" "valid version" cli_release_verify_manifest "$d"
make_bundle "${serve}/stable" "01.0.0" "stable" "${keydir}/good.key"
cp "${serve}/stable/manifest.json" "${serve}/stable/manifest.json.sig" "$d/"
expect_refuse "a validly signed manifest whose version has a leading zero (01.0.0)" "valid version" cli_release_verify_manifest "$d"
make_bundle "${serve}/stable" "1.0.0" "nightly" "${keydir}/good.key"
cp "${serve}/stable/manifest.json" "${serve}/stable/manifest.json.sig" "$d/"
expect_refuse "a validly signed manifest naming an unknown channel (nightly)" "unknown channel" cli_release_verify_manifest "$d"
cp "${serve0}/stable/manifest.json" "${serve}/stable/manifest.json"
sed -i "s|${release_base}/stable/|${release_base}/beta/|" "${serve}/stable/manifest.json"
resign "${serve}/stable" "${keydir}/good.key"
cp "${serve}/stable/manifest.json" "${serve}/stable/manifest.json.sig" "$d/"
expect_refuse "a validly signed stable manifest whose artifacts sit under the beta channel" "outside the stable channel" cli_release_verify_manifest "$d"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 4: the manifest over the (shimmed) network - channel, version field, replay, size ceiling --"
# ---------------------------------------------------------------------------------------------
export MARAN_SELFTEST_SERVE="$serve0"
d="$(new_case_dir)"
expect_accept "a stable manifest fetched for stable" cli_release_fetch_manifest stable "$d"
expect_true "the fetched manifest and its signature are on disk" test -f "${d}/manifest.json" -a -f "${d}/manifest.json.sig"
run_capture cli_release_manifest_field "${d}/manifest.json" version
expect_equal "the fetched manifest is the served one (version)" "1.0.0" "$(trim "$RUN_OUT")"
d="$(new_case_dir)"
expect_accept "a beta manifest fetched for beta" cli_release_fetch_manifest beta "$d"
expect_refuse "a channel that is neither stable nor beta" "channel must be" cli_release_fetch_manifest nightly "$(new_case_dir)"

serve="$(new_serve)"
cp "${serve}/beta/manifest.json" "${serve}/beta/manifest.json.sig" "${serve}/stable/"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a beta manifest (valid signature) replayed under the stable URL" "channel" cli_release_fetch_manifest stable "$d"

serve="$(new_serve)"
cp "${serve}/stable/manifest.json" "${serve}/stable/manifest.json.sig" "${serve}/beta/"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a stable manifest (valid signature) served for a beta request" "channel" cli_release_fetch_manifest beta "$d"

serve="$(new_serve)"
sed -i 's/"channel": "stable"/"channel": "beta"/' "${serve}/stable/manifest.json"
cp "${serve}/stable/manifest.json" "${serve}/beta/manifest.json"
cp "${serve}/stable/manifest.json.sig" "${serve}/beta/manifest.json.sig"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a stable manifest whose channel was rewritten to beta, asked for as beta (channel matches, signature does not)" "signature|verif" cli_release_fetch_manifest beta "$d"

serve="$(new_serve)"
make_bundle "${serve}/stable" "1.0.0" "-" "${keydir}/good.key"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "an old-style manifest with no channel field (validly signed) from the network" "channel" cli_release_fetch_manifest stable "$d"
run_capture cli_release_manifest_field "${d}/manifest.json" channel
expect_equal "an absent field is read as nothing, not as an error" "" "$(trim "$RUN_OUT")"
export MARAN_SELFTEST_SERVE="$serve0"
d="$(new_case_dir)"
expect_accept "the same builder WITH the channel field (inverse control)" cli_release_fetch_manifest stable "$d"

serve="$(new_serve)"
sed -i "s|\"url\": \"${release_base}/stable/api-${target_arch}.tar.gz\"|\"url\": \"https://evil.example.test/stable/api-${target_arch}.tar.gz\"|" "${serve}/stable/manifest.json"
grep -q 'evil.example.test' "${serve}/stable/manifest.json" || selftest_failed "fixture: the hostile url edit changed nothing."
resign "${serve}/stable" "${keydir}/good.key"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a validly signed manifest that points an artifact at another host" "outside|channel" cli_release_fetch_manifest stable "$d"

serve="$(new_serve)"
sed -i "s|\"url\": \"${release_base}|\"url\": \"http://releases.maran.innovayse.com|" "${serve}/stable/manifest.json"
grep -q '"url": "http://' "${serve}/stable/manifest.json" || selftest_failed "fixture: the plain-http url edit changed nothing."
resign "${serve}/stable" "${keydir}/good.key"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a validly signed manifest whose artifact urls are plain http" "outside|channel" cli_release_fetch_manifest stable "$d"

serve="$(new_serve)"
rm -f "${serve}/stable/manifest.json.sig"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a manifest the host serves without its signature" "download failed" cli_release_fetch_manifest stable "$d"

serve="$(new_serve)"
openssl dgst -sha384 -sign "${keydir}/attacker.key" -out "${serve}/stable/manifest.json.sig" "${serve}/stable/manifest.json"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a manifest signed by an attacker's key, served as the release" "signature|verif" cli_release_fetch_manifest stable "$d"
# The optional KEYFILE argument is how a first install (CLI key not yet installed) names its package key.
d="$(new_case_dir)"
expect_accept "the same attacker-signed manifest fetched when its own key is named as the KEYFILE argument" \
  cli_release_fetch_manifest stable "$d" "${keydir}/attacker.pub"
export MARAN_SELFTEST_SERVE="$serve0"
d="$(new_case_dir)"
expect_refuse "the good manifest fetched when the attacker's key is named as the KEYFILE argument" "signature|verif" \
  cli_release_fetch_manifest stable "$d" "${keydir}/attacker.pub"

serve="$(new_serve)"
sed -i '0,/"sha256": "\([0-9a-f]\)/s//"sha256": "x/' "${serve}/stable/manifest.json"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "a manifest altered in transit (one sha256 character)" "signature|verif" cli_release_fetch_manifest stable "$d"

# curl's --max-filesize is advisory when the server states no length, so the library measures what
# was written. The shim ignores the flag, which is exactly the server that lies.
export MARAN_SELFTEST_SERVE="$serve0"
dl="$(new_case_dir)"
expect_refuse "a download larger than its byte ceiling (the shim ignores --max-filesize, as a server without a length does)" \
  "exceeds" cli_release_download "${release_base}/stable/manifest.json" "${dl}/over.json" 10 5
expect_true "the over-size download was deleted, not left for a later step to trust" test ! -e "${dl}/over.json"
expect_accept "the same download under a generous ceiling (inverse control)" \
  cli_release_download "${release_base}/stable/manifest.json" "${dl}/ok.json" 10 1048576

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 5: the artifacts - checksum after download, the right architecture --"
# ---------------------------------------------------------------------------------------------
export MARAN_SELFTEST_SERVE="$serve0"
art_dir="$(new_case_dir)"
expect_accept "the manifest and its three archives fetched" fetch_all stable "$art_dir"
want_hashes="$(for c in api agent frontend; do sha_of "${serve0}/stable/${c}-${target_arch}.tar.gz"; done | sort)"
got_hashes="$(find "$art_dir" -maxdepth 1 -name '*.tar.gz' -exec sha256sum {} + | awk '{print $1}' | sort)"
expect_equal "the downloaded archives are exactly this architecture's three (${target_arch}), not the other's" "$want_hashes" "$got_hashes"

serve="$(new_serve)"
printf 'TAMPERED' >> "${serve}/stable/agent-${target_arch}.tar.gz"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_accept "the manifest of a bundle whose agent archive was altered still verifies (the signature covers the manifest only)" cli_release_fetch_manifest stable "$d"
expect_refuse "an agent archive altered after signing" "checksum mismatch" cli_release_fetch_artifacts "${d}/manifest.json" "$d"

serve="$(new_serve)"
# A manifest that is correctly SIGNED but whose digest does not describe the archive (a build defect,
# or a host serving yesterday's archive under today's manifest): the checksum is what refuses it.
sed -i "0,/\"sha256\": \"[0-9a-f]\{64\}\"/s//\"sha256\": \"$(repeat_char 0 64)\"/" "${serve}/stable/manifest.json"
resign "${serve}/stable" "${keydir}/good.key"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_accept "the manifest of that bundle (its signature is good)" cli_release_fetch_manifest stable "$d"
expect_refuse "an archive whose bytes do not match the digest in a validly signed manifest" "checksum mismatch" cli_release_fetch_artifacts "${d}/manifest.json" "$d"

serve="$(new_serve)"
rm -f "${serve}/stable/frontend-${target_arch}.tar.gz"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_refuse "an archive the host does not serve (404)" "download failed" fetch_all stable "$d"

serve="$(new_serve)"
# Damage only the OTHER architecture's archive: this host never downloads it, so nothing may refuse.
printf 'TAMPERED' >> "${serve}/stable/agent-${other_arch}.tar.gz"
export MARAN_SELFTEST_SERVE="$serve"
d="$(new_case_dir)"
expect_accept "a bundle whose damage is only in the other architecture's archive (inverse control)" fetch_all stable "$d"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 6: the offline tarball path (no network at all) --"
# ---------------------------------------------------------------------------------------------
make_offline "${serve0}/stable" "${work}/offline-good.tar.gz"
d="$(new_case_dir)"
: > "$MARAN_SELFTEST_INSTALL_LOG"
: > "$MARAN_SELFTEST_TAR_LOG"
expect_accept "a signed offline tarball" cli_release_verify_offline "${work}/offline-good.tar.gz" "$d"
expect_true "the offline tarball was unpacked for the caller (manifest and archives present)" \
  test -f "${d}/manifest.json" -a -f "${d}/manifest.json.sig" -a -f "${d}/api.tar.gz" -a -f "${d}/agent.tar.gz" -a -f "${d}/frontend.tar.gz"
expect_true "the private copy of the tarball is deleted once unpacked" test ! -e "${d}/offline-bundle.tar.gz"
expect_equal "the work directory is still mode 0700 after a bundle without a ./ member" "700" "$(stat -c %a "$d")"
# Single copy: the operator's file is opened ONCE (the install that copies it) and every tar call
# names the root-owned copy, never the original, so a swap of the original after the copy changes
# nothing that was read. The blind spot: this audits argv, it cannot race the file.
orig_installs="$(grep -cF -- "${work}/offline-good.tar.gz" "$MARAN_SELFTEST_INSTALL_LOG" || true)"
orig_tar_calls="$(grep -cF -- "${work}/offline-good.tar.gz" "$MARAN_SELFTEST_TAR_LOG" || true)"
copy_tar_calls="$(grep -cF -- "${d}/offline-bundle.tar.gz" "$MARAN_SELFTEST_TAR_LOG" || true)"
expect_equal "the operator's tarball is named by exactly one install call (a single open)" "1" "$orig_installs"
expect_equal "no tar call names the operator's tarball: it was never listed or extracted in place" "0" "$orig_tar_calls"
expect_true "the tar calls that vetted and extracted named the private copy (positive control for the count above)" test "$copy_tar_calls" -ge 3

# `tar -czf x -C dir .` carries a leading ./ member whose mode GNU tar may apply to the directory it
# extracts into; the source directory is 0777 so a loosened work directory would be visible.
dot_src="$(mktemp -d "${work}/dotsrc.XXXXXX")"
cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$dot_src/"
for c in api agent frontend; do cp "${serve0}/stable/${c}-${target_arch}.tar.gz" "${dot_src}/${c}.tar.gz"; done
chmod 0777 "$dot_src"
tar -czf "${work}/offline-dot.tar.gz" -C "$dot_src" .
tar -tvzf "${work}/offline-dot.tar.gz" | grep -qE -- '^drwxrwxrwx .* \./$' \
  || selftest_failed "fixture: the dot tarball does not carry a 0777 ./ member - the mode check would be vacuous."
d="$(new_case_dir)"
expect_accept "an offline tarball built with 'tar -czf x -C dir .' (a ./ member)" cli_release_verify_offline "${work}/offline-dot.tar.gz" "$d"
expect_equal "the work directory is still mode 0700 after a ./ member with mode 0777 was extracted into it" "700" "$(stat -c %a "$d")"

serve="$(new_serve)"
printf 'TAMPERED' >> "${serve}/stable/api-${target_arch}.tar.gz"
make_offline "${serve}/stable" "${work}/offline-tampered.tar.gz"
d="$(new_case_dir)"
expect_refuse "an offline tarball whose api archive changed after signing" "checksum mismatch" \
  cli_release_verify_offline "${work}/offline-tampered.tar.gz" "$d"

make_offline "${serve0}/stable" "${work}/offline-unsigned.tar.gz" skip-sig
d="$(new_case_dir)"
expect_refuse "an offline tarball with no signature" "missing" cli_release_verify_offline "${work}/offline-unsigned.tar.gz" "$d"
# A signature already sitting in the work directory must not stand in for the tarball's own.
d="$(new_case_dir)"
cp "${serve0}/stable/manifest.json.sig" "$d/"
expect_refuse "an unsigned offline tarball while a valid signature already sits in the work directory" "missing" \
  cli_release_verify_offline "${work}/offline-unsigned.tar.gz" "$d"

serve="$(new_serve)"
openssl dgst -sha384 -sign "${keydir}/attacker.key" -out "${serve}/stable/manifest.json.sig" "${serve}/stable/manifest.json"
make_offline "${serve}/stable" "${work}/offline-attacker.tar.gz"
d="$(new_case_dir)"
expect_refuse "an offline tarball signed by an attacker's key" "signature|verif" \
  cli_release_verify_offline "${work}/offline-attacker.tar.gz" "$d"
d="$(new_case_dir)"
expect_accept "the same attacker-signed offline tarball when its own key is named as the KEYFILE argument" \
  cli_release_verify_offline "${work}/offline-attacker.tar.gz" "$d" "${keydir}/attacker.pub"
d="$(new_case_dir)"
expect_refuse "the good offline tarball when the attacker's key is named as the KEYFILE argument" "signature|verif" \
  cli_release_verify_offline "${work}/offline-good.tar.gz" "$d" "${keydir}/attacker.pub"

# Hostile members: the tarball is signed and checksummed like any other, so only the member listing
# can refuse it. Each fixture is checked to carry the member it is about (a vacuity guard).
hostile_offline="$(mktemp -d "${work}/offline-hostile.XXXXXX")"
cp "${serve0}/stable/manifest.json" "${serve0}/stable/manifest.json.sig" "$hostile_offline/"
for c in api agent frontend; do cp "${serve0}/stable/${c}-${target_arch}.tar.gz" "${hostile_offline}/${c}.tar.gz"; done
echo evil > "${work}/selftest-evil-offline"
( cd "$hostile_offline" && tar -P -czf "${work}/offline-dotdot.tar.gz" manifest.json manifest.json.sig api.tar.gz agent.tar.gz frontend.tar.gz ../selftest-evil-offline )
tar -tPzf "${work}/offline-dotdot.tar.gz" | grep -qxF '../selftest-evil-offline' || selftest_failed "fixture: the offline ../ tarball does not carry its member."
d="$(new_case_dir)"
expect_refuse "an offline tarball with a ../ member" "member refused" cli_release_verify_offline "${work}/offline-dotdot.tar.gz" "$d"
( cd "$hostile_offline" && tar -P -czf "${work}/offline-absolute.tar.gz" manifest.json manifest.json.sig api.tar.gz agent.tar.gz frontend.tar.gz "${work}/selftest-evil-offline" )
tar -tPzf "${work}/offline-absolute.tar.gz" | grep -qxF "${work}/selftest-evil-offline" || selftest_failed "fixture: the offline absolute tarball does not carry its member."
d="$(new_case_dir)"
expect_refuse "an offline tarball with an absolute-path member" "member refused" cli_release_verify_offline "${work}/offline-absolute.tar.gz" "$d"
ln -s /etc/passwd "${hostile_offline}/link-member"
( cd "$hostile_offline" && tar -czf "${work}/offline-symlink.tar.gz" manifest.json manifest.json.sig api.tar.gz agent.tar.gz frontend.tar.gz link-member )
tar -tvzf "${work}/offline-symlink.tar.gz" | grep -q -- '-> /etc/passwd' || selftest_failed "fixture: the offline symlink tarball does not carry its link."
d="$(new_case_dir)"
expect_refuse "an offline tarball with a symlink member" "symlink" cli_release_verify_offline "${work}/offline-symlink.tar.gz" "$d"
expect_equal "none of the hostile offline tarballs wrote its member anywhere but the fixture's own source" "1" \
  "$(find "$work" -name 'selftest-evil-offline' | wc -l | tr -d ' ')"

# The directory the tarball is read from must not be one another uid can write into.
open_parent="$(mktemp -d "${work}/open.XXXXXX")"
cp "${work}/offline-good.tar.gz" "${open_parent}/"
chmod 0777 "$open_parent"
d="$(new_case_dir)"
expect_refuse "an offline tarball whose directory is world writable (mode 0777)" "group or world writable" \
  cli_release_verify_offline "${open_parent}/offline-good.tar.gz" "$d"
chmod 0755 "$open_parent"
d="$(new_case_dir)"
expect_accept "the same tarball once its directory is mode 0755 (inverse control)" \
  cli_release_verify_offline "${open_parent}/offline-good.tar.gz" "$d"
if [ "$(id -u)" -eq 0 ]; then
  chown 65534 "$open_parent"
  d="$(new_case_dir)"
  expect_refuse "an offline tarball whose directory is owned by another uid" "owned by root" \
    cli_release_verify_offline "${open_parent}/offline-good.tar.gz" "$d"
else
  echo "UNOBSERVED HERE: the refusal of an offline tarball in a directory owned by another uid (this run is not root)."
fi

ln -s "${work}/offline-good.tar.gz" "${work}/offline-link.tar.gz"
d="$(new_case_dir)"
expect_refuse "an offline tarball that is a symlink" "not found" cli_release_verify_offline "${work}/offline-link.tar.gz" "$d"
expect_refuse "an offline tarball that does not exist" "not found" cli_release_verify_offline "${work}/offline-missing.tar.gz" "$d"
truncate -s 2147483649 "${work}/offline-huge.tar.gz"
expect_refuse "an offline tarball larger than the 2 GiB total ceiling (refused before a byte is copied)" "larger than" \
  cli_release_verify_offline "${work}/offline-huge.tar.gz" "$d"
rm -f -- "${work}/offline-huge.tar.gz"
echo "this is not a gzip tar" > "${work}/offline-garbage.tar.gz"
expect_refuse "an offline tarball that is not an archive at all" "refused" cli_release_verify_offline "${work}/offline-garbage.tar.gz" "$d"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 7: the outbound request, audited from the shim's record of its argv --"
# ---------------------------------------------------------------------------------------------
calls="$(wc -l < "$MARAN_SELFTEST_CURL_LOG" | tr -d ' ')"
[ "$calls" -gt 10 ] || selftest_failed "the curl shim recorded only ${calls} call(s): the argv audit below would be vacuous."
selftest_pass "the shim recorded ${calls} curl calls (positive control for the argv audit)"
# audit: <label> <pattern every call must match or empty> <pattern no call may match or empty>.
audit() {
  local label="$1" must="$2" mustnot="$3" line
  if [ -n "$must" ]; then
    while IFS= read -r line; do
      printf '%s' "$line" | grep -qE -- "$must" || selftest_failed "${label}: a curl call lacks /${must}/: ${line}"
    done < "$MARAN_SELFTEST_CURL_LOG"
  fi
  if [ -n "$mustnot" ] && grep -qE -- "$mustnot" "$MARAN_SELFTEST_CURL_LOG"; then
    selftest_failed "${label}: a curl call matches /${mustnot}/: $(grep -E -- "$mustnot" "$MARAN_SELFTEST_CURL_LOG" | head -1)"
  fi
  selftest_pass "$label"
}
audit "every curl call pins https only (--proto =https)" '--proto[ =]+=?https' ""
audit "every curl call fails on an HTTP error (--fail)" '(^| )--fail( |$)' ""
audit "every curl call has a connect timeout" '--connect-timeout' ""
audit "every curl call has a maximum time" '--max-time' ""
audit "every curl call requires TLS 1.2 or newer" '--tlsv1\.2' ""
audit "every curl call limits redirects to three" '--max-redirs 3( |$)' ""
audit "every curl call ignores the user's curl configuration (--disable)" '(^| )--disable( |$)' ""
audit "no curl call disables certificate verification (-k, --insecure)" "" '(^| )(-k|--insecure|-[a-zA-Z]*k[a-zA-Z]*)( |$)'
audit "no curl call carries a query string or a custom header (no identifier leaves the host)" "" '\?|(^| )(-H|--header)( |=)'
# The audit patterns are themselves proven able to hit: each one is matched against a line it must match.
printf '%s' "--disable --fail --proto =https --tlsv1.2 --max-redirs 3 --connect-timeout 15 --max-time 60 --url https://h/stable/manifest.json" \
  | grep -qE -- '--proto[ =]+=?https' \
  && printf '%s' "--max-redirs 3 --url x" | grep -qE -- '--max-redirs 3( |$)' \
  && printf '%s' "--disable --fail" | grep -qE -- '(^| )--disable( |$)' \
  || selftest_failed "the --proto, --max-redirs or --disable pattern cannot match a line carrying it."
printf '%s' "-k --insecure https://h/x?id=1" | grep -qE -- '(^| )(-k|--insecure)( |$)' \
  && printf '%s' "https://h/x?id=1" | grep -qE -- '\?' || selftest_failed "the insecure or query patterns cannot hit."
selftest_pass "the audit patterns match what they hunt (positive controls)"
