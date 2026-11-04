#!/usr/bin/env bash
# update-selftest-layout.sh: the cases about WHERE a release lives - release.state, the `current`
# link, switching and the previous release, pruning, the interrupted-update check, and the adoption
# of a host installed before the release layout existed.
#
# Sourced by update-selftest.sh after update-selftest-world.sh; never executed on its own and it sets
# no shell option. Releases here are small synthetic directories (mk_release): what is under test is
# the layout logic, not the archives, which the install and unpack parts cover.

# mk_release: <name>. A release directory with an api component holding a marker that names it, so a
# path read through the static links shows which release answered.
mk_release() {
  install -d "${mar}/releases/${1}/api"
  echo "release ${1}" > "${mar}/releases/${1}/api/marker.txt"
}

# write_state_file: <content> [mode]. A release.state nobody wrote through the CLI.
write_state_file() {
  printf '%s' "$1" > "${etc}/release.state"
  chmod "${2:-0644}" "${etc}/release.state"
}

# plant_legacy: a host the old installer made: api, agent and frontend are real directories.
plant_legacy() {
  local c
  rm -rf -- "$mar" "$etc"
  install -d "${mar}/cli/keys" "$etc"
  install -m 0644 "${keydir}/good.pub" "${mar}/cli/keys/release-signing.pub"
  for c in api agent frontend; do
    install -d "${mar}/${c}"
    echo "legacy ${c} content" > "${mar}/${c}/marker.txt"
  done
}

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 8: release.state - writing, reading, and refusing a line that is not one --"
# ---------------------------------------------------------------------------------------------
fresh_layout
mk_release 1.0.0
: > "$MARAN_SELFTEST_CHOWN_LOG"
expect_accept "writing the state of a stable install" cli_layout_state_write stable 1.0.0 ""
expect_true "release.state is a plain file under /etc/maran" test -f "${etc}/release.state"
expect_true "release.state names the channel" grep -qx 'CHANNEL=stable' "${etc}/release.state"
expect_true "release.state names the version" grep -qx 'VERSION=1.0.0' "${etc}/release.state"
expect_true "release.state carries an empty PREVIOUS" grep -qx 'PREVIOUS=' "${etc}/release.state"
expect_equal "release.state is mode 0644" "644" "$(stat -c %a "${etc}/release.state")"
expect_true "the temporary state file was handed to root:root before the rename" \
  grep -qE 'root:root -- .*\.release\.state\.' "$MARAN_SELFTEST_CHOWN_LOG"
run_capture cli_layout_state_read VERSION
expect_equal "state_read VERSION" "1.0.0" "$(trim "$RUN_OUT")"
run_capture cli_layout_state_read CHANNEL
expect_equal "state_read CHANNEL" "stable" "$(trim "$RUN_OUT")"
run_capture cli_layout_state_read PREVIOUS
expect_equal "state_read PREVIOUS (empty)" "" "$(trim "$RUN_OUT")"
expect_refuse "state_read of a key that is not one of the three" "unknown release state key" cli_layout_state_read EVIL
cp "${etc}/release.state" "${work}/state.before"
expect_refuse "a state write with a channel outside stable and beta" "channel" cli_layout_state_write nightly 1.0.0 ""
expect_refuse "a state write with a version that is not SemVer" "version" cli_layout_state_write stable "../1.0.0" ""
expect_refuse "a state write with a newline in the version (a second line would be a second setting)" "version" \
  cli_layout_state_write stable $'1.0.0\nCHANNEL=beta' ""
expect_refuse "a state write with a hostile PREVIOUS" "previous" cli_layout_state_write stable 1.0.0 $'1.0.0\nVERSION=9.9.9'
expect_refuse "a state write with a malformed legacy name" "version" cli_layout_state_write stable legacy-2026 ""
expect_true "the refused writes left release.state byte for byte as it was" cmp -s "${work}/state.before" "${etc}/release.state"
expect_equal "no temporary file is left in /etc/maran" "release.state" "$(ls -A "$etc" | tr '\n' ' ' | sed 's/ $//')"
expect_accept "a state write for beta with a previous version (inverse control)" cli_layout_state_write beta 1.1.0-beta.1 1.0.0
run_capture cli_layout_previous_version
expect_equal "previous_version answers PREVIOUS" "1.0.0" "$(trim "$RUN_OUT")"
expect_accept "a state write whose VERSION is a legacy name" cli_layout_state_write stable legacy-20261104T000000Z ""
expect_accept "a state write whose PREVIOUS is a legacy name" cli_layout_state_write stable 1.0.0 legacy-20261104T000000Z
cli_layout_state_write stable 1.0.0 "0.9.9"
expect_refuse "a PREVIOUS naming a release that is no longer on disk" "no longer on disk" cli_layout_previous_version
cli_layout_state_write stable 1.0.0 ""
run_capture cli_layout_previous_version
expect_equal "previous_version with no PREVIOUS prints nothing" "" "$(trim "$RUN_OUT")"

