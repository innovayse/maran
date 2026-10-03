#!/usr/bin/env bash
# the agent unit's writable roots, and preflight's backup-space warning
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_the_agent_unit_declares_its_writable_roots: the systemd half, and the limits of
# asking a text file about a sandbox.
#
# Two properties, both of which a wrong answer would make invisible on a running host:
#
#   1. Every writable root the agent uses is named in ReadWritePaths=. This is checked in
#      full against agent_paths.rs by `maran structure` (check 20); what is added here is
#      the four paths today's two fixes touch, stated by name, so a build of this image
#      fails if one of them is dropped even where the agent's constant went with it.
#   2. EnvironmentFile= appears BEFORE Environment=PATH=. systemd applies these in file
#      order, so that ordering is what stops a PATH= line added to /etc/maran/agent.env
#      from winning against the deliberately empty PATH. Reverse the two lines and the
#      unit still parses, still starts and still looks right — the protection is the
#      ORDER, and nothing else in this repository observes it.
assert_the_agent_unit_declares_its_writable_roots() {
  local writable_set entry root covered
  writable_set="$(grep '^ReadWritePaths=' "$AGENT_UNIT" | head -1 | sed 's/^ReadWritePaths=//')"
  [ -n "$writable_set" ] \
    || fail "$(basename "$AGENT_UNIT") has no ReadWritePaths= line — this check cannot observe the unit's writable set"

  # The roots the agent actually writes, named by hand so that dropping one from the unit fails
  # this build even where the agent's own constant went with it.
  #
  # `/var/lib/maran` is deliberately NOT in this list, and the deletion is the point rather than a
  # tidy-up: nothing under agent/crates/*/src builds a path there, the directory belongs to the
  # unprivileged panel uid (owned by the service account and its group, 0750), and an entry granting the root daemon write access
  # to a directory the panel owns is the exact shape the scratch and the SFTP jail were RELOCATED
  # out of after measured escalations. It was removed from the unit, and the sentence this check
  # used to print about it — "the agent writes there" — was false.
  #
  # `/var/log/maran/sites` and `/var/lib/maran-sftp` are here because they were missing: the two
  # newest roots were absent from the list whose whole purpose is to notice an absent root. The
  # site log root is the most load-bearing of them, since the root nginx master opens every site's
  # access_log and error_log through it.
  local checked=0
  for root in \
    /home \
    /home/.maran-restore \
    /var/lib/maran-sftp \
    /var/lib/maran-scratch \
    /var/backups/maran \
    /var/log/maran/sites \
    /run/maran; do
    covered=0
    for entry in $writable_set; do
      entry="${entry#-}"
      case "$root" in
        "$entry"|"$entry"/*) covered=1 ;;
      esac
    done
    [ "$covered" -eq 1 ] \
      || fail "$(basename "$AGENT_UNIT") ReadWritePaths= does not cover ${root}, which the agent writes. Under this sandbox it would get EROFS at runtime on a real server, with a clean install behind it. If the agent has genuinely stopped writing there, remove the root from THIS list in the same change and say why — a grant nothing writes is a permanent one nobody revisits."
    checked=$((checked + 1))
  done
  # The vacuity guard, on the axis that can go blind: this loop is one list, a `for` over an empty
  # list exits 0, and a root deleted from it removes a check with no output changing anywhere.
  # Raise it with the list.
  [ "$checked" -eq 7 ] \
    || fail "the writable-root list checked ${checked} roots, not the 7 the agent writes. A root has been dropped from the list, and a check that is not in the list is not a check that failed — it is one that no longer exists."

  local environment_file_line path_line
  environment_file_line="$(grep -n '^EnvironmentFile=' "$AGENT_UNIT" | head -1 | cut -d: -f1)"
  path_line="$(grep -n '^Environment=PATH=' "$AGENT_UNIT" | head -1 | cut -d: -f1)"
  [ -n "$environment_file_line" ] \
    || fail "$(basename "$AGENT_UNIT") has no EnvironmentFile= line; the agent would start with no MARAN_AGENT_ALLOW_UID and deny the API every request"
  [ -n "$path_line" ] \
    || fail "$(basename "$AGENT_UNIT") has no 'Environment=PATH=' line; the daemon would inherit systemd's compiled-in PATH, which begins with the directories this product installs into"
  [ "$environment_file_line" -lt "$path_line" ] \
    || fail "$(basename "$AGENT_UNIT") sets Environment=PATH= on line ${path_line}, BEFORE EnvironmentFile= on line ${environment_file_line}. systemd applies these in file order, so in this order a PATH= line written into /etc/maran/agent.env overrides the empty PATH the unit intends."

  echo "maran-agent.service names all seven writable roots the agent uses, and loads EnvironmentFile= before it empties PATH."
  echo "UNOBSERVED HERE: this image boots no systemd. Nothing above ran ExecStartPre=, mounted a"
  echo "ReadWritePaths= sandbox, or applied UMask=0027 — those are claims about a booted host, and the"
  echo "two lines above are claims about a text file. What would settle them is one boot per family:"
  echo "'systemd-analyze verify maran-agent.service', then 'systemctl show -p ReadWritePaths -p UMask"
  echo "maran-agent.service' read back from the running manager, a file created by the daemon and"
  echo "stat'd for 0640, a write attempted outside the set and seen to fail with EROFS, and a scratch"
  echo "directory planted before a restart and seen to be gone after it."
}


# run_preflight_step: runs step 10's code THE WAY install.sh runs it — a plain command in a child
# shell with `set -euo pipefail`, the step file sourced, and MARAN_PANEL_PORT exported first
# because the step's `readonly MARAN_REQUIRED_PORTS="${MARAN_PANEL_PORT:?…}"` refuses to be sourced
# without it — and hands back its status and its output.
#
# The port comes from install.sh, never from a literal here, for the reason
# assert_panel_port_has_one_authority exists.
#
# The optional PATH prefix is how the free-space check is driven: `check_backup_space` reads the
# host with `df`, and a container's `df` reports whatever the build host happens to have free, so
# the only way to see BOTH of that function's branches is to control its answer. Same mechanism
# run_nginx_step uses for a hostile nginx and the staging assertion uses for `install(1)` — the
# STEP is the real one, unmodified; the tool it calls is the stand-in.
run_preflight_step() {
  local snippet="$1" path_prefix="${2:-}"
  local search_path="$PATH"
  [ -z "$path_prefix" ] || search_path="${path_prefix}:${PATH}"
  MARAN_PANEL_PORT="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")" \
    PATH="$search_path" \
    bash -c 'set -euo pipefail
. "$1"
. "$2"
eval "$3"' _ "$COMMON_LIB" "$PREFLIGHT_STEP" "$snippet"
}

# df_stand_in: writes, into directory `$1`, a `df` that reports `$2` MiB available and records that
# it was reached in `$1/df.ran`.
#
# The marker is not decoration. A PATH prefix that failed to take effect would leave the REAL `df`
# answering, and on a build host with little free space the "warns" case below would then pass
# without the stand-in having run at all — a refusal that never happened, scored as one. The same
# trap the step 50 staging assertion names.
#
# It prints the header line and one data line because that is what `df -P` guarantees and what the
# step's `awk 'NR==2 { print $4 }'` reads; anything else would be this script inventing a format the
# installer does not parse.
df_stand_in() {
  local directory="$1" available_mb="$2"
  cat > "${directory}/df" <<STAND_IN
#!/usr/bin/env bash
touch "${directory}/df.ran"
echo "Filesystem 1M-blocks Used Available Capacity Mounted-on"
echo "polygon-stand-in 102400 102400 ${available_mb} 100% /"
STAND_IN
  chmod 0755 "${directory}/df"
  rm -f "${directory}/df.ran"
}

# assert_preflight_warns_about_backup_space_without_refusing: step 10's backup free-space check,
# in all three of the states it can be in, driven through the installer's own function.
#
# Three properties, and the third is the one that would be a defect rather than a missing feature:
#
#   1. Below the floor it WARNS and names the number it measured. A check that says "not enough
#      space" without the figure is not actionable, and the plan asks for the number by name.
#   2. Above the floor it accepts. This is the inverse control rules/testing.md requires of every
#      refusing gate: a check mutated to warn unconditionally passes every test that only ever
#      hands it a starved filesystem.
#   3. It NEVER fails the install. `_PREFLIGHT_FAILED` is read back out of the child after the warn
#      case, because a warning that quietly sets that flag is a refusal wearing a warning's words,
#      and an operator who intends to mount a backup volume after installing would be locked out of
#      their own server by it.
#
# It also runs the function against the REAL `df` on the real host, with no stand-in, to prove the
# thing parses this family's actual `df -Pm` output — the stand-in's format is this script's belief
# about `df`, and a belief is not the tool.
assert_preflight_warns_about_backup_space_without_refusing() {
  local shim_dir output
  shim_dir="$(mktemp -d)"

  # 1. Starved: one mebibyte free, far under the step's floor.
  df_stand_in "$shim_dir" 1
  output="$(run_preflight_step 'check_backup_space; echo "PREFLIGHT_FAILED=${_PREFLIGHT_FAILED}"' "$shim_dir" 2>&1)" \
    || fail "check_backup_space exited non-zero on a starved filesystem; it is a warning and must never abort the step. It said: ${output}"
  [ -f "${shim_dir}/df.ran" ] \
    || fail "the df stand-in in ${shim_dir} was never executed, so check_backup_space measured the real host and the warning below would be about nothing"
  case "$output" in
    *"PREFLIGHT WARN"*) ;;
    *) fail "check_backup_space did not warn on a filesystem with 1 MiB free. It said: ${output}" ;;
  esac
  case "$output" in
    *"1 MiB free"*) ;;
    *) fail "check_backup_space warned without naming the number it measured, which is the one thing the warning is for. It said: ${output}" ;;
  esac
  case "$output" in
    *"/var/backups/maran"*) ;;
    *) fail "check_backup_space warned without naming the backup root, so an operator cannot tell which filesystem to enlarge. It said: ${output}" ;;
  esac
  case "$output" in
    *"PREFLIGHT_FAILED=0"*) ;;
    *) fail "check_backup_space set the preflight failure flag. It is documented as a warning: an operator who plans to mount a backup volume after installing would be refused an install by this. It said: ${output}" ;;
  esac

  # 2. Ample: comfortably over the floor. The gate must ACCEPT.
  df_stand_in "$shim_dir" 999999
  output="$(run_preflight_step 'check_backup_space' "$shim_dir" 2>&1)" \
    || fail "check_backup_space exited non-zero on a filesystem with plenty of space: ${output}"
  [ -f "${shim_dir}/df.ran" ] \
    || fail "the df stand-in in ${shim_dir} was never executed on the ample case"
  case "$output" in
    *"PREFLIGHT WARN"*) fail "check_backup_space warned about a filesystem with 999999 MiB free, so it warns whatever it is handed and the warning above proves nothing. It said: ${output}" ;;
    *"PREFLIGHT OK"*) ;;
    *) fail "check_backup_space said neither OK nor WARN on an ample filesystem: ${output}" ;;
  esac

  # 3. The real tool on the real host: whatever this build host has free, the function must parse
  #    this family's own `df -Pm` and produce one of its two verdicts rather than an empty number.
  output="$(run_preflight_step 'check_backup_space' 2>&1)" \
    || fail "check_backup_space exited non-zero against this family's real df: ${output}"
  case "$output" in
    *"PREFLIGHT OK"*|*"PREFLIGHT WARN"*) ;;
    *) fail "check_backup_space produced neither verdict against the real df on this family, so its awk does not read this df's output: ${output}" ;;
  esac
  case "$output" in
    *": 0 MiB free"*|*"only 0 MiB free"*)
      fail "check_backup_space read 0 MiB off this family's real df, which means its column arithmetic is wrong here rather than that the disk is full: ${output}" ;;
  esac

  rm -rf "$shim_dir"
  echo "Step 10's backup free-space check warns with the number, accepts an ample filesystem, never fails the install, and parses this family's real df."
  echo "UNOBSERVED HERE: three of the four cases above were answered by a df stand-in, not by a"
  echo "filesystem. What this image cannot show is the check on a host whose backup volume is"
  echo "mounted at /var/backups AFTER the install: step 10 runs before step 40 creates the"
  echo "directory, so it measures the nearest existing ancestor and its number is then about the"
  echo "wrong filesystem — labelled in the message, never corrected. What would settle it is one"
  echo "boot per family: a real volume mounted there, an install run, and the warning read back."
}

# The account directory and the two files planted under the backup root. Named here because the
# assertion below and its inverse control must plant and look for exactly the same things, and a
# check that hunts for a name nothing planted is the shape that reports a protection as held.
readonly PLANTED_BACKUP_ACCOUNT="/var/backups/maran/polykeep"
readonly PLANTED_BACKUP_ARTIFACT="${PLANTED_BACKUP_ACCOUNT}/00000000-0000-0000-0000-0000000000ff.tar.gz"
readonly PLANTED_BACKUP_SIDECAR="${PLANTED_BACKUP_ACCOUNT}/00000000-0000-0000-0000-0000000000ff.json"
readonly PLANTED_BACKUP_BYTES="polygon planted artifact — a customer's only copy"

# plant_a_backup_artifact: an account directory and two files under the REAL backup root, in the
# layout ops::backup writes (<root>/<account>/<id>.tar.gz plus its sidecar) and at the modes it
# writes them at. Real files at the real path, because the whole question is what a root process
# running `rm -rf` by name does to them.
plant_a_backup_artifact() {
  install -d -o root -g root -m 0700 /var/backups/maran
  install -d -o root -g root -m 0700 "$PLANTED_BACKUP_ACCOUNT"
  printf '%s\n' "$PLANTED_BACKUP_BYTES" > "$PLANTED_BACKUP_ARTIFACT"
  printf '{"id":"00000000-0000-0000-0000-0000000000ff"}\n' > "$PLANTED_BACKUP_SIDECAR"
  chmod 0600 "$PLANTED_BACKUP_ARTIFACT" "$PLANTED_BACKUP_SIDECAR"
  [ -f "$PLANTED_BACKUP_ARTIFACT" ] && [ -f "$PLANTED_BACKUP_SIDECAR" ] \
    || fail "the planted backup artifact was not written, so everything below would be measuring an empty directory"
}

# backup_root_survives: runs the uninstaller at <path> the way main() runs it — the deleters, in
# main()'s order — and answers whether the planted artifact is still there afterwards, byte for
# byte. Prints a diagnosis and returns 1 when it is not.
#
# A function that RETURNS rather than one that calls fail, because it is used twice and in opposite
# directions: once on the real uninstaller, which must survive it, and once on a copy carrying the
# one line this whole promise is about, which must not.
#
# `remove_var_lib` and `remove_logs` are the two deleters that run `rm -rf` on a named directory,
# which is where a fifth sibling would be added by whoever added it. `note_backups_kept` runs after
# them, in main()'s order, so its count is a count taken after the deleting is done.
backup_root_survives() {
  local uninstaller="$1" output status=0
  output="$(bash -c 'set -euo pipefail
. "$1"
remove_var_lib
remove_logs
note_backups_kept' _ "$uninstaller" 2>&1)" || status="$?"

  if [ "$status" -ne 0 ]; then
    printf 'the uninstaller exited %s while deleting; it said: %s\n' "$status" "$output"
    return 1
  fi
  if [ ! -d "$PLANTED_BACKUP_ACCOUNT" ]; then
    printf 'the uninstaller DELETED %s. That directory is a customer'"'"'s only copy of their files and their databases, and an uninstall is not a decommission. It said: %s\n' \
      "$PLANTED_BACKUP_ACCOUNT" "$output"
    return 1
  fi
  if [ ! -f "$PLANTED_BACKUP_ARTIFACT" ]; then
    printf 'the uninstaller DELETED the artifact %s while leaving its directory. It said: %s\n' \
      "$PLANTED_BACKUP_ARTIFACT" "$output"
    return 1
  fi
  if ! printf '%s\n' "$PLANTED_BACKUP_BYTES" | cmp -s - "$PLANTED_BACKUP_ARTIFACT"; then
    printf 'the artifact at %s survived the uninstall with different bytes in it\n' "$PLANTED_BACKUP_ARTIFACT"
    return 1
  fi
  printf '%s' "$output"
  return 0
}
