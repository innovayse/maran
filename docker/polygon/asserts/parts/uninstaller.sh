#!/usr/bin/env bash
# what uninstall.sh removes, and what it must keep
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_the_uninstaller_keeps_the_backup_root: the uninstaller leaves /var/backups/maran alone,
# says so naming the path and the count, and this assertion can tell the difference.
#
# The plan's words: an uninstaller that removes a customer's only copy of their data "is the worst
# defect this product could ship, and it would be one line". Today the line is absent by omission —
# `remove_var_lib` deletes three siblings of the backup root by name, and a fourth `rm -rf` beside
# them would read like its neighbours and nothing in this repository would have gone red for it.
#
# So this is proved in both directions, which for a promise about ABSENCE is not optional: every
# positive assertion here passes just as happily against an uninstaller that never had the promise,
# because the deletion is what has to be caught and there is none to catch. The inverse control puts
# that one line into a COPY of the uninstaller — verified landed with `cmp` and a grep count, since
# a mutation that silently failed to apply is how three mutations in this repository were scored
# blind — and requires the check above to refuse it, naming the path it lost.
#
# UNOBSERVED HERE: this is the uninstaller's shell functions against a planted file, not an
# uninstall of a real install. No systemd, no maran-api, no agent, no operator at a terminal, and
# `confirm` reaches no tty in a build layer so `remove_logs` takes its keep-the-data branch. What a
# booted host would have to show instead is one real install, one real backup taken through the
# panel, `bash uninstall.sh --yes`, and the artifact still on the disk afterwards with the
# transcript naming it.
assert_the_uninstaller_keeps_the_backup_root() {
  local mutant output diagnosis planted_lines restored

  plant_a_backup_artifact

  output="$(backup_root_survives "$UNINSTALLER")" \
    || fail "the real uninstaller did not leave the backup root alone: ${output}"
  case "$output" in
    *"/var/backups/maran"*) ;;
    *) fail "the uninstaller kept the backup root but never named it. An operator finishing a decommission has to be told where the data it refused to delete is: ${output}" ;;
  esac
  case "$output" in
    *"2 file(s)"*) ;;
    *) fail "the uninstaller did not report the two planted artifacts as a count; a report that says 'backups were kept' without saying how many is not something an operator can act on: ${output}" ;;
  esac

  # The inverse control: the one line, in a copy, verified to have landed before it is scored.
  mutant="$(mktemp)"
  sed 's|^  rm -rf /var/lib/maran$|  rm -rf /var/lib/maran\n  rm -rf /var/backups/maran|' \
    "$UNINSTALLER" > "$mutant"
  planted_lines="$(grep -c '^  rm -rf /var/backups/maran$' "$mutant" || true)"
  [ "$planted_lines" -eq 1 ] \
    || fail "the inverse control planted ${planted_lines} deletions of the backup root instead of exactly one — the anchor line in uninstall.sh has changed, and this control would have scored a mutation that never applied"
  if cmp -s "$UNINSTALLER" "$mutant"; then
    fail "the inverse control's copy of the uninstaller is identical to the original, so the deletion below never applied and the refusal it asks for would be about nothing"
  fi

  if diagnosis="$(backup_root_survives "$mutant")"; then
    rm -f "$mutant"
    fail "an uninstaller carrying 'rm -rf /var/backups/maran' PASSED this assertion, so the assertion cannot see the deletion it exists for and its green verdict above means nothing"
  fi
  case "$diagnosis" in
    *"$PLANTED_BACKUP_ACCOUNT"*) ;;
    *) fail "the inverse control was refused, but the diagnosis does not name what was lost: ${diagnosis}" ;;
  esac
  rm -f "$mutant"

  # Everything the mutant and the real deleters removed, put back by the installer's own steps
  # rather than by literals here: the backup root and the panel layout from step 40, the SFTP jail
  # base from step 86 (remove_var_lib deletes it, and the suites that follow this build need it).
  run_user_step 'create_directory_layout' >/dev/null \
    || fail "step 40's create_directory_layout could not restore the layout this assertion's deleters removed"
  run_installer_step 'install_sftp_prerequisites' >/dev/null \
    || fail "step 86's install_sftp_prerequisites could not restore the SFTP jail base this assertion's deleters removed"
  for restored in /var/backups/maran /var/lib/maran /var/lib/maran-scratch /var/lib/maran-sftp; do
    [ -d "$restored" ] \
      || fail "${restored} is missing after this assertion restored the layout; the image would ship without it and every suite that needs it would fail for the wrong reason"
  done
  rm -rf "$PLANTED_BACKUP_ACCOUNT"

  echo "The uninstaller keeps /var/backups/maran, names it and counts what is in it — and an uninstaller that deletes it is refused here."
  echo "UNOBSERVED HERE: this is the uninstaller's shell functions against a planted file, not an"
  echo "uninstall of a real install. No systemd, no maran-api, no agent, and no operator at a"
  echo "terminal — confirm() reaches no tty in a build layer, so remove_logs took its keep-the-data"
  echo "branch and the delete-the-logs branch is not exercised here at all. What would settle it is"
  echo "one real install per family, one real backup taken through the panel, bash uninstall.sh"
  echo "--yes, and the artifact still on the disk afterwards with the transcript naming it."
}
# ---------------------------------------------------------------------------------------------
# The uninstall census. See assert_the_uninstaller_removes_every_drop_in_the_installer_creates.
# ---------------------------------------------------------------------------------------------