# A state file nobody wrote through the CLI is parsed, never sourced, and refused when it is not ours.
write_state_file $'CHANNEL=stable\nVERSION=1.0.0\nPREVIOUS=\n'
run_capture cli_layout_state_read VERSION
expect_equal "a hand-written but well-formed release.state reads (inverse control)" "1.0.0" "$(trim "$RUN_OUT")"
write_state_file 'CHANNEL=stable
VERSION=1.0.0
PREVIOUS='
run_capture cli_layout_state_read VERSION
expect_equal "a release.state without a final newline reads (inverse control)" "1.0.0" "$(trim "$RUN_OUT")"
write_state_file $'CHANNEL=stable\nVERSION=1.0.0\nPREVIOUS=\nEVIL=$(id)\n'
expect_refuse "a release.state with a line that is not CHANNEL, VERSION or PREVIOUS" "unrecognised" cli_layout_state_read VERSION
write_state_file $'CHANNEL=stable\n\nVERSION=1.0.0\n'
expect_refuse "a release.state with a blank line" "unrecognised" cli_layout_state_read VERSION
write_state_file $'CHANNEL=stable\nVERSION=1.0.0\r\nPREVIOUS=\n'
expect_refuse "a release.state whose VERSION ends in a carriage return" "invalid VERSION" cli_layout_state_read VERSION
write_state_file $'VERSION=1.0.0\nVERSION=1.0.1\n'
expect_refuse "a release.state that repeats VERSION with another value" "repeats" cli_layout_state_read VERSION
write_state_file $'VERSION=1.0.0\nVERSION=1.0.0\n'
expect_refuse "a release.state that repeats VERSION with the same value" "repeats" cli_layout_state_read VERSION
write_state_file $'CHANNEL=stable\nCHANNEL=beta\nVERSION=1.0.0\n'
expect_refuse "a release.state that repeats CHANNEL" "repeats" cli_layout_state_read CHANNEL
write_state_file $'CHANNEL=nightly\nVERSION=1.0.0\nPREVIOUS=\n'
expect_refuse "a release.state with a channel outside the allow-list" "invalid CHANNEL" cli_layout_state_read VERSION
write_state_file $'CHANNEL=stable\nVERSION=../../x\nPREVIOUS=\n'
expect_refuse "a release.state whose VERSION is path-shaped" "invalid VERSION" cli_layout_state_read VERSION
write_state_file $'CHANNEL=stable\nVERSION=1.0.0\nPREVIOUS=\n' 0664
expect_refuse "a release.state that group members can write (0664)" "not writable" cli_layout_state_read VERSION
write_state_file $'CHANNEL=stable\nVERSION=1.0.0\nPREVIOUS=\n' 0666
expect_refuse "a release.state that anyone can write (0666)" "not writable" cli_layout_state_read VERSION
write_state_file $'CHANNEL=stable\nVERSION=1.0.0\nPREVIOUS=\n' 0600
run_capture cli_layout_state_read VERSION
expect_equal "a release.state of mode 0600 reads (inverse control)" "1.0.0" "$(trim "$RUN_OUT")"
rm -f "${etc}/release.state"
ln -s "${work}/state.before" "${etc}/release.state"
expect_refuse "a release.state that is a symlink to a good file" "plain file" cli_layout_state_read VERSION
rm -f "${etc}/release.state"
ln -s "${work}/no-such-state" "${etc}/release.state"
expect_refuse "a release.state that is a dangling symlink" "plain file" cli_layout_state_read VERSION
rm -f "${etc}/release.state"
run_capture cli_layout_state_read VERSION
expect_equal "no release.state at all: prints nothing and succeeds" "0:" "${RUN_STATUS}:$(trim "$RUN_OUT")"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 9: current_version - empty only when there is no current at all --"
# ---------------------------------------------------------------------------------------------
fresh_layout
mk_release 1.0.0
install -d "${work}/victim"
run_capture cli_layout_current_version
expect_equal "no current symlink: prints nothing and succeeds" "0:" "${RUN_STATUS}:$(trim "$RUN_OUT")"
install -d "${mar}/current"
expect_refuse "a current that is a real directory" "not a symlink" cli_layout_current_version
rmdir "${mar}/current"
ln -s ../etc "${mar}/current"
expect_refuse "a current pointing outside releases/" "unexpected target" cli_layout_current_version
rm -f "${mar}/current"
ln -s "${mar}/releases/1.0.0" "${mar}/current"
expect_refuse "a current with an absolute target" "unexpected target" cli_layout_current_version
rm -f "${mar}/current"
install -d "${mar}/releases/not-a-version"
ln -s releases/not-a-version "${mar}/current"
expect_refuse "a current naming a directory that is not a release name" "unexpected target" cli_layout_current_version
rm -f "${mar}/current"
ln -s releases/9.9.9 "${mar}/current"
expect_refuse "a dangling current (the release it names is gone)" "missing" cli_layout_current_version
rm -f "${mar}/current"
ln -s "${work}/victim" "${mar}/releases/2.0.0"
ln -s releases/2.0.0 "${mar}/current"
expect_refuse "a current naming a release that is itself a symlink" "missing" cli_layout_current_version
rm -f "${mar}/current"
ln -s releases/1.0.0 "${mar}/current"
run_capture cli_layout_current_version
expect_equal "a current naming an installed release" "1.0.0" "$(trim "$RUN_OUT")"
rm -f "${mar}/current"
mk_release legacy-20261104T000000Z
ln -s releases/legacy-20261104T000000Z "${mar}/current"
run_capture cli_layout_current_version
expect_equal "a current naming an adopted legacy release" "legacy-20261104T000000Z" "$(trim "$RUN_OUT")"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 10: switch and previous, through the static symlinks the units resolve --"
# ---------------------------------------------------------------------------------------------
fresh_layout
mk_release 1.0.0
mk_release 1.1.0-beta.1
expect_accept "switching current to 1.0.0" cli_layout_switch 1.0.0
expect_true "current is a symlink" test -L "${mar}/current"
expect_equal "current points at releases/1.0.0 (a relative link, so the tree can move)" "releases/1.0.0" "$(readlink "${mar}/current")"
run_capture cli_layout_current_version
expect_equal "current_version resolves current" "1.0.0" "$(trim "$RUN_OUT")"
expect_equal "a unit's path /usr/local/maran/api/... now reads the 1.0.0 api" "release 1.0.0" "$(cat "${mar}/api/marker.txt")"
sum_before="$(sha_of "${mar}/releases/1.0.0/api/marker.txt")"
cli_layout_state_write beta 1.1.0-beta.1 1.0.0
expect_accept "switching current to 1.1.0-beta.1" cli_layout_switch 1.1.0-beta.1
expect_equal "the unit path now reads the beta api" "release 1.1.0-beta.1" "$(cat "${mar}/api/marker.txt")"
run_capture cli_layout_current_version
expect_equal "current_version after the switch" "1.1.0-beta.1" "$(trim "$RUN_OUT")"
run_capture cli_layout_previous_version
expect_equal "previous_version names the release before the switch" "1.0.0" "$(trim "$RUN_OUT")"
expect_equal "the old release is still on disk, byte for byte" "$sum_before" "$(sha_of "${mar}/releases/1.0.0/api/marker.txt")"

