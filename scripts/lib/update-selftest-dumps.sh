#!/usr/bin/env bash
# update-selftest-dumps.sh: the cases about the pre-update database dump CONTRACT of update-dumps.sh,
# the directory rules, the name grammar (including a name that splits into FROM and TO in more than
# one way, because a pre-release identifier may contain "-to-"), the newest dump chosen by the
# timestamp in its NAME and never by mtime, and pruning that touches only names that fit.
#
# Sourced by update-selftest.sh after update-selftest-world.sh; never executed on its own and it sets
# no shell option. No database is involved: a dump here is an empty file, because what is under test
# is how dumps are named, found and removed, not what they hold (pg_dump is UNOBSERVED HERE).

# matches: <text> <extended regex>. A predicate for expect_true.
matches() {
  [[ "$1" =~ $2 ]]
}

# mk_dump: <file name>. An empty 0600 file in the dump directory.
mk_dump() {
  : > "${backup_dir}/${1}"
  chmod 0600 "${backup_dir}/${1}"
}

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 19: the dump directory - created root-only, refused when anyone else could reach it --"
# ---------------------------------------------------------------------------------------------
rm -rf -- "$backup_dir"
expect_equal "cli_dumps_path prints the configured directory" "$backup_dir" "$(cli_dumps_path)"
run_capture cli_dumps_dir
expect_equal "cli_dumps_dir creates the directory with mode 0700 and prints it" "${backup_dir}:700" "$(trim "$RUN_OUT"):$(stat -c %a "$backup_dir")"
expect_accept "cli_dumps_dir on the directory it created" cli_dumps_dir
chmod 0755 "$backup_dir"
expect_refuse "a dump directory other users can enter (0755)" "not a root-owned 0700" cli_dumps_dir
expect_refuse "...refused by require_private too" "not a root-owned 0700" cli_dumps_require_private
chmod 0700 "$backup_dir"
expect_accept "the same directory at 0700 (inverse control)" cli_dumps_require_private
mv "$backup_dir" "${work}/dumps.aside"
ln -s "${work}/dumps.aside" "$backup_dir"
expect_refuse "a dump directory that is a symlink" "not a root-owned 0700" cli_dumps_dir
rm -f "$backup_dir"
mv "${work}/dumps.aside" "$backup_dir"
if [ "$(id -u)" -eq 0 ]; then
  chown 65534 "$backup_dir"
  expect_refuse "a dump directory owned by another uid" "not a root-owned 0700" cli_dumps_require_private
  chown 0 "$backup_dir"
else
  echo "UNOBSERVED HERE: the refusal of a dump directory owned by another uid (this run is not root, and stat reports its own files as root's)."
fi
rm -rf -- "$backup_dir"
expect_refuse "require_private on a directory that does not exist" "not a root-owned 0700" cli_dumps_require_private
cli_dumps_dir > /dev/null

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 20: the name grammar pre-<FROM>-to-<TO>-<UTC stamp>.dump --"
# ---------------------------------------------------------------------------------------------
run_capture cli_dumps_name 1.0.0 1.1.0-beta.1
expect_true "a name for 1.0.0 -> 1.1.0-beta.1 carries both releases and a UTC stamp" \
  matches "$(trim "$RUN_OUT")" '^pre-1\.0\.0-to-1\.1\.0-beta\.1-[0-9]{8}T[0-9]{6}Z\.dump$'
run_capture cli_dumps_name legacy-20261104T000000Z 1.0.0
expect_true "a legacy host's FROM is the adopted release name, the string recorded as PREVIOUS" \
  matches "$(trim "$RUN_OUT")" '^pre-legacy-20261104T000000Z-to-1\.0\.0-[0-9]{8}T[0-9]{6}Z\.dump$'
expect_refuse "naming a dump after the unknown release 'unknown'" "invalid release" cli_dumps_name unknown 1.0.0
expect_refuse "naming a dump after a path-shaped FROM" "invalid release" cli_dumps_name "../x" 1.0.0
expect_refuse "naming a dump after an empty FROM" "invalid release" cli_dumps_name "" 1.0.0
expect_refuse "naming a dump after a TO that is not SemVer (1.0)" "invalid release" cli_dumps_name 1.0.0 1.0
expect_refuse "naming a dump after a TO with a newline in it" "invalid release" cli_dumps_name 1.0.0 $'1.1.0\nx'