# The directories this census is about, and the reason it is these six and not everything the
# installer touches.
#
# Every one of them is a directory the DISTRIBUTION owns, into which a product drops a file that
# some daemon then reads on a schedule or at boot: logrotate reads /etc/logrotate.d nightly from
# cron, systemd reads /etc/systemd/system and /etc/tmpfiles.d at boot, libpam reads /etc/pam.d per
# authentication, nginx reads /etc/nginx/conf.d on reload, sudo reads /etc/sudoers.d per command.
# So a file left behind here is not untidiness — it is a live effect on a host that no longer runs
# this product, and the effect is silent, which is why the defect that prompted this census
# (`/etc/logrotate.d/maran-panel`, installed by step 80 and removed by nothing) survived review:
# the policy carries `missingok`, so logrotate would have gone on reading it and saying nothing for
# years.
#
# What this scope deliberately CANNOT see, because widening it would force a wrong uninstaller:
#
# - /etc/maran, /usr/local/maran, /var/lib/maran*, /run/maran*: directories that are wholly ours.
#   The uninstaller removes them as trees, so a per-file census over them would enumerate files
#   with no removal line of their own and demand lines that must not exist.
# - /var/log/maran, /var/backups/maran, /home/<account>, the PostgreSQL database: the operator's
#   and the customer's data. The uninstaller keeps the backup root on purpose and prompts before
#   the rest, which is the behaviour assert_the_uninstaller_keeps_the_backup_root exists to
#   protect. A census that demanded removal here would be a census arguing for data loss.
# - /etc/ssh/sshd_config and the family's nftables configuration: files the installer EDITS rather
#   than creates. What must go from those is our BLOCK, not the file, and that is a different
#   property with its own checks (remove_sftp_sshd_block, and
#   assert_uninstaller_never_leaves_a_dangling_include for the firewall include).
readonly UNINSTALL_CENSUS_DROP_IN_DIRECTORIES="/etc/logrotate.d /etc/pam.d /etc/tmpfiles.d /etc/systemd/system /etc/nginx/conf.d /etc/sudoers.d"

# The register: drop-in paths the installer NAMES and the uninstaller must NOT remove, each with
# the reason it is kept.
#
# This is the half that keeps the census honest. "Everything the installer installs is removed" is
# a property this product must not have — it already keeps a backup root deliberately — so the
# property asserted is the weaker and truthful one: every path the census finds is EITHER removed
# by its exact literal OR named here with a reason. One line per entry, `path|reason`.
#
# It carries a staleness guard for the same reason `maran structure`'s exemption lists gained one
# after three of them were found naming files that no longer exist: an entry for a path the
# installer no longer names is a hole the next file of that name falls into, and it reads exactly
# like a considered decision. So every entry below must still be a path the census FINDS, and must
# not also be removed — a path that is registered as kept and deleted anyway means one of the two
# statements is a lie and a reader cannot tell which.
readonly UNINSTALL_CENSUS_KEPT_REGISTER="/etc/pam.d/vsftpd|the distribution's own PAM stack for the packaged vsftpd service. installer/lib/89-ftps.sh names it only to say it never edits it — this panel's daemon authenticates against /etc/pam.d/maran-ftps, a service of our own — so removing it would break a stock vsftpd this product never configured and may not have installed.
/etc/systemd/system/multi-user.target.wants|systemd's own enablement directory, which this installer reads and never creates. Step 89 looks in it to prove maran-ftps.service has no entry there; an rm at this path would un-enable every unit on the host."

