#!/usr/bin/env bash
# release-unpack.sh: vetting and safe extraction of release archives.
#
# Sourced by /usr/local/sbin/maran (and by installer/lib/50-artifacts.sh) after common.sh; never
# executed on its own, and it carries no `set` option (the wrapper has set -euo pipefail). Both
# functions return 1 with a message on stderr instead of calling cli_fail, so a caller decides how a
# refusal ends its run and can clean up first.
#
# The rule for a release tree is the strictest one that still ships the panel: a member is a regular
# file or a directory, and nothing else. Hard links, device nodes, FIFOs and symlinks are refused. A
# symlink is refused outright rather than checked for where it points, because a lexical "stays
# inside the root" decision is exactly the kind of text-parsing judgement a path attack defeats
# (a link to a link, a target built from a directory the archive also creates), and no release needs
# one. The check that used to decide by depth is gone with that: a defensive branch that cannot
# fire is deleted, not labelled.

# cli_unpack_vet_members TARBALL: lists the members of a gzip tar and returns 1 (message on stderr)
# when it holds none, or any member that is absolute, has a `..` component, has a dangerous name, or
# is not a regular file or a directory.
#
# Names are judged in the listing's ESCAPED form (--quoting-style=escape, LC_ALL=C so every byte >= 0x80
# is printed as an octal escape and never raw). In that form each member is exactly one line, a newline
# in a name shows as `\n`, and a backslash in a name shows as `\\`. A name is accepted only when every
# backslash in it begins one of: `\ ` (a space), `\"`, `\'`, or a three-digit octal escape of a byte
# 0200..0377 (the bytes of a UTF-8 name). Everything else is refused: `\n`, `\r`, `\t` and the other
# letter escapes, `\\` (a real backslash), and octal escapes of 0000..0177 (control characters, DEL).
# So spaces and UTF-8 names are legitimate; newlines, control characters and backslashes are not.
#
# Blind spot, stated: bytes >= 0x80 are accepted without checking that they form valid UTF-8, and the
# decision rests on GNU tar quoting faithfully; the extraction below uses the same tar. This vets
# names and types only; it does not look at file contents.
cli_unpack_vet_members() {
  local archive="$1" name line listing dotdot_re='(^|/)\.\.(/|$)' count=0
  local safe_name_re='^([^\\]|\\[ "'"'"']|\\[23][0-7][0-7])*$'
  listing="$(LC_ALL=C tar --quoting-style=escape -tzf "$archive")" \
    || { echo "maran: cannot list ${archive}" >&2; return 1; }
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    count=$((count + 1))
    if [[ "$name" == /* || "$name" =~ $dotdot_re ]] || ! [[ "$name" =~ $safe_name_re ]]; then
      echo "maran: archive member refused: ${name}" >&2; return 1
    fi
  done <<< "$listing"
  [ "$count" -gt 0 ] || { echo "maran: ${archive} holds no members" >&2; return 1; }
  listing="$(LC_ALL=C tar --quoting-style=escape -tvzf "$archive")" \
    || { echo "maran: cannot list ${archive}" >&2; return 1; }
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    case "${line:0:1}" in
      -|d) ;;
      l) echo "maran: archive holds a symlink, which a release may not: ${line}" >&2; return 1 ;;
      *) echo "maran: archive member of a refused type: ${line}" >&2; return 1 ;;
    esac
  done <<< "$listing"
}

# cli_unpack_into TARBALL DEST: vets TARBALL, extracts it into the existing directory DEST with no
# ownership or permissions restored from the archive (umask 022 in a subshell), then makes the whole
# tree root:root and strips group and world write, the set-id bits and the sticky bit, in that order
# (chown clears set-id on some systems, but not on every one, so the chmod comes after and is not
# left to the kernel). Returns 1 on any refusal or failure; the caller removes DEST.
cli_unpack_into() {
  local archive="$1" dest="$2"
  [ -d "$dest" ] && [ ! -L "$dest" ] || { echo "maran: ${dest} is not a plain directory" >&2; return 1; }
  cli_unpack_vet_members "$archive" || return 1
  ( umask 022; tar -xzf "$archive" -C "$dest" --no-same-owner --no-same-permissions ) \
    || { echo "maran: cannot extract ${archive}" >&2; return 1; }
  chown -R root:root -- "$dest" && chmod -R go-w,ug-s,o-t -- "$dest" \
    || { echo "maran: cannot normalise the ownership of ${dest}" >&2; return 1; }
}
