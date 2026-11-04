#!/usr/bin/env bash
# update-selftest-unpack.sh: the cases about the archive rules of release-unpack.sh, driven directly,
# cli_unpack_vet_members TARBALL (what a member may be) and cli_unpack_into TARBALL DEST (extraction
# without the archive's ownership or permissions, then root:root, and no set-id, sticky or group/world
# write bits left).
#
# Sourced by update-selftest.sh after update-selftest-world.sh; never executed on its own and it sets
# no shell option. Both functions RETURN 1 with a message instead of calling cli_fail, which
# expect_refuse sees as a non-zero status of the capturing subshell. Every fixture is checked to carry
# the member it is about, so a refusal cannot pass over an archive that lost its hostile member.

# build_tar: <kind> <out tarball>. Writes a small gzip tar of the named shape from a fresh source
# directory; the shapes are clean, clean-nodot, dotname, dotdot, absolute, backslash, newline,
# symlink-in, symlink-out, hardlink, fifo and modes.
build_tar() {
  local kind="$1" out="$2" src
  src="$(mktemp -d "${work}/tarsrc.XXXXXX")"
  echo "file a" > "${src}/a.txt"
  install -d "${src}/sub"
  echo "file b" > "${src}/sub/b.txt"
  case "$kind" in
    clean) tar -czf "$out" -C "$src" . ;;
    clean-nodot) tar -czf "$out" -C "$src" a.txt sub ;;
    dotname)
      echo "a file whose name merely starts with two dots" > "${src}/..foo"
      tar -czf "$out" -C "$src" . ;;
    dotdot)
      echo "evil" > "${work}/selftest-evil-unpack-dotdot"
      tar -P -czf "$out" -C "$src" . ../selftest-evil-unpack-dotdot
      tar -tPzf "$out" | grep -qxF '../selftest-evil-unpack-dotdot' || selftest_failed "fixture: the dotdot tarball does not carry its ../ member." ;;
    absolute)
      echo "evil" > "${work}/selftest-evil-unpack-absolute"
      tar -P -czf "$out" -C "$src" . "${work}/selftest-evil-unpack-absolute"
      tar -tPzf "$out" | grep -qxF "${work}/selftest-evil-unpack-absolute" || selftest_failed "fixture: the absolute tarball does not carry its member." ;;
    backslash)
      echo "evil" > "${src}/back\\slash"
      tar -czf "$out" -C "$src" .
      tar --quoting-style=escape -tzf "$out" | grep -qF 'back\\slash' || selftest_failed "fixture: the backslash tarball does not carry its member." ;;
    newline)
      echo "evil" > "${src}/$(printf 'nl\nname')"
      tar -czf "$out" -C "$src" .
      tar --quoting-style=escape -tzf "$out" | grep -qF 'nl\nname' || selftest_failed "fixture: the newline tarball does not carry its member." ;;
    symlink-in)
      ln -s a.txt "${src}/link"
      tar -czf "$out" -C "$src" .
      tar -tvzf "$out" | grep -qE -- 'link -> a\.txt' || selftest_failed "fixture: the in-tree symlink tarball does not carry its link." ;;
    symlink-out)
      ln -s /etc/passwd "${src}/link"
      tar -czf "$out" -C "$src" .
      tar -tvzf "$out" | grep -qE -- 'link -> /etc/passwd' || selftest_failed "fixture: the escaping symlink tarball does not carry its link." ;;
    hardlink)
      ln "${src}/a.txt" "${src}/hard"
      tar -czf "$out" -C "$src" .
      tar -tvzf "$out" | grep -qE -- '^h' || selftest_failed "fixture: the hardlink tarball does not carry a hard-link member." ;;
    fifo)
      mkfifo "${src}/pipe"
      tar -czf "$out" -C "$src" .
      tar -tvzf "$out" | grep -qE -- '^p' || selftest_failed "fixture: the fifo tarball does not carry a FIFO member." ;;
    modes)
      chmod 0666 "${src}/a.txt"
      echo "set-id" > "${src}/setid-file"
      chmod 6755 "${src}/setid-file"
      install -d -m 1777 "${src}/world-dir"
      tar -czf "$out" -C "$src" .
      tar -tvzf "$out" | grep -qE -- '^-rwsr-sr-x .* \./setid-file$' || selftest_failed "fixture: the modes tarball does not carry its set-id file." ;;
    *) selftest_failed "fixture: unknown tarball kind ${kind}" ;;
  esac
}

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 17: vetting - which members a release archive may carry --"
# ---------------------------------------------------------------------------------------------
utar="${work}/utar"
install -d "$utar"
for kind in clean clean-nodot dotname dotdot absolute backslash newline symlink-in symlink-out hardlink fifo modes; do
  build_tar "$kind" "${utar}/${kind}.tar.gz"