# expand_installer_literals: one installer file's text with its own literal shell constants
# substituted, so a path written as ${MARAN_UNIT_DIR}/maran-api.service is readable as a path.
#
# Without this the census is blind to exactly the files that matter most. Step 70 writes the two
# systemd units and the tmpfiles snippet as ${MARAN_UNIT_DIR}/maran-api.service,
# ${MARAN_UNIT_DIR}/maran-agent.service and ${MARAN_TMPFILES_DIR}/${MARAN_API_TMPFILES_NAME} —
# not one of the three is a single literal, so a literal-only census would have enumerated ten
# drop-ins instead of thirteen and would have reported a clean tree while being unable to see the
# panel's own units at all. That is the census-reads-nothing failure mode, and it is why the
# expander carries its own planted control below.
#
# Only assignments whose value is a plain literal in the SAME file are resolved: no command
# substitution, no nested variable, no whitespace, no `|` (which would break the sed script this
# builds). A path a step COMPUTES — from the OS family, a loop variable, a `basename` — is
# therefore invisible to the census, and that is stated in its output.
expand_installer_literals() {
  local file="$1" script="" assignment name value
  while IFS= read -r assignment; do
    name="${assignment%%=*}"
    name="${name##* }"
    value="${assignment#*=}"
    value="${value%\"}"
    value="${value#\"}"
    case "$value" in
      ''|*'|'*) continue ;;
    esac
    script="${script}s|\${${name}}|${value}|g;s|\$${name}\\b|${value}|g;"
  done < <( { grep -oE '^[[:space:]]*(readonly[[:space:]]+)?[A-Z][A-Z0-9_]*="[^"$`]*"' "$file" || true; } )
  # An empty sed script is `sed ''`, which is a valid no-op, so a file declaring no constants
  # still comes out as itself rather than killing the run.
  sed "$script" "$file"
}

# installer_drop_in_paths: every path under the six drop-in directories named anywhere in the
# given installer files, one per line, de-duplicated.
#
# Comments included, deliberately, the same choice the FTPS log-path census makes: a comment
# naming a path is what the next person edits from, and a path that appears only in prose is
# either a file we place (and must remove) or somebody else's (and must be registered). Both are
# answers the census wants; neither is noise.
#
# The trailing `sed` strips sentence punctuation, because these paths are named inside English
# prose and `... never edits /etc/pam.d/vsftpd.` would otherwise enter the census one byte longer
# than the path it is about. The cost is the inability to see a drift to a path that genuinely
# ends in punctuation, which no installer here writes.
installer_drop_in_paths() {
  local directory file
  for file in "$@"; do
    local expanded
    expanded="$(expand_installer_literals "$file")"
    for directory in $UNINSTALL_CENSUS_DROP_IN_DIRECTORIES; do
      printf '%s\n' "$expanded" \
        | { grep -oE "${directory}/[A-Za-z0-9_.@-]+" || true; } \
        | sed 's/[.,;:)]\+$//'
    done
  done | sort -u | { grep -E '^/' || true; }
}

# uninstaller_removal_targets: every absolute path the uninstaller hands to `rm`, one per line.
#
# Line continuations are joined first — `rm -f a \` + newline + `b` is one command and `b` is one
# of its targets — and only lines whose FIRST word is `rm` are read. That second rule is not
# cosmetic: installer/uninstall.sh prints an operator runbook containing the words
# `rm -f /etc/systemd/system/<that unit>`, and a census that read an echo as a deletion would
# accept a path the uninstaller merely talks about.
uninstaller_removal_targets() {
  sed -e ':join' -e '/\\$/{N;s/\\\n//;bjoin' -e '}' "$UNINSTALLER" \
    | { grep -oE '^[[:space:]]*rm[[:space:]]+-[rf]+[[:space:]]+[^#]*' || true; } \
    | tr ' \t' '\n\n' \
    | { grep -E '^"?/' || true; } \
    | tr -d '"' \
    | sort -u
}

