#!/usr/bin/env bash
# update-selftest-install.sh: the cases about installing a release from verified archives,
# cli_layout_install_release VERSION STAGEDIR [replace]: its preconditions, the sha256 re-check made
# immediately before unpacking, what a clean install leaves on disk, the replace semantics, and the
# refusal of every hostile archive shape on the way in.
#
# Sourced by update-selftest.sh after update-selftest-world.sh and -layout.sh; never executed on its
# own and it sets no shell option. Real signed bundles are fetched through the curl shim, so the
# archives that reach the installer are the ones the download path would have produced.

# stage_release: <version>. Builds (once) a signed stable bundle for the version, fetches and
# verifies it like `maran update` does, and prints the staging directory. The bundle is served from
# its own directory, so one layout can stage several versions.
stage_release() {
  local v="$1" dir
  [ -d "${work}/bundle-${v}" ] || make_bundle "${work}/bundle-${v}/stable" "$v" "stable" "${keydir}/good.key"
  dir="$(new_case_dir)"
  MARAN_SELFTEST_SERVE="${work}/bundle-${v}" fetch_all stable "$dir"
  printf '%s' "$dir"
}

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 14: install - preconditions, the second sha256 check, and what a clean install leaves --"
# ---------------------------------------------------------------------------------------------
fresh_layout
st="$(stage_release 1.0.0)"
chmod 0775 "$st"
expect_refuse "a stage directory that group members can write (0775)" "not writable" cli_layout_install_release 1.0.0 "$st"
chmod 0700 "$st"
ln -s "$st" "${staging_base}/stage-link"
expect_refuse "a stage that is a symlink" "not a plain directory" cli_layout_install_release 1.0.0 "${staging_base}/stage-link"
mv "${st}/agent.tar.gz" "${work}/agent.aside"
expect_refuse "a stage with one archive missing" "agent.tar.gz is missing" cli_layout_install_release 1.0.0 "$st"
mv "${work}/agent.aside" "${st}/agent.tar.gz"
mv "${st}/manifest.json" "${work}/manifest.aside"
expect_refuse "a stage with no manifest to check the archives against" "manifest.json is missing" cli_layout_install_release 1.0.0 "$st"
mv "${work}/manifest.aside" "${st}/manifest.json"
expect_refuse "a stage whose manifest is for another release" "not for release" cli_layout_install_release 1.0.1 "$st"
expect_refuse "an install mode that is not 'replace'" "unknown install mode" cli_layout_install_release 1.0.0 "$st" bogus

for hostile in "../escape" "1.0.0/../x" "1.0" "v1.0.0" "" "1.0.0+build.1" "01.0.0" "legacy-20261104T000000Z"; do
  expect_refuse "installing under the version '${hostile}'" "refusing" cli_layout_install_release "$hostile" "$st"
done
expect_equal "no hostile version created anything beside releases/" "" "$(ls -A "${mar}/releases")"
expect_true "no 'escape' file or directory was created anywhere under the temporary root" test -z "$(find "$mroot" -name 'escape*' | head -1)"

# The second sha256 check. The agent archive is swapped in the stage AFTER cli_release_fetch_artifacts
# checked it, for a different but perfectly valid and clean archive, so only a digest check made
# immediately before unpacking can see the swap. This is the refusing control for that check.
cp "${st}/agent.tar.gz" "${work}/agent.good"
swap_stage="${work}/swap-stage"
install -d "$swap_stage"
echo "swapped in after the download check" > "${swap_stage}/maran-agent"
chmod 0755 "${swap_stage}/maran-agent"
( cd "$swap_stage" && tar -czf "${st}/agent.tar.gz" maran-agent )
expect_refuse "an archive swapped in the stage AFTER its download check (the second sha256 check, before unpacking)" "checksum mismatch" \
  cli_layout_install_release 1.0.0 "$st"
expect_true "the refused install left neither releases/1.0.0 nor a .partial behind" \
  test -z "$(find "${mar}/releases" -mindepth 1 -maxdepth 1 -name '1.0.0*' | head -1)"
cp "${work}/agent.good" "${st}/agent.tar.gz"
: > "$MARAN_SELFTEST_CHOWN_LOG"
expect_accept "the intact stage installs (inverse control for the swap)" cli_layout_install_release 1.0.0 "$st"

rel="${mar}/releases/1.0.0"
expect_true "releases/1.0.0 holds api, agent and frontend" test -f "${rel}/api/Maran.Host" -a -f "${rel}/agent/maran-agent" -a -f "${rel}/frontend/index.html"
expect_true "the api archive's content is this architecture's" grep -q " 1.0.0 ${target_arch} " "${rel}/api/Maran.Host"
expect_true "the entry points are executable" test -x "${rel}/api/Maran.Host" -a -x "${rel}/agent/maran-agent"
expect_true "the staging rename left no releases/1.0.0.partial behind" test ! -e "${mar}/releases/1.0.0.partial"
expect_true "the CLI chowned the release to root:root (not left to the archive's owners)" grep -q 'root:root' "$MARAN_SELFTEST_CHOWN_LOG"
if find "$rel" ! -type l -perm /022 | grep -q .; then
  selftest_failed "the installed release has group or world writable entries (the archive carried a 0666 file): $(find "$rel" ! -type l -perm /022 | head -3 | tr '\n' ' ')"