expect_refuse "switching to a release that is not installed" "not on disk" cli_layout_switch 9.9.9
expect_refuse "switching to a path-shaped version" "refusing" cli_layout_switch "../../etc"
expect_refuse "switching to a version with a path inside it" "refusing" cli_layout_switch "1.0.0/../1.1.0-beta.1"
expect_refuse "switching to the empty version" "refusing" cli_layout_switch ""
ln -s "${work}/victim" "${mar}/releases/3.0.0"
expect_refuse "switching to a release directory that is a symlink" "not on disk" cli_layout_switch 3.0.0
run_capture cli_layout_current_version
expect_equal "the refused switches left current where it was" "1.1.0-beta.1" "$(trim "$RUN_OUT")"
expect_accept "switching BACK to the previous release (the rollback move, inverse control)" cli_layout_switch 1.0.0
run_capture cli_layout_current_version
expect_equal "current_version after the switch back" "1.0.0" "$(trim "$RUN_OUT")"
leftovers="$(ls -A "$mar" | grep -vxE 'api|agent|frontend|current|releases|cli' || true)"
expect_equal "the switches left no temporary link beside current" "" "$leftovers"
mk_release legacy-20261104T000000Z
expect_accept "switching to an adopted legacy release (a release name wherever one is valid)" cli_layout_switch legacy-20261104T000000Z
run_capture cli_layout_current_version
expect_equal "current_version names the legacy release" "legacy-20261104T000000Z" "$(trim "$RUN_OUT")"
rm -f "${mar}/api"
cli_layout_switch 1.0.0
expect_equal "a missing static link is created by the switch" "current/api" "$(readlink "${mar}/api")"
rm -f "${mar}/api"
install -d "${mar}/api"
expect_refuse "a static path that is a real directory (the layout was never adopted)" "real directory" cli_layout_switch 1.0.0
rm -rf "${mar}/api"
ln -s current/api "${mar}/api"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 11: prune keeps the 3 newest releases plus current and previous --"
# ---------------------------------------------------------------------------------------------
fresh_layout
mk_release 1.0.0
cli_layout_state_write stable 1.0.0 ""
expect_refuse "pruning with no current release" "without a current release" cli_layout_prune
expect_true "the refused prune removed nothing" test -d "${mar}/releases/1.0.0"