# assert_the_uninstaller_removes_every_drop_in_the_installer_creates: the census this repository
# owed, and the check that would have caught today's defect the day it was written.
#
# The defect: a fix added a THIRD rotation policy, installed by step 80. installer/uninstall.sh
# removed the other two by exact literal name and not the new one — three installed, two removed —
# and it was found by a person reading the file. Nothing in this repository compared the two lists;
# the only uninstall assertions covered a dangling firewall include and the backup root.
#
# Why a CENSUS and not "the three policies are removed". A check naming the three files agrees with
# the tree the day it is written and is silent on the fourth, which is the same defect one release
# later. So both sides are DERIVED: the installed side from the step list installer/install.sh
# itself runs, the removed side from the uninstaller's own `rm` lines. A drop-in added tomorrow by
# a step nobody here has read is caught BY NAME.
#
# What is asserted is not "everything installed is removed" — that property would force a wrong
# uninstaller, since this one keeps a backup root on purpose and prompts before customer data.
# It is: **every drop-in the installer names is either removed by its exact literal, or named in
# UNINSTALL_CENSUS_KEPT_REGISTER with a reason** — and the register cannot go stale, because every
# entry in it must still be a path the census finds.
#
# It enforces the CONVENTION and not merely the outcome. The path must appear as a whole
# `rm` ARGUMENT: `rm -f /etc/logrotate.d/maran-*` would not satisfy it, and that is deliberate. A
# glob in this uninstaller is dangerous — a peer proved the literal is not a prefix match by
# planting `/etc/logrotate.d/maran-panel-backup` and watching it survive, and a glob would have
# eaten an operator's own copy. A lane that wants a glob has to change this check and argue for it.
assert_the_uninstaller_removes_every_drop_in_the_installer_creates() {
  local censused=0 planted steps step_count step_file installer_files
  local installed removed path entry registered_path reason

  # 1. The step list, from the installer's own table of contents rather than from a list here. A
  # list written here would be the same "two named copies" arrangement this census replaces.
  steps="$( { grep -oE '^[[:space:]]*run_step[[:space:]]+[0-9]+-[A-Za-z0-9-]+\.sh' "$INSTALLER_ENTRY_POINT" || true; } | awk '{ print $2 }' | sort -u)"
  step_count="$(printf '%s' "$steps" | grep -c . || true)"
  # The vacuity guard on the axis that can go blind. `run_step` renamed, or the loop replaced by
  # something else, empties this list and every comparison below becomes a comparison over nothing.
  [ "$step_count" -ge 10 ] \
    || fail "only ${step_count} step files could be read out of ${INSTALLER_ENTRY_POINT}'s run_step lines, and this installer has more than ten steps. The census below would have enumerated the drop-ins of a handful of files and reported on all of them (rules/testing.md)"
  censused=$((censused + 1))

  # Its positive control: the extractor must see a step it has never seen, in a copy. Without
  # this, a `run_step` spelling that no longer matches returns a SHORTER list and the guard above
  # is the only thing between that and a census that quietly stopped reading a step.
  planted="/tmp/polygon-uninstall-census-steps.sh"
  { cat "$INSTALLER_ENTRY_POINT"; echo '  run_step 99-polygon-planted.sh step_polygon_planted'; } > "$planted"
  printf '%s\n' "$( { grep -oE '^[[:space:]]*run_step[[:space:]]+[0-9]+-[A-Za-z0-9-]+\.sh' "$planted" || true; } | awk '{ print $2 }')" \
    | grep -Fxq -- '99-polygon-planted.sh' \
    || fail "the step-list extractor read ${step_count} steps out of install.sh but does not read '99-polygon-planted.sh' out of a copy carrying exactly that run_step line — it is not reading the file's content, and the census's coverage below means nothing."
  rm -f -- "$planted"
  censused=$((censused + 1))

  # 2. Every one of those steps must be IN this image. A step the Dockerfile does not carry is a
  # step whose drop-ins this census cannot see, and a census with an invisible member is the
  # failure this whole block is written against — so it is a named build failure, never a skip.
  installer_files="$INSTALLER_ENTRY_POINT"
  for step_file in $steps; do
    require_installer_file "${INSTALLER_LIB}/${step_file}" "installer/lib/${step_file}"
    installer_files="${installer_files} ${INSTALLER_LIB}/${step_file}"
  done
  censused=$((censused + 1))

  # 3. The two controls on the extractor itself, before anything is compared. The first is the
  # ordinary planted one; the second is on the EXPANDER, and it is the one that matters here,
  # because without variable expansion this census silently loses the panel's own two systemd
  # units and its tmpfiles snippet while still printing a clean verdict.
  planted="/tmp/polygon-uninstall-census-step.sh"
  { cat "$FTPS_STEP"; echo '# polygon control: /etc/logrotate.d/maran-polygon-planted'; } > "$planted"
  installer_drop_in_paths "$planted" | grep -Fxq -- '/etc/logrotate.d/maran-polygon-planted' \
    || fail "the drop-in extractor does not read '/etc/logrotate.d/maran-polygon-planted' out of a copy of 89-ftps.sh carrying exactly that line — it is not reading the installer's content, and every agreement reported below is an agreement with nothing (rules/testing.md)."
  if installer_drop_in_paths "$FTPS_STEP" | grep -Fxq -- '/etc/logrotate.d/maran-polygon-planted'; then
    fail "the drop-in extractor reports '/etc/logrotate.d/maran-polygon-planted' for the UNMODIFIED 89-ftps.sh, which names it nowhere — it is answering from something other than the file, so its planted control above proves nothing."
  fi
  rm -f -- "$planted"
  censused=$((censused + 1))

  planted="/tmp/polygon-uninstall-census-expand.sh"
  {
    echo 'readonly ZZ_POLYGON_PLANTED_DIR="/etc/logrotate.d"'
    echo 'install -D -m 0644 x "${ZZ_POLYGON_PLANTED_DIR}/maran-polygon-expanded"'
  } > "$planted"
  installer_drop_in_paths "$planted" | grep -Fxq -- '/etc/logrotate.d/maran-polygon-expanded' \
    || fail "the drop-in extractor cannot resolve a destination written as \${VARIABLE}/name, so it is blind to every file installer/lib/70-services.sh places — both systemd units and the tmpfiles snippet are written that way. A census that cannot see the panel's own units would report this tree clean whatever the uninstaller did (rules/testing.md)."
  rm -f -- "$planted"
  censused=$((censused + 1))

  # 4. The two lists.
  installed="$(installer_drop_in_paths $installer_files)"
  removed="$(uninstaller_removal_targets)"

  # The two vacuity guards, one per list. Either list going empty makes every claim below trivially
  # true: an empty installed list has nothing to demand, and an empty removed list would fail
  # everything, which is the direction that at least announces itself — both are failures to
  # observe and neither is a verdict.
  local installed_count removed_count
  installed_count="$(printf '%s' "$installed" | grep -c . || true)"
  removed_count="$(printf '%s' "$removed" | grep -c . || true)"
  [ "$installed_count" -gt 0 ] \
    || fail "the census found NO path under ${UNINSTALL_CENSUS_DROP_IN_DIRECTORIES} in any of the ${step_count} installer steps, and this installer places a rotation policy, two systemd units, a tmpfiles snippet, a PAM stack and two nginx files. The installed list is empty, so 'every installed drop-in is removed' held over nothing (rules/testing.md)"
  [ "$removed_count" -gt 0 ] \
    || fail "no absolute path could be read out of any 'rm' line in ${UNINSTALLER}, so the removed list is empty and every path below would be reported as an omission. The extractor has stopped reading the uninstaller, which is a failure to observe and not a finding about the uninstaller (rules/testing.md)"
  censused=$((censused + 2))

  # 5. The register's staleness guard, and its contradiction guard.
  local register_count=0
  while IFS= read -r entry; do
    [ -n "$entry" ] || continue
    register_path="${entry%%|*}"
    reason="${entry#*|}"
    register_count=$((register_count + 1))
    [ -n "$reason" ] && [ "$reason" != "$register_path" ] \
      || fail "UNINSTALL_CENSUS_KEPT_REGISTER names ${register_path} with no reason. An exemption without a reason is one nobody can review or retire (rules/security.md)"
    printf '%s\n' "$installed" | grep -Fxq -- "$register_path" \
      || fail "UNINSTALL_CENSUS_KEPT_REGISTER exempts ${register_path} from removal and no installer step names that path any more — an exemption for a file the installer does not place is a hole the next file of that name falls into, and it reads exactly like a decision somebody made on purpose (rules/security.md)"
    if printf '%s\n' "$removed" | grep -Fxq -- "$register_path"; then
      fail "UNINSTALL_CENSUS_KEPT_REGISTER says ${register_path} is kept and installer/uninstall.sh removes it by that exact literal. One of the two is wrong and a reader cannot tell which: either the register entry is stale, or the uninstaller is deleting a file this repository has written down a reason to keep."
    fi
    censused=$((censused + 1))
  done < <(printf '%s\n' "$UNINSTALL_CENSUS_KEPT_REGISTER")
  # The register's own vacuity guard. An empty register would make the staleness loop above examine
  # nothing while reading like a clean list — the exact shape `maran structure`'s exemption guard
  # was given a control for.
  [ "$register_count" -ge 2 ] \
    || fail "UNINSTALL_CENSUS_KEPT_REGISTER read as ${register_count} entries, and it is written with two. The staleness guard examined no exemption at all, which is a failure to observe and not a clean register (rules/testing.md)"
  censused=$((censused + 1))

  # 6. The census proper. Removed by its exact literal, or registered as kept with a reason.
  #
  # `grep -Fxq` and not a substring test, and that is the trap a sibling check in this file fell
  # into: '/etc/logrotate.d/maran-panel' is a substring of '/etc/logrotate.d/maran-panel-backup',
  # so a substring match would accept an uninstaller that removes the wrong file and would also
  # accept a deliberately corrupted path. Whole line, whole word, nothing else.
  local omissions=0
  while IFS= read -r path; do
    [ -n "$path" ] || continue
    if printf '%s\n' "$removed" | grep -Fxq -- "$path"; then
      continue
    fi
    if printf '%s\n' "$UNINSTALL_CENSUS_KEPT_REGISTER" | grep -q "^${path}|"; then
      continue
    fi
    omissions=$((omissions + 1))
    echo "assert-installer-steps.sh: the installer names ${path} and installer/uninstall.sh neither removes it by that exact literal nor names it in UNINSTALL_CENSUS_KEPT_REGISTER with a reason for keeping it." >&2
  done < <(printf '%s\n' "$installed")
  [ "$omissions" -eq 0 ] \
    || fail "${omissions} drop-in file(s) the installer places are left on the host by installer/uninstall.sh, named above. Every one of these directories is read by a daemon on a schedule or at boot, so a leftover goes on having an effect on a host that no longer runs this product — silently, because a logrotate policy carries missingok and a tmpfiles snippet just recreates its directory. Either add 'rm -f <path>' to the uninstaller, or add the path to UNINSTALL_CENSUS_KEPT_REGISTER with the reason it must stay."
  censused=$((censused + 1))

  echo "The uninstall census: ${installed_count} drop-in path(s) named across install.sh and its ${step_count} steps, under ${UNINSTALL_CENSUS_DROP_IN_DIRECTORIES}, each of them either removed by installer/uninstall.sh as a whole rm ARGUMENT — ${removed_count} such paths read out of its own rm lines — or named in UNINSTALL_CENSUS_KEPT_REGISTER with a reason, with every register entry proved to still be a path the installer places. ${censused} checks made."
  printf '%s\n' "$installed" | sed 's/^/  installed: /'
  echo "UNOBSERVED HERE: this reads the installer's TEXT, not a host. A destination a step COMPUTES rather than declares — from the OS family, a loop, a basename — is invisible to it, and so is any drop-in outside the six directories above: /etc/maran, /usr/local/maran, /var/lib/maran*, /var/log/maran and /var/backups/maran are removed as trees or kept on purpose, and /etc/ssh/sshd_config and the family's nftables file are EDITED rather than created, so what must go from those is our block and not the file."
  echo "UNOBSERVED HERE: it asserts a removal LITERAL exists, not that the removal RUNS. An rm inside a branch that never fires, after an early exit, or in a function main() stopped calling passes this check unchanged — assert_the_uninstaller_keeps_the_backup_root is the one assertion here that executes the uninstaller's deleters, and it does so for a single path. Nor does it judge ORDER: a policy removed after the prompt that may have deleted the directory it rotates satisfies this."
}