# A name fits when ANY split at a "-to-" yields two release names; a lookup by TO needs the split to
# end in exactly that TO. "pre-1.0.0-to-1.0.0-to-2.0.0-..." splits two valid ways.
ambiguous="pre-1.0.0-to-1.0.0-to-2.0.0-20261104T000000Z.dump"
plain="pre-1.0.0-to-1.1.0-20261104T000000Z.dump"
for row in "${plain}|" \
           "pre-legacy-20261104T000000Z-to-1.0.0-20261104T000100Z.dump|" \
           "pre-1.0.0-to-1.1.0-to-x-20261104T000000Z.dump|" \
           "${plain}|1.1.0" \
           "${ambiguous}|2.0.0" \
           "${ambiguous}|1.0.0-to-2.0.0"; do
  IFS='|' read -r name to <<< "$row"
  expect_true "the name ${name} fits the grammar${to:+ with TO ${to}}" cli_dumps_name_fits "$name" "$to"
done
for row in "${ambiguous}|1.0.0" \
           "${ambiguous}|3.0.0" \
           "${plain}|1.0.0" \
           "pre-1.0.0-1.1.0-20261104T000000Z.dump|" \
           "pre-1.0.0-to-1.1.0-20261104.dump|" \
           "pre-1.0.0-to-1.1.0-20261104T000000Z.dumps|" \
           "pre-1.0-to-1.1.0-20261104T000000Z.dump|" \
           "pre-1.0.0-to-1.1-20261104T000000Z.dump|" \
           "x-pre-1.0.0-to-1.1.0-20261104T000000Z.dump|" \
           "pre--to-1.1.0-20261104T000000Z.dump|" \
           "pre-1.0.0-to--20261104T000000Z.dump|" \
           "${plain}.bak|"; do
  IFS='|' read -r name to <<< "$row"
  expect_false "the name ${name} does not fit the grammar${to:+ with TO ${to}}" cli_dumps_name_fits "$name" "$to"
done

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 21: the newest dump for a release - by the stamp in the name, never by mtime --"
# ---------------------------------------------------------------------------------------------
dump_a="pre-1.0.0-to-1.1.0-20261101T000000Z.dump"
dump_b="pre-1.0.0-to-1.1.0-20261103T000000Z.dump"
dump_c="pre-1.0.1-to-1.1.0-20261102T000000Z.dump"
dump_d="pre-1.0.0-to-1.2.0-20261105T000000Z.dump"
dump_e="pre-0.9.0-to-1.1.0-20261104T000000Z.dump"
dump_f="pre-1.0.0-to-1.0.0-to-2.0.0-20261106T000000Z.dump"
for n in "$dump_a" "$dump_b" "$dump_c" "$dump_d"; do mk_dump "$n"; done
touch -d '2037-01-01 00:00:00' "${backup_dir}/${dump_a}"
touch -d '2001-01-01 00:00:00' "${backup_dir}/${dump_b}"
[ "${backup_dir}/${dump_a}" -nt "${backup_dir}/${dump_b}" ] || selftest_failed "fixture: the oldest-named dump is not the newest on disk - the mtime check would be vacuous."
run_capture cli_dumps_newest_to 1.1.0
expect_equal "the newest dump for 1.1.0 is the greatest NAME (…20261103), though another is newer on disk" "${backup_dir}/${dump_b}" "$(trim "$RUN_OUT")"
mk_dump "$dump_e"
run_capture cli_dumps_newest_to 1.1.0
expect_equal "the newest dump is chosen across every FROM" "${backup_dir}/${dump_e}" "$(trim "$RUN_OUT")"
run_capture cli_dumps_newest_to 1.2.0
expect_equal "the newest dump for 1.2.0 is the one dump taken for it" "${backup_dir}/${dump_d}" "$(trim "$RUN_OUT")"
run_capture cli_dumps_newest_to 1.9.9
expect_equal "no dump for 1.9.9: prints nothing and succeeds" "0:" "${RUN_STATUS}:$(trim "$RUN_OUT")"
run_capture cli_dumps_newest_to "../x"
{ [ "$RUN_STATUS" -eq 0 ] && [[ "$RUN_OUT" == *"not a release name"* ]] && [[ "$RUN_OUT" != *"/pre-"* ]]; } \
  || selftest_failed "a lookup by a name that is not a release must say so and print no path: ${RUN_OUT}"
