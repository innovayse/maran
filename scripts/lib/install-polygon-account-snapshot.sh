#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file (through
# install-polygon-account-restore.sh's stage).
#
# The eyes of the account-restore proof (issue #6): a snapshot of an account's home and databases and
# a comparison of two snapshots. Nothing here restores anything; a proof that an account came back
# identical is worth exactly what its comparison can SEE, so this file is where that is argued.
#
# What a snapshot records (rules/testing.md, "a check must be able to observe what it reports on"):
#
#   files  one line per entry under the home, the home itself included:
#            path, type, uid, gid, mode, size, link target, sha256 of the content
#          Size is recorded for regular files only (a directory's size is the filesystem's business),
#          the content hash for regular files only, the link target for symlinks only. Modification
#          times are NOT recorded: a restore that extracts an archive does not promise them, and a
#          comparison that demanded them would fail on a correct restore.
#   db     one line per database of the account and one per table:
#            database.table, row count, sha256 of `mysqldump --skip-comments --order-by-primary`
#
# Names holding a tab or a newline cannot be put on one line and make the snapshot REFUSE rather
# than silently drop them.
#
# What the comparison can tell apart is the list of "axes" in acr_compare_awk: removed, added, type,
# owner, group, mode, size, content, link for files; removed, added, rows, content for databases.
# Every axis is proved able to fail twice: against synthetic snapshots with no container at all
# (acr_selftest_comparison), and against a REAL home and database in the container, where a real
# chmod, chown, flipped byte, removed file, added file, retargeted link and altered row is planted,
# compared and reverted (acr_live_controls). The first proves the comparison; the second proves the
# collector feeds it every axis.
#
# Reads these globals from install-polygon.sh and its stages: container, note, recovery_expect.
# Owns ACR_WORK (a host directory the snapshots are written into) and ACR_ACCOUNT / ACR_DB, which the
# restore stage sets.

ACR_WORK=""
ACR_ACCOUNT="${REC_ACCOUNT_NAME:-polyrecover}"
ACR_DB=""

# acr_expect: one scored check of this proof. $1 = what it asserts, $2 = observed, $3 = expected.
acr_expect() { recovery_expect "account-restore: $1" "$2" "$3"; }

# acr_x: runs a command in the current container and prints its standard output; a failed command is
# an empty answer, never a harness death.
acr_x() { docker exec "$container" "$@" 2>/dev/null | tr -d '\r' || true; }

# acr_sql: one SQL statement against the customer MariaDB, as root over the unix socket, the same
# door the agent uses (installer/lib/85-mysql.sh). Prints the result; a failure prints nothing.
#   $1 = SQL.
acr_sql() {
  docker exec "$container" /usr/bin/mysql --protocol=socket --user=root --batch --skip-column-names \
    -e "$1" 2>/dev/null | tr -d '\r' || true
}

# acr_home_script: the program that lists a home, printed so it can be fed to `bash -s` in the
# container. Arguments of the printed script: $1 = the home directory.
acr_home_script() {
  cat <<'HOME'
set -euo pipefail
export LC_ALL=C
home="$1"
tab=$'\t'
cd "$home"
if [ -n "$(find . \( -name $'*\t*' -o -name $'*\n*' \) -print -quit)" ]; then
  echo "unrepresentable: a name under ${home} holds a tab or a newline" >&2
  exit 3
fi
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
# Size only for regular files, link target only for symlinks: the awk turns the others into "-".
find . -printf '%p\t%y\t%U\t%G\t%m\t%s\t%l\n' \
  | awk -F'\t' -v OFS='\t' '{ if ($2 != "f") $6 = "-"; if ($7 == "") $7 = "-"; print }' \
  | sort -t "$tab" -k1,1 > "$work/meta"
find . -type f -print0 | while IFS= read -r -d '' file; do
  sum="$(sha256sum < "$file")"
  printf '%s\t%s\n' "$file" "${sum%% *}"
done | sort -t "$tab" -k1,1 > "$work/sums"
join -t "$tab" -a 1 -e - -o auto "$work/meta" "$work/sums"
HOME
}