fresh_layout
for v in 1.0.0 1.0.1 1.0.2 1.0.3 1.0.4 1.0.5 1.0.10 legacy-20261101T000000Z; do mk_release "$v"; done
install -d "${mar}/releases/keepme"
install -d "${work}/victim"
echo "precious" > "${work}/victim/precious"
ln -s "${work}/victim" "${mar}/releases/0.9.0"
echo "not a directory" > "${mar}/releases/1.0.9"
ln -s releases/1.0.0 "${mar}/current"
cli_layout_state_write stable 1.0.0 1.0.1
expect_accept "pruning with current on the OLDEST release and previous on the second oldest" cli_layout_prune
# The three newest by precedence are 1.0.10, 1.0.5 and 1.0.4 (not 1.0.5, 1.0.4, 1.0.3: 1.0.10 is
# newer than 1.0.5, which a lexical sort gets wrong).
for v in 1.0.0 1.0.1 1.0.4 1.0.5 1.0.10; do
  expect_true "release ${v} survived (current, previous or among the 3 newest)" test -d "${mar}/releases/${v}"
done
for v in 1.0.2 1.0.3 legacy-20261101T000000Z; do
  expect_true "release ${v} (neither current, previous nor among the 3 newest) was removed" test ! -e "${mar}/releases/${v}"
done
expect_true "a directory whose name is not a version was never touched" test -d "${mar}/releases/keepme"
expect_true "a symlink named like a version never made prune follow it out of releases/" test -f "${work}/victim/precious"
expect_true "...and the symlink itself was left alone" test -L "${mar}/releases/0.9.0"
expect_true "a plain file named like a version was left alone" test -f "${mar}/releases/1.0.9"
expect_equal "the release current points at is intact after the prune" "release 1.0.0" "$(cat "${mar}/releases/1.0.0/api/marker.txt")"

fresh_layout
for v in 1.0.0 1.0.1 1.0.2; do mk_release "$v"; done
ln -s releases/1.0.2 "${mar}/current"
cli_layout_state_write stable 1.0.2 1.0.1
expect_accept "pruning when only three releases exist (inverse control)" cli_layout_prune
for v in 1.0.0 1.0.1 1.0.2; do
  expect_true "with three releases ${v} stays (a prune that deletes everything fails here)" test -d "${mar}/releases/${v}"
done