fi
selftest_pass "no entry of the installed release is group or world writable (the archive carried a 0666 file)"
if find "$rel" -perm /7000 | grep -q .; then
  selftest_failed "the installed release has set-id or sticky entries (the archive carried a 6755 file and a 1777 directory): $(find "$rel" -perm /7000 | head -3 | tr '\n' ' ')"
fi
selftest_pass "no entry of the installed release carries a set-id or sticky bit (the archive carried both)"

sum_before="$(sha_of "${rel}/api/Maran.Host")"
expect_refuse "installing a version whose releases/ directory already exists" "already exists" cli_layout_install_release 1.0.0 "$st"
expect_equal "the refused second install left the installed release untouched" "$sum_before" "$(sha_of "${rel}/api/Maran.Host")"

install -d "${mar}/releases/1.1.0-beta.1.partial/junk"
st_beta="$(stage_release 1.1.0-beta.1)"
expect_accept "installing 1.1.0-beta.1 over a stale .partial a killed run left" cli_layout_install_release 1.1.0-beta.1 "$st_beta"
expect_true "...which is gone, with its junk" test ! -e "${mar}/releases/1.1.0-beta.1.partial"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 15: replace - an abandoned release is replaced; current and PREVIOUS never are --"
# ---------------------------------------------------------------------------------------------
cli_layout_state_write stable 1.0.0 ""
cli_layout_switch 1.0.0
expect_refuse "replacing the release that current names" "current or the previous" cli_layout_install_release 1.0.0 "$st" replace
expect_equal "the refused replace left the running release untouched" "$sum_before" "$(sha_of "${rel}/api/Maran.Host")"
cli_layout_state_write beta 1.1.0-beta.1 1.0.0
cli_layout_switch 1.1.0-beta.1
expect_refuse "replacing the release that PREVIOUS names" "current or the previous" cli_layout_install_release 1.0.0 "$st" replace

install -d "${mar}/releases/1.2.0"
echo "stale" > "${mar}/releases/1.2.0/stale.txt"
st_abandoned="$(stage_release 1.2.0)"
expect_refuse "an existing release directory without 'replace'" "already exists" cli_layout_install_release 1.2.0 "$st_abandoned"
expect_accept "an abandoned release (neither current nor previous) with 'replace'" cli_layout_install_release 1.2.0 "$st_abandoned" replace
expect_true "the abandoned release was replaced: its stale file is gone and the new files are in" \
  test ! -e "${mar}/releases/1.2.0/stale.txt" -a -f "${mar}/releases/1.2.0/api/Maran.Host"
st_fresh="$(stage_release 1.3.0)"
expect_accept "'replace' on a version that is not installed is a plain install (inverse control)" cli_layout_install_release 1.3.0 "$st_fresh" replace
install -d "${work}/victim"
echo "precious" > "${work}/victim/precious"
ln -s "${work}/victim" "${mar}/releases/1.4.0"
st_link="$(stage_release 1.4.0)"
expect_refuse "'replace' over a release path that is a symlink" "not a plain directory" cli_layout_install_release 1.4.0 "$st_link" replace
expect_true "the symlink's target was not touched" test -f "${work}/victim/precious"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 16: hostile archives on the way in - validly signed, checksum right, still refused --"
# ---------------------------------------------------------------------------------------------
fresh_layout
for evil in traversal absolute symlink-up symlink-abs symlink-benign hardlink fifo no-entry not-executable; do
  serve="$(new_serve)"
  make_bundle "${serve}/stable" "1.2.0" "stable" "${keydir}/good.key" "$evil"
  est="$(new_case_dir)"
  MARAN_SELFTEST_SERVE="$serve" fetch_all stable "$est"
  expect_refuse "an api archive with a ${evil} member" "refused|safely|executable" cli_layout_install_release 1.2.0 "$est"
  expect_true "the ${evil} refusal left neither releases/1.2.0 nor a .partial behind" \
    test -z "$(find "${mar}/releases" -mindepth 1 -maxdepth 1 -name '1.2.0*' | head -1)"
  expect_true "the ${evil} member was written nowhere under the release root" \
    test -z "$(find "$mar" \( -name 'selftest-evil-*' -o -name 'escape-link' -o -name 'hardlink-alias' -o -name 'pipe' \) | head -1)"
done
expect_equal "the ../ member was written nowhere (only the fixture's own source file exists)" "1" "$(find "$work" -name 'selftest-evil-traversal' | wc -l | tr -d ' ')"
serve="$(new_serve)"
make_bundle "${serve}/stable" "1.2.0" "stable" "${keydir}/good.key"
est="$(new_case_dir)"
MARAN_SELFTEST_SERVE="$serve" fetch_all stable "$est"
expect_accept "the same version from a clean bundle installs into the very layout the hostile ones failed in (inverse control)" \
  cli_layout_install_release 1.2.0 "$est"
expect_true "the clean install holds its files" test -f "${mar}/releases/1.2.0/api/Maran.Host" -a -f "${mar}/releases/1.2.0/frontend/index.html"