# ---------------------------------------------------------------------------------------------
# Step 89 — FTPS: a daemon that is installed and does not listen.
#
# Everything below RUNS installer/lib/89-ftps.sh's own functions and then looks at what they
# left behind. Nothing here repeats the step's work: the package comes from the step's own
# `vsftpd_packages_for_family`, the group from `ensure_ftps_group`, the jail base from
# `ensure_ftps_directories`, the PAM stack from `install_ftps_pam_service`, and the unit and the
# rotation policy from `install_ftps_unit` and `install_ftps_logrotate`. An image that performed
# any of it itself and then asserted it would be asserting about the image.
#
# WHAT IS DELIBERATELY NOT ASSERTED HERE, and it is the most important sentence in this block:
# the package's own ENABLEMENT bookkeeping. "The package did not enable the packaged unit" looks
# like the obvious thing to check and it is the wrong control, because it is a statement about
# `deb-systemd-helper` on one family rather than about whether the daemon can start.
#
# The measurements, all taken 2026-09-09 in throwaway containers with `docker run --rm`, because
# this repository has had two contradictory readings of it written down:
#
#   ubuntu:24.04 and debian:trixie, /etc/systemd/system/vsftpd.service already a link to
#   /dev/null when `apt-get install vsftpd` runs — NO .wants entry is created, and
#   /var/lib/systemd/deb-systemd-helper-enabled holds no vsftpd.service.dsh-also record. The
#   helper checks for exactly that link and skips the enable.
#
#   The same two images with no mask — /etc/systemd/system/multi-user.target.wants/vsftpd.service
#   IS created, with the dsh-also record beside it.
#
# So on the Debian family the mask happens to suppress the enablement too. That is a side effect
# of one helper's behaviour, it differs from what the comment block in installer/lib/89-ftps.sh
# records, and NOTHING here is allowed to depend on it:
# a check asserting the absence of that symlink would be asserting on a package script, and would
# go red the day a distribution changes its helper without the daemon becoming any more startable.
#
# The control is the MASK — /etc/systemd/system/vsftpd.service as a link to /dev/null, shadowing
# the unit file the package ships, which is what makes the unit unstartable — plus the positive
# control that there IS a shipped unit under the shadow. The .wants state is PRINTED, on both
# branches, so the next reader sees what this host actually did instead of re-deriving it from a
# check that was never about it.