# Pre-release precedence decides which are the newest: 2.0.0 > 2.0.0-rc.1 > 2.0.0-beta.2 > 2.0.0-alpha.1.
fresh_layout
for v in 2.0.0 2.0.0-rc.1 2.0.0-beta.2 2.0.0-alpha.1 1.9.0 1.8.0 1.7.0; do mk_release "$v"; done
ln -s releases/1.7.0 "${mar}/current"
cli_layout_state_write stable 1.7.0 ""
expect_accept "pruning with pre-releases among the releases" cli_layout_prune
for v in 1.7.0 2.0.0 2.0.0-rc.1 2.0.0-beta.2; do
  expect_true "release ${v} survived (current or among the 3 newest by SemVer precedence)" test -d "${mar}/releases/${v}"
done
for v in 2.0.0-alpha.1 1.9.0 1.8.0; do
  expect_true "release ${v} was removed" test ! -e "${mar}/releases/${v}"
done

# A legacy name is a release name wherever one is valid: kept as current, ranked below every SemVer one.
fresh_layout
for v in legacy-20261104T000000Z 1.0.0 1.0.1 1.0.2 1.0.3 1.0.4; do mk_release "$v"; done
ln -s releases/legacy-20261104T000000Z "${mar}/current"
cli_layout_state_write stable legacy-20261104T000000Z ""
expect_accept "pruning with an adopted legacy release as current" cli_layout_prune
expect_true "the legacy release that current names survived" test -d "${mar}/releases/legacy-20261104T000000Z"
expect_true "the oldest SemVer release was removed beside it" test ! -e "${mar}/releases/1.0.1"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 12: an interrupted update is refused by a returned status, never repaired --"
# ---------------------------------------------------------------------------------------------
fresh_layout
expect_accept "a host with no current and no release.state (nothing installed yet)" cli_layout_check_consistent
mk_release 1.0.0
mk_release 1.1.0-beta.1
cli_layout_state_write stable 1.0.0 ""
cli_layout_switch 1.0.0
expect_accept "an untouched install: current and release.state agree" cli_layout_check_consistent
install -d "${mar}/releases/1.3.0.partial" "${mar}/releases/junk.partial"
echo half > "${mar}/releases/1.3.0.partial/half-written"
expect_accept "a stale releases/<version>.partial from a killed unpack does not block the next run" cli_layout_check_consistent
expect_true "...and the check removed it" test ! -e "${mar}/releases/1.3.0.partial"
expect_true "a .partial whose name is not a version is left alone" test -d "${mar}/releases/junk.partial"

# What update.sh leaves when it is killed after the switch: PREVIOUS written, `current` moved,
# VERSION still the old one.
cli_layout_state_write stable 1.0.0 1.0.0
cli_layout_switch 1.1.0-beta.1
cp "${etc}/release.state" "${work}/state.interrupted"
expect_returns_refusal "killed after the switch (current 1.1.0-beta.1, release.state 1.0.0), the consistency check" "interrupted" cli_layout_check_consistent
for fragment in "1.1.0-beta.1" "1.0.0" "maran-update" "maran rollback"; do
  printf '%s' "$RUN_OUT" | grep -qF -- "$fragment" \
    || selftest_failed "the interrupted-update refusal does not say '${fragment}': ${RUN_OUT}"
done
selftest_pass "the refusal names both versions, the dump directory and the command to take"
expect_true "the refusal repaired nothing: release.state is untouched" cmp -s "${work}/state.interrupted" "${etc}/release.state"
run_capture cli_layout_current_version
expect_equal "the refusal repaired nothing: current is untouched" "1.1.0-beta.1" "$(trim "$RUN_OUT")"
previous_target="$(cli_layout_previous_version)"
expect_equal "PREVIOUS written before the switch is the rollback target" "1.0.0" "$previous_target"
cli_layout_switch "$previous_target"
expect_accept "after returning to the recorded release the check passes again (inverse control)" cli_layout_check_consistent