# acr_db_script: the program that lists the account's databases, printed for `bash -s` in the
# container. Arguments of the printed script: $1 = the account's name (its databases are
# `<account>_*`).
acr_db_script() {
  cat <<'DB'
set -euo pipefail
export LC_ALL=C
account="$1"
q() { /usr/bin/mysql --protocol=socket --user=root --batch --skip-column-names "$@"; }
databases="$(q -e "show databases like '${account}\\_%'")"
printf '%s\n' "$databases" | sort | while IFS= read -r db; do
  [ -n "$db" ] || continue
  printf '%s\t-\t-\n' "$db"
  tables="$(q -e "select table_name from information_schema.tables where table_schema = '${db}' and table_type = 'BASE TABLE' order by table_name")"
  printf '%s\n' "$tables" | while IFS= read -r table; do
    [ -n "$table" ] || continue
    rows="$(q -e "select count(*) from \`${db}\`.\`${table}\`")"
    sum="$(/usr/bin/mysqldump --protocol=socket --user=root --skip-comments --order-by-primary \
      --single-transaction "$db" "$table" | sha256sum | cut -d' ' -f1)"
    printf '%s.%s\t%s\t%s\n' "$db" "$table" "$rows" "$sum"
  done
done
DB
}

# acr_snapshot: records the account's home and databases under the label $1, in ACR_WORK. Returns 1,
# with the reason noted, when either half could not be read: a snapshot that is missing a half would
# compare as "nothing differs" against another that is missing the same half.
#   $1 = label.
acr_snapshot() {
  local label="$1" status=0
  acr_home_script | docker exec -i "$container" bash -s -- "/home/${ACR_ACCOUNT}" \
    >"${ACR_WORK}/${label}.files" 2>"${ACR_WORK}/${label}.err" || status=$?
  if [ "$status" -ne 0 ]; then
    note "account-restore: the home of ${ACR_ACCOUNT} could not be listed for snapshot '${label}' (exit ${status}): $(head -c 300 "${ACR_WORK}/${label}.err" | tr '\n' ' ')"
    return 1
  fi
  status=0
  acr_db_script | docker exec -i "$container" bash -s -- "$ACR_ACCOUNT" \
    >"${ACR_WORK}/${label}.db" 2>"${ACR_WORK}/${label}.err" || status=$?
  if [ "$status" -ne 0 ]; then
    note "account-restore: the databases of ${ACR_ACCOUNT} could not be listed for snapshot '${label}' (exit ${status}): $(head -c 300 "${ACR_WORK}/${label}.err" | tr '\n' ' ')"
    return 1
  fi
  if [ ! -s "${ACR_WORK}/${label}.files" ]; then
    note "account-restore: snapshot '${label}' of the home is empty, so it observed nothing"
    return 1
  fi
  return 0
}

# acr_entries: how many lines snapshot $1's half $2 (files or db) holds.
acr_entries() { grep -c . "${ACR_WORK}/$1.$2" 2>/dev/null || true; }

# acr_compare_awk: the comparison itself. Prints "compared N" first, always (a comparison that
# printed nothing at all is a broken one, not an identical one), then one line per differing axis:
#   differs: <axis> x<count> (<up to three paths>)
# Arguments: -v kind=files|db, then the two snapshot files.
acr_compare_awk() {
  cat <<'AWK'
function note_difference(axis, key) {
  count[axis]++
  if (count[axis] <= 3) example[axis] = example[axis] (example[axis] == "" ? "" : ", ") key
}
FILENAME == ARGV[1] { first[$1] = $0; next }
{ second[$1] = $0 }
END {
  total = 0
  for (key in first) total++
  print "compared " total
  for (key in first) if (!(key in second)) note_difference("removed", key)
  for (key in second) if (!(key in first)) note_difference("added", key)
  for (key in first) {
    if (!(key in second)) continue
    split(first[key], x, "\t")
    split(second[key], y, "\t")
    if (kind == "files") {
      if (x[2] != y[2]) note_difference("type", key)
      if (x[3] != y[3]) note_difference("owner", key)
      if (x[4] != y[4]) note_difference("group", key)
      if (x[5] != y[5]) note_difference("mode", key)
      if (x[6] != y[6]) note_difference("size", key)
      if (x[7] != y[7]) note_difference("link", key)
      if (x[8] != y[8]) note_difference("content", key)
    } else {
      if (x[2] != y[2]) note_difference("rows", key)
      if (x[3] != y[3]) note_difference("content", key)
    }
  }
  split("removed added type owner group mode size link rows content", axes, " ")
  for (i = 1; i <= 10; i++) {
    axis = axes[i]
    if (count[axis] > 0) print "differs: " axis " x" count[axis] " (" example[axis] ")"
  }
}
AWK
}

# acr_compare: compares snapshots $1 and $2. Prints the differing axes. Returns 0 when nothing
# differs, 1 when something does, 2 when the comparison itself did not run (a missing snapshot, an
# awk that failed).
#   $1 = snapshot, $2 = snapshot, $3 = scope: files, db or all (default).
acr_compare() {
  local a="$1" b="$2" scope="${3:-all}" kind out differing=0 half
  local program
  program="$(acr_compare_awk)"
  for half in files db; do
    case "$scope" in all|"$half") ;; *) continue ;; esac
    [ -r "${ACR_WORK}/${a}.${half}" ] && [ -r "${ACR_WORK}/${b}.${half}" ] || { echo "no snapshot: ${a}.${half} or ${b}.${half}"; return 2; }
    kind="$half"
    out="$(awk -F'\t' -v kind="$kind" "$program" "${ACR_WORK}/${a}.${half}" "${ACR_WORK}/${b}.${half}" 2>&1)" \
      || { echo "awk failed on ${half}: ${out}"; return 2; }
    case "$out" in "compared "*) ;; *) echo "the comparison printed no 'compared' line: ${out}"; return 2 ;; esac
    if [ "$(printf '%s\n' "$out" | grep -c '^differs: ' || true)" -gt 0 ]; then
      differing=1
      printf '%s\n' "$out" | grep '^differs: ' | sed "s/^differs: /differs: ${half}: /"
    fi
  done
  return "$differing"
}

# acr_verdict: "identical", "differs" or "broken" for the comparison of $1 and $2, printing nothing.
#   $1, $2 = snapshots, $3 = scope.
acr_verdict() {
  local status=0
  acr_compare "$@" >/dev/null || status=$?
  case "$status" in 0) echo identical ;; 1) echo differs ;; *) echo broken ;; esac
}

# acr_same: scored: snapshots $2 and $3 are identical on every axis (within scope $4). On a
# difference the axes are printed, so a failure carries its reason.
#   $1 = what it asserts, $2, $3 = snapshots, $4 = scope.
acr_same() {
  local label="$1" a="$2" b="$3" scope="${4:-all}" out="" status=0
  out="$(acr_compare "$a" "$b" "$scope")" || status=$?
  case "$status" in 0) acr_expect "$label" identical identical ;; 1) acr_expect "$label" differs identical ;; *) acr_expect "$label" broken identical ;; esac
  [ "$status" -eq 0 ] || printf '%s\n' "$out" | sed 's/^/      /' >&2
}

# acr_differs: scored: snapshots $2 and $3 DIFFER, and the comparison names every axis in $4. This is
# the positive control of the whole proof: a restore check that has never been shown a difference it
# refuses is decoration.
#   $1 = what it asserts, $2, $3 = snapshots, $4 = space-separated axes that must be named.
acr_differs() {
  local label="$1" a="$2" b="$3" axes="$4" out="" status=0 axis missing=""
  out="$(acr_compare "$a" "$b")" || status=$?
  if [ "$status" -ne 1 ]; then
    acr_expect "$label" "$([ "$status" -eq 0 ] && echo identical || echo broken)" differs
    return 0
  fi
  for axis in $axes; do
    grep -qE "^differs: [a-z]+: ${axis} x" <<<"$out" || missing="${missing} ${axis}"
  done
  acr_expect "$label (names:${missing:- all of} ${axes})" "${missing:-named}" named
  [ -z "$missing" ] || printf '%s\n' "$out" | sed 's/^/      /' >&2
}

# acr_selftest_comparison: the comparison proved against synthetic snapshots, with no container. Every
# axis is planted once and must be named; a snapshot compared with itself must be accepted (the
# inverse control, because a comparison that refused everything would pass every test that only ever
# hands it a difference); a missing snapshot must be "broken" and not "identical". Runs before the
# stage touches a server, so a blind comparison cannot certify anything.
acr_selftest_comparison() {
  local own="" tab=$'\t' h1 h2 h3 base_files base_db
  if [ -z "$ACR_WORK" ]; then ACR_WORK="$(mktemp -d)"; own="yes"; fi
  h1="$(printf 'one' | sha256sum | cut -d' ' -f1)"
  h2="$(printf 'two' | sha256sum | cut -d' ' -f1)"
  h3="$(printf 'three' | sha256sum | cut -d' ' -f1)"
  base_files="$(printf '%s\n' \
    ".${tab}d${tab}1001${tab}1001${tab}750${tab}-${tab}-${tab}-" \
    "./a.txt${tab}f${tab}1001${tab}1001${tab}644${tab}3${tab}-${tab}${h1}" \
    "./b.bin${tab}f${tab}1001${tab}1001${tab}600${tab}3${tab}-${tab}${h2}" \
    "./empty${tab}d${tab}1001${tab}1001${tab}755${tab}-${tab}-${tab}-" \
    "./link${tab}l${tab}1001${tab}1001${tab}777${tab}-${tab}a.txt${tab}-")"
  base_db="$(printf '%s\n' \
    "app${tab}-${tab}-" \
    "app.notes${tab}3${tab}${h1}" \
    "app.tags${tab}2${tab}${h3}")"
  printf '%s\n' "$base_files" > "${ACR_WORK}/st_base.files"
  printf '%s\n' "$base_db" > "${ACR_WORK}/st_base.db"

  # The vacuity guard, on the axis that can go blind: the base has every entry type the mutations
  # below need something to hit.
  acr_expect "selftest: the synthetic home holds a directory, a regular file and a symlink" \
    "$(for t in d f l; do awk -F'\t' -v t="$t" '$2 == t { n++ } END { exit n ? 0 : 1 }' "${ACR_WORK}/st_base.files" && printf %s "$t"; done)" dfl

  # The inverse control first: a snapshot compared with a copy of itself is accepted.
  cp "${ACR_WORK}/st_base.files" "${ACR_WORK}/st_copy.files"
  cp "${ACR_WORK}/st_base.db" "${ACR_WORK}/st_copy.db"
  acr_expect "selftest: a snapshot compared with a copy of itself is identical" "$(acr_verdict st_base st_copy)" identical

  acr_selftest_files_axis mode "./a.txt" 5 777 mode
  acr_selftest_files_axis owner "./a.txt" 3 0 owner
  acr_selftest_files_axis group "./a.txt" 4 0 group
  acr_selftest_files_axis size "./a.txt" 6 4 size
  acr_selftest_files_axis content "./a.txt" 8 "$h3" content
  acr_selftest_files_axis link "./link" 7 b.txt link
  acr_selftest_files_axis type "./empty" 2 f type
  # A regular file's byte flipped without its size changing is a content difference and no other.
  awk -F'\t' -v OFS='\t' '$1 == "./b.bin" { $8 = "'"$h3"'" } 1' "${ACR_WORK}/st_base.files" > "${ACR_WORK}/st_flip.files"
  cp "${ACR_WORK}/st_base.db" "${ACR_WORK}/st_flip.db"
  acr_differs "selftest: a flipped byte (same size) is named as content" st_base st_flip "content"
  acr_expect "selftest: a flipped byte is not also named as a size difference" \
    "$(acr_compare st_base st_flip | grep -c ': size x' || true)" 0

  grep -v '^./a.txt' "${ACR_WORK}/st_base.files" > "${ACR_WORK}/st_gone.files"
  cp "${ACR_WORK}/st_base.db" "${ACR_WORK}/st_gone.db"
  acr_differs "selftest: a removed file is named" st_base st_gone "removed"
  grep -v '^./empty' "${ACR_WORK}/st_base.files" > "${ACR_WORK}/st_nodir.files"
  cp "${ACR_WORK}/st_base.db" "${ACR_WORK}/st_nodir.db"
  acr_differs "selftest: a removed EMPTY directory is named" st_base st_nodir "removed"
  { cat "${ACR_WORK}/st_base.files"; printf './extra.txt\tf\t1001\t1001\t644\t1\t-\t%s\n' "$h2"; } > "${ACR_WORK}/st_more.files"
  cp "${ACR_WORK}/st_base.db" "${ACR_WORK}/st_more.db"
  acr_differs "selftest: an added file is named" st_base st_more "added"

  cp "${ACR_WORK}/st_base.files" "${ACR_WORK}/st_row.files"
  awk -F'\t' -v OFS='\t' '$1 == "app.notes" { $3 = "'"$h2"'" } 1' "${ACR_WORK}/st_base.db" > "${ACR_WORK}/st_row.db"
  acr_differs "selftest: an altered row (same count) is named as content" st_base st_row "content"
  cp "${ACR_WORK}/st_base.files" "${ACR_WORK}/st_rows.files"
  awk -F'\t' -v OFS='\t' '$1 == "app.notes" { $2 = 4; $3 = "'"$h2"'" } 1' "${ACR_WORK}/st_base.db" > "${ACR_WORK}/st_rows.db"
  acr_differs "selftest: an added row is named as rows and content" st_base st_rows "rows content"
  cp "${ACR_WORK}/st_base.files" "${ACR_WORK}/st_notable.files"
  grep -v '^app.tags' "${ACR_WORK}/st_base.db" > "${ACR_WORK}/st_notable.db"
  acr_differs "selftest: a dropped table is named" st_base st_notable "removed"
  cp "${ACR_WORK}/st_base.files" "${ACR_WORK}/st_nodb.files"
  : > "${ACR_WORK}/st_nodb.db"
  acr_differs "selftest: a dropped database is named" st_base st_nodb "removed"

  acr_expect "selftest: a comparison against a snapshot that does not exist is broken, not identical" \
    "$(acr_verdict st_base st_missing)" broken
  acr_expect "selftest: the files scope ignores a database difference" "$(acr_verdict st_base st_row files)" identical
  acr_expect "selftest: the db scope ignores a file difference" "$(acr_verdict st_base st_gone db)" identical
  [ -z "$own" ] || { rm -rf -- "$ACR_WORK"; ACR_WORK=""; }
}

# acr_selftest_files_axis: plants one changed field of one line in the synthetic home and requires the
# comparison to name exactly the axis meant.
#   $1 = axis, $2 = path, $3 = field number, $4 = new value, $5 = axis the comparison must name.
acr_selftest_files_axis() {
  awk -F'\t' -v OFS='\t' -v p="$2" -v f="$3" -v v="$4" '$1 == p { $f = v } 1' \
    "${ACR_WORK}/st_base.files" > "${ACR_WORK}/st_${1}.files"
  cp "${ACR_WORK}/st_base.db" "${ACR_WORK}/st_${1}.db"
  acr_differs "selftest: a changed ${1} is named" st_base "st_${1}" "$5"
}

# acr_control: one live control. Plants a real change in the container, snapshots, requires the
# comparison against the reference snapshot to name the axes, then reverts it.
#   $1 = name, $2 = reference snapshot, $3 = axes that must be named, $4 = shell text planting the
#   change, $5 = shell text reverting it. Both texts are this harness's own constants, run as root in
#   the container.
acr_control() {
  local name="$1" reference="$2" axes="$3" plant="$4" revert="$5"
  if ! docker exec "$container" bash -c "$plant" >/dev/null 2>&1; then
    note "account-restore: the control '${name}' could not be planted, so the snapshot's view of it was not observed"
    return 0
  fi
  if acr_snapshot control; then
    acr_differs "live control: a real ${name} is seen by the snapshot" "$reference" control "$axes"
  fi
  docker exec "$container" bash -c "$revert" >/dev/null 2>&1 \
    || note "account-restore: the control '${name}' could not be reverted; the account is no longer what ${reference} recorded"
}

# The flipped-byte control, as text for `bash -c` with __F__ standing for the file. It saves the
# original byte, writes a different one at the same offset (the size does not change), and the
# revert writes the original back.
ACR_FLIP_PLANT='o=$(dd if=__F__ bs=1 skip=100 count=1 2>/dev/null | od -An -tx1 | tr -d " \n"); printf %s "$o" > /root/acr-original-byte; if [ "$o" = 58 ]; then n=59; else n=58; fi; printf "\\x$n" | dd of=__F__ bs=1 seek=100 conv=notrunc 2>/dev/null'
ACR_FLIP_REVERT='printf "\\x$(cat /root/acr-original-byte)" | dd of=__F__ bs=1 seek=100 conv=notrunc 2>/dev/null; rm -f /root/acr-original-byte'

# acr_sql_command: the text of a `bash -c` command running the statement $1 as root over the socket.
# The statement must hold no single quote; string literals in it use double quotes.
acr_sql_command() { printf "/usr/bin/mysql --protocol=socket --user=root -e '%s'" "$1"; }

# acr_live_controls: plants, one at a time and reverting each, a real change on every axis in the
# account's real home and database, and requires the snapshot to see it. Ends by requiring the
# account to be identical to the reference again, which proves every revert was exact.
#   $1 = reference snapshot, which must be of the seeded account (acr_seed's tree).
acr_live_controls() {
  local reference="$1" home="/home/${ACR_ACCOUNT}" uid gid
  uid="$(acr_x stat -c %u "$home")"
  gid="$(acr_x stat -c %g "$home")"
  acr_expect "the home's owner can be read (control for the owner and group reverts)" \
    "$([[ "$uid" =~ ^[0-9]+$ ]] && [[ "$gid" =~ ^[0-9]+$ ]] && echo readable || echo unreadable)" readable

  acr_control "chmod" "$reference" "mode" \
    "chmod 0777 '${home}/proof/alpha.txt'" "chmod 0644 '${home}/proof/alpha.txt'"
  acr_control "chown" "$reference" "owner" \
    "chown 0 '${home}/proof/alpha.txt'" "chown ${uid} '${home}/proof/alpha.txt'"
  acr_control "chgrp" "$reference" "group" \
    "chgrp 0 '${home}/proof/alpha.txt'" "chgrp ${gid} '${home}/proof/alpha.txt'"
  acr_control "flipped byte" "$reference" "content" \
    "${ACR_FLIP_PLANT//__F__/${home}/proof/binary.dat}" "${ACR_FLIP_REVERT//__F__/${home}/proof/binary.dat}"
  acr_control "removed file" "$reference" "removed" \
    "mv '${home}/proof/secret.txt' /root/acr-control-file" "mv /root/acr-control-file '${home}/proof/secret.txt'"
  acr_control "removed empty directory" "$reference" "removed" \
    "mv '${home}/proof/nested/deep/empty' /root/acr-control-dir" "mv /root/acr-control-dir '${home}/proof/nested/deep/empty'"
  acr_control "added file" "$reference" "added" \
    "touch '${home}/proof/extra.txt'" "rm -f '${home}/proof/extra.txt'"
  acr_control "retargeted symlink" "$reference" "link" \
    "ln -sfn elsewhere '${home}/proof/nested/link-to-alpha' && chown -h ${uid}:${gid} '${home}/proof/nested/link-to-alpha'" \
    "ln -sfn ../alpha.txt '${home}/proof/nested/link-to-alpha' && chown -h ${uid}:${gid} '${home}/proof/nested/link-to-alpha'"
  acr_control "altered row" "$reference" "content" \
    "$(acr_sql_command "update \`${ACR_DB}\`.notes set qty = qty + 1 where id = 1")" \
    "$(acr_sql_command "update \`${ACR_DB}\`.notes set qty = qty - 1 where id = 1")"
  acr_control "added row" "$reference" "rows content" \
    "$(acr_sql_command "insert into \`${ACR_DB}\`.tags values (\"control\", 1)")" \
    "$(acr_sql_command "delete from \`${ACR_DB}\`.tags where name = \"control\"")"

  if acr_snapshot control_end; then
    acr_same "after every control was reverted the account is identical to its reference (every revert was exact)" \
      "$reference" control_end
  fi
}

# acr_print_snapshot_blind_spots: what the snapshot cannot see, printed in the stage's own output.
acr_print_snapshot_blind_spots() {
  echo "    UNOBSERVED HERE: extended attributes, ACLs, file capabilities, SELinux labels and disk quota usage. The snapshot reads path, type, owner, group, mode, size, link target and content hash only."
  echo "    UNOBSERVED HERE: modification and access times (a restore does not promise them), hard-link sharing, and sparse-file holes."
  echo "    UNOBSERVED HERE: database objects other than base tables (views, routines, triggers, events), column-level grants, and the database user's password."
  echo "    UNOBSERVED HERE: a name holding a tab or a newline (the snapshot refuses such a home rather than listing it wrongly)."
}