done
expect_accept "a clean archive of files and directories (it has a ./ member)" cli_unpack_vet_members "${utar}/clean.tar.gz"
expect_accept "a clean archive listed by member name, with no ./ member" cli_unpack_vet_members "${utar}/clean-nodot.tar.gz"
expect_accept "a member whose NAME merely starts with two dots (..foo is not a .. component; inverse control)" cli_unpack_vet_members "${utar}/dotname.tar.gz"
expect_refuse "a member with a leading ../ component" "member refused" cli_unpack_vet_members "${utar}/dotdot.tar.gz"
expect_refuse "a member with an absolute path" "member refused" cli_unpack_vet_members "${utar}/absolute.tar.gz"
expect_refuse "a member whose name carries a backslash" "member refused" cli_unpack_vet_members "${utar}/backslash.tar.gz"
expect_refuse "a member whose name carries a newline (listed escaped, so it shows a backslash)" "member refused" cli_unpack_vet_members "${utar}/newline.tar.gz"
expect_refuse "a symlink that stays inside the tree (symlinks are refused outright)" "symlink" cli_unpack_vet_members "${utar}/symlink-in.tar.gz"
expect_refuse "a symlink that escapes the tree" "symlink" cli_unpack_vet_members "${utar}/symlink-out.tar.gz"
expect_refuse "a hard link" "refused type" cli_unpack_vet_members "${utar}/hardlink.tar.gz"
expect_refuse "a FIFO" "refused type" cli_unpack_vet_members "${utar}/fifo.tar.gz"
if [ "$(id -u)" -eq 0 ]; then
  devsrc="$(mktemp -d "${work}/tarsrc.XXXXXX")"
  mknod "${devsrc}/dev" c 1 3
  tar -czf "${utar}/device.tar.gz" -C "$devsrc" .
  tar -tvzf "${utar}/device.tar.gz" | grep -qE -- '^c' || selftest_failed "fixture: the device tarball does not carry a character device."
  expect_refuse "a character device node" "refused type" cli_unpack_vet_members "${utar}/device.tar.gz"
else
  echo "UNOBSERVED HERE: the refusal of a device node (creating one needs root; the FIFO case above shares its member-type branch)."
fi
echo "not a gzip tar" > "${utar}/garbage.tar.gz"
expect_refuse "a file that is not an archive" "cannot list" cli_unpack_vet_members "${utar}/garbage.tar.gz"
expect_refuse "an archive that does not exist" "cannot list" cli_unpack_vet_members "${utar}/missing.tar.gz"
head -c 10240 /dev/zero | gzip > "${utar}/empty.tar.gz"
expect_refuse "an archive that holds no members at all (only end-of-archive blocks)" "holds no members|cannot list" cli_unpack_vet_members "${utar}/empty.tar.gz"

# ---------------------------------------------------------------------------------------------
echo
echo "-- case 18: extraction - nothing from the archive's owners or modes survives --"
# ---------------------------------------------------------------------------------------------
dest="$(mktemp -d "${work}/unpack.XXXXXX")"
: > "$MARAN_SELFTEST_CHOWN_LOG"
: > "$MARAN_SELFTEST_TAR_LOG"
expect_accept "extracting a clean archive into an existing directory" cli_unpack_into "${utar}/clean.tar.gz" "$dest"
expect_true "the members are in the destination" test -f "${dest}/a.txt" -a -f "${dest}/sub/b.txt"
expect_true "the destination was made root:root recursively" grep -qE 'root:root' "$MARAN_SELFTEST_CHOWN_LOG"
# The extraction audit reads the argv of the extracting tar calls; the hostile attempts below are
# vetted before any extraction, so they add none. Reset first, and prove the audit can hit.
extract_calls="$(grep -cE -- '^-xzf ' "$MARAN_SELFTEST_TAR_LOG" || true)"
bare_calls="$(grep -E -- '^-xzf ' "$MARAN_SELFTEST_TAR_LOG" | grep -vc -- '--no-same-owner --no-same-permissions' || true)"
expect_true "the audit saw the extracting tar call (positive control)" test "$extract_calls" -ge 1
expect_equal "every extracting tar call restores neither owner nor permissions from the archive" "0" "$bare_calls"

# set-id, sticky and group/world write bits: the shim re-adds them AFTER the extractor ran, as a tar
# that restored modes would, so the stripping observed is the CLI's own. The control proves the
# re-adding works, otherwise a clean result would mean nothing.
ctrl="$(mktemp -d "${work}/unpack.XXXXXX")"
MARAN_SELFTEST_TAR_PLANT_SETID=1 tar -xzf "${utar}/modes.tar.gz" -C "$ctrl"
expect_true "the shim re-adds set-id and sticky bits after an extraction (positive control)" test -n "$(find "$ctrl" -perm /7000 | head -1)"
dest="$(mktemp -d "${work}/unpack.XXXXXX")"
MARAN_SELFTEST_TAR_PLANT_SETID=1 expect_accept "extracting an archive with a 0666 file, a 6755 file and a 1777 directory" cli_unpack_into "${utar}/modes.tar.gz" "$dest"
expect_true "...no set-id or sticky bit is left anywhere in the destination" test -z "$(find "$dest" -perm /7000 | head -1)"
expect_true "...no entry is writable by group or others" test -z "$(find "$dest" ! -type l -perm /022 | head -1)"

link_dest="${work}/unpack-link"
ln -s "$dest" "$link_dest"
expect_refuse "a destination that is a symlink" "not a plain directory" cli_unpack_into "${utar}/clean.tar.gz" "$link_dest"
expect_refuse "a destination that does not exist" "not a plain directory" cli_unpack_into "${utar}/clean.tar.gz" "${work}/no-such-destination"
dest="$(mktemp -d "${work}/unpack.XXXXXX")"
expect_refuse "extracting an archive with a symlink member" "symlink" cli_unpack_into "${utar}/symlink-out.tar.gz" "$dest"
expect_equal "...which wrote nothing into the destination" "" "$(ls -A "$dest")"