# Fail closed: whatever cannot be read or does not add up is a refusal that RETURNS, never a pass.
rm -f "${mar}/current"
ln -s releases/9.9.9 "${mar}/current"
expect_returns_refusal "a current that names a missing release" "missing" cli_layout_check_consistent
rm -f "${mar}/current"
install -d "${mar}/current"
expect_returns_refusal "a current that is a real directory" "not a symlink" cli_layout_check_consistent
rmdir "${mar}/current"
cli_layout_switch 1.0.0
write_state_file $'VERSION=1.0.0\nVERSION=1.0.0\n'
expect_returns_refusal "a release.state that repeats a key (unreadable state)" "repeats" cli_layout_check_consistent
write_state_file $'CHANNEL=nightly\nVERSION=1.0.0\nPREVIOUS=\n'
expect_returns_refusal "a release.state with an invalid channel" "invalid CHANNEL" cli_layout_check_consistent
cli_layout_state_write stable 1.0.0 ""
rm -f "${mar}/current"
expect_returns_refusal "release.state names 1.0.0 but there is no current" "interrupted" cli_layout_check_consistent
cli_layout_state_write stable legacy-20261104T000000Z ""
expect_accept "no current and a legacy-* state (an adoption not yet switched)" cli_layout_check_consistent
rm -f "${etc}/release.state"
cli_layout_switch 1.0.0
expect_returns_refusal "a SemVer current with no release.state to agree with" "interrupted" cli_layout_check_consistent
mk_release legacy-20261104T000000Z
cli_layout_switch legacy-20261104T000000Z
expect_accept "a legacy current with no release.state (an adoption in progress)" cli_layout_check_consistent

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 13: legacy adoption - real directories become releases/, symlinks take their place --"
# ---------------------------------------------------------------------------------------------
plant_legacy
cli_layout_state_write stable 1.0.0 ""
inode_before="$(stat -c %i "${mar}/api")"
run_capture cli_layout_adopt_legacy
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "adopting a legacy layout whose release.state names 1.0.0 was refused: ${RUN_OUT}"
expect_equal "adoption prints the release name it created" "1.0.0" "$(trim "$RUN_OUT")"
expect_true "api, agent and frontend moved into releases/1.0.0" \
  test -f "${mar}/releases/1.0.0/api/marker.txt" -a -f "${mar}/releases/1.0.0/agent/marker.txt" -a -f "${mar}/releases/1.0.0/frontend/marker.txt"
expect_equal "the move kept the directory's inode (a running process keeps its files)" "$inode_before" "$(stat -c %i "${mar}/releases/1.0.0/api")"
for c in api agent frontend; do
  expect_true "${c} is now a symlink" test -L "${mar}/${c}"
  expect_equal "/usr/local/maran/${c} still reads the legacy content, through current" "legacy ${c} content" "$(cat "${mar}/${c}/marker.txt")"
done
expect_equal "current resolves to releases/1.0.0" "$(readlink -f "${mar}/releases/1.0.0")" "$(readlink -f "${mar}/current")"
run_capture cli_layout_current_version
expect_equal "current_version after adoption" "1.0.0" "$(trim "$RUN_OUT")"
expect_accept "current and release.state agree after the adoption" cli_layout_check_consistent
find "$mar" -printf '%p %l\n' | sort > "${work}/adopted.before"
run_capture cli_layout_adopt_legacy
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "adopting an already adopted layout was refused: ${RUN_OUT}"
expect_equal "an already adopted layout adopts nothing and prints nothing (inverse control)" "" "$(trim "$RUN_OUT")"
find "$mar" -printf '%p %l\n' | sort > "${work}/adopted.after"
expect_true "the second adoption changed nothing" cmp -s "${work}/adopted.before" "${work}/adopted.after"

plant_legacy
expect_accept "adopting a legacy layout with no release.state" cli_layout_adopt_legacy
legacy_name="$(find "${mar}/releases" -mindepth 1 -maxdepth 1 -name 'legacy-*' -printf '%f\n')"
expect_equal "the content went to exactly one releases/legacy-<timestamp>" "1" "$(printf '%s\n' "$legacy_name" | grep -c .)"
expect_true "the legacy name is legacy-<UTC date>T<UTC time>Z" cli_version_is_legacy "$legacy_name"
expect_equal "the legacy content survived the move" "legacy api content" "$(cat "${mar}/api/marker.txt")"
expect_true "api is a symlink after the adoption" test -L "${mar}/api"
expect_accept "a legacy current with no release.state is consistent (the first update after adoption)" cli_layout_check_consistent