selftest_pass "a lookup by a TO that is not a release name prints no path and says why"
# Candidates that must never be returned, each NEWER by name than the dump that should win.
ln -s "${backup_dir}/${dump_b}" "${backup_dir}/pre-1.0.0-to-1.1.0-20271231T000000Z.dump"
install -d "${backup_dir}/pre-1.0.0-to-1.1.0-20271230T000000Z.dump"
: > "${backup_dir}/pre-1.0.0-to-1.1.0-20271229T000000Z.dump.bak"
mk_dump "pre-1.0-to-1.1.0-20271228T000000Z.dump"
run_capture cli_dumps_newest_to 1.1.0
expect_equal "a symlink, a directory, a .bak and a badly named file are never returned, however new" "${backup_dir}/${dump_e}" "$(trim "$RUN_OUT")"
chmod 0755 "$backup_dir"
run_capture cli_dumps_newest_to 1.1.0
{ [ "$RUN_STATUS" -eq 0 ] && [[ "$RUN_OUT" == *"not trusted"* ]] && [[ "$RUN_OUT" != *"/pre-"* ]]; } \
  || selftest_failed "a lookup in a directory others can enter must print no path and say why: ${RUN_OUT}"
selftest_pass "a dump directory others can enter yields no path (its dumps are not trusted)"
chmod 0700 "$backup_dir"
mv "$backup_dir" "${work}/dumps.aside"
run_capture cli_dumps_newest_to 1.1.0
expect_equal "no dump directory at all: prints nothing and succeeds" "0:" "${RUN_STATUS}:$(trim "$RUN_OUT")"
mv "${work}/dumps.aside" "$backup_dir"
mk_dump "$dump_f"
run_capture cli_dumps_newest_to 2.0.0
expect_equal "an ambiguous name is found by the TO it ends in (2.0.0)" "${backup_dir}/${dump_f}" "$(trim "$RUN_OUT")"
run_capture cli_dumps_newest_to 1.0.0-to-2.0.0
expect_equal "...and by the other TO it can be read as (1.0.0-to-2.0.0)" "${backup_dir}/${dump_f}" "$(trim "$RUN_OUT")"
run_capture cli_dumps_newest_to 1.0.0
expect_equal "...and not by a TO it cannot end in (1.0.0)" "0:" "${RUN_STATUS}:$(trim "$RUN_OUT")"
if [ "$(id -u)" -eq 0 ]; then
  chown 65534 "${backup_dir}/${dump_f}"
  run_capture cli_dumps_newest_to 2.0.0
  expect_equal "a dump file owned by another uid is never returned" "0:" "${RUN_STATUS}:$(trim "$RUN_OUT")"
  chown 0 "${backup_dir}/${dump_f}"
else
  echo "UNOBSERVED HERE: that a dump owned by another uid is skipped (this run is not root, and stat reports its own files as root's)."
fi

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 22: pruning keeps the N newest by name and removes nothing that is not a dump --"
# ---------------------------------------------------------------------------------------------
: > "${backup_dir}/notes.txt"
for keep in x -1 "1;2"; do
  expect_refuse "keeping '${keep}' dumps" "non-negative integer" cli_dumps_prune "$keep"
done
chmod 0755 "$backup_dir"
expect_refuse "pruning a dump directory others can enter" "not a root-owned 0700" cli_dumps_prune 3
expect_true "...which removed nothing" test -e "${backup_dir}/${dump_a}" -a -e "${backup_dir}/${dump_b}"
chmod 0700 "$backup_dir"
# Six dumps fit the grammar: by name F (1106), D (1105), E (1104), B (1103), C (1102), A (1101). The
# mtime order differs (A is the newest on disk), so a prune by mtime would keep A and remove F.
expect_accept "pruning to the 3 newest" cli_dumps_prune 3
for n in "$dump_f" "$dump_d" "$dump_e"; do
  expect_true "the dump ${n} survived (among the 3 newest by name)" test -f "${backup_dir}/${n}"
done
for n in "$dump_b" "$dump_c" "$dump_a"; do
  expect_true "the dump ${n} was removed" test ! -e "${backup_dir}/${n}"
done
expect_true "a file that is not a dump was left alone" test -f "${backup_dir}/notes.txt"
expect_true "a symlink named like a dump was left alone" test -L "${backup_dir}/pre-1.0.0-to-1.1.0-20271231T000000Z.dump"
expect_true "a directory named like a dump was left alone" test -d "${backup_dir}/pre-1.0.0-to-1.1.0-20271230T000000Z.dump"
expect_true "a badly named file under pre-*.dump was left alone" test -f "${backup_dir}/pre-1.0-to-1.1.0-20271228T000000Z.dump"
expect_accept "pruning again when only 3 dumps exist (inverse control)" cli_dumps_prune 3
expect_true "...removes none of them" test -f "${backup_dir}/${dump_f}" -a -f "${backup_dir}/${dump_d}" -a -f "${backup_dir}/${dump_e}"
mv "$backup_dir" "${work}/dumps.aside"
expect_accept "pruning when there is no dump directory has nothing to do" cli_dumps_prune 3
mv "${work}/dumps.aside" "$backup_dir"