# ftps_unit_shipped_by_the_distribution: the path of the vsftpd unit the PACKAGE ships, or the
# empty string. Both families' locations are tried because the answer is a distribution fact:
# /lib/systemd/system on Debian, /usr/lib/systemd/system on RHEL, and the two are the same
# directory on a merged-usr Debian.
ftps_unit_shipped_by_the_distribution() {
  local candidate
  for candidate in /lib/systemd/system/vsftpd.service /usr/lib/systemd/system/vsftpd.service; do
    if [ -f "$candidate" ]; then
      printf '%s' "$candidate"
      return 0
    fi
  done
  printf ''
}

# ftps_wants_entries_for: every .wants entry naming a unit, one per line, or nothing.
# Used by the mask assertion to REPORT the Debian family's bookkeeping and by the
# switched-off assertion to REFUSE one naming maran-ftps.service, which are two different
# questions about the same kind of symlink.
ftps_wants_entries_for() {
  find /etc/systemd/system -name "$1" -path '*.wants/*' 2>/dev/null || true
}

# ftps_pam_directives: a PAM file with its comments and its blank lines removed.
#
# It exists because the file's own comments argue about the modules it does NOT use, so a search
# over the raw text finds the explanation and calls it a defect. Stripping the comments is what
# makes the aggregate-stack probe a question about the stack rather than about the prose.
ftps_pam_directives() {
  sed -e 's/#.*$//' -e '/^[[:space:]]*$/d' "$1"
}

# assert_file_is: one regular file against `uid:gid:mode`, the three questions asked separately
# so the diagnosis names which one failed, and a symbolic link at the name refused outright.
#
# The companion of assert_directory_is above, and it exists for the same reason: `install -D`
# exits 0 onto a symlink and applies the ownership and the mode to the link's TARGET. Two of the
# three files it is used on decide who may authenticate (the PAM stack) and what root executes
# (the unit), so a link at either name is a file somebody else chose.
assert_file_is() {
  local path="$1" expected="$2" found

  [ -e "$path" ] || [ -L "$path" ] \
    || fail "${path} was not created by the installer step that is supposed to create it"
  if [ -L "$path" ]; then
    fail "${path} is a SYMBOLIC LINK, not a regular file. Its owner and its mode are the link's, never the target's, and root reading or executing through it reads wherever it points."
  fi
  [ -f "$path" ] || fail "${path} exists but is not a regular file"

  found="$(stat -c '%u:%g:%a' "$path")"
  [ "$found" = "$expected" ] \
    || fail "${path} is uid:gid:mode ${found}, not ${expected} ($(stat -c '%U:%G' "$path") by name)"
}