plant_legacy
cli_layout_state_write stable 1.0.0 ""
install -d "${mar}/releases/1.0.0"
expect_refuse "adopting over a release directory that already exists" "already exists" cli_layout_adopt_legacy
expect_true "the refused adoption moved nothing: api is still a real directory with its content" test -d "${mar}/api" -a ! -L "${mar}/api" -a -f "${mar}/api/marker.txt"
plant_legacy
rm -rf -- "${mar}/agent"
expect_refuse "adopting a half legacy layout (agent is gone)" "is missing" cli_layout_adopt_legacy
plant_legacy
rm -rf -- "${mar}/agent"
echo "not a directory" > "${mar}/agent"
expect_refuse "adopting a layout where a component is a plain file" "neither a link nor a directory" cli_layout_adopt_legacy
plant_legacy
ln -s "${work}/victim" "${mar}/releases"
expect_refuse "adopting through a releases/ that is a symlink" "is a symlink" cli_layout_adopt_legacy

# Resume after a kill. The order is: create releases/<name>, point current, then per component rename
# it in and link it back; each state below is one a kill leaves, built by hand.
plant_legacy
cli_layout_state_write stable 1.0.0 ""
install -d "${mar}/releases/1.0.0"
mv "${mar}/api" "${mar}/releases/1.0.0/api"
ln -s current/api "${mar}/api"
ln -s releases/1.0.0 "${mar}/current"
run_capture cli_layout_adopt_legacy
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "resuming an adoption killed after the first component was refused: ${RUN_OUT}"
expect_equal "resuming after a kill with current set and one component moved prints the release" "1.0.0" "$(trim "$RUN_OUT")"
expect_true "the two components still real were moved into the release" test -f "${mar}/releases/1.0.0/agent/marker.txt" -a -f "${mar}/releases/1.0.0/frontend/marker.txt"
expect_true "all three static links exist and read the content" \
  test -L "${mar}/api" -a -L "${mar}/agent" -a -L "${mar}/frontend" -a "$(cat "${mar}/frontend/marker.txt")" = "legacy frontend content"
expect_accept "current and release.state agree after the resumed adoption" cli_layout_check_consistent

plant_legacy
cli_layout_state_write stable 1.0.0 ""
install -d "${mar}/releases/1.0.0"
mv "${mar}/api" "${mar}/releases/1.0.0/api"
ln -s current/api "${mar}/api"
mv "${mar}/agent" "${mar}/releases/1.0.0/agent"
ln -s releases/1.0.0 "${mar}/current"
run_capture cli_layout_adopt_legacy
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "resuming an adoption killed between a rename and its link was refused: ${RUN_OUT}"
expect_equal "resuming after a kill between a rename and its link prints the release" "1.0.0" "$(trim "$RUN_OUT")"
expect_equal "the component renamed but never linked got its link back" "legacy agent content" "$(cat "${mar}/agent/marker.txt")"
expect_true "the component still real was moved" test -f "${mar}/releases/1.0.0/frontend/marker.txt" -a -L "${mar}/frontend"

plant_legacy
cli_layout_state_write stable 1.0.0 ""
install -d "${mar}/releases/2.0.0"
ln -s releases/2.0.0 "${mar}/current"
expect_refuse "a component is real and current names a release that is neither the recorded version nor legacy" "refusing to guess" cli_layout_adopt_legacy
expect_true "the refused resume moved nothing" test -d "${mar}/api" -a ! -L "${mar}/api" -a -f "${mar}/api/marker.txt"

plant_legacy
cli_layout_state_write stable 1.0.0 ""
install -d "${mar}/releases/1.0.0/api"
ln -s releases/1.0.0 "${mar}/current"
expect_refuse "a component that exists both in the root and inside the release" "both" cli_layout_adopt_legacy

plant_legacy
install -d "${mar}/releases/legacy-20261104T000000Z"
ln -s releases/legacy-20261104T000000Z "${mar}/current"
run_capture cli_layout_adopt_legacy
[ "$RUN_STATUS" -eq 0 ] || selftest_failed "resuming an adoption into a legacy release with no release.state was refused: ${RUN_OUT}"
expect_equal "resuming into a legacy-* current with no recorded version prints the legacy name" "legacy-20261104T000000Z" "$(trim "$RUN_OUT")"
expect_true "...and moved the content in" test -f "${mar}/releases/legacy-20261104T000000Z/frontend/marker.txt"
