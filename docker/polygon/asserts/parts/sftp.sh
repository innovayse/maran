#!/usr/bin/env bash
# 86-sftp.sh: prerequisites, the Match block, and validation before replacement
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_sftp_prerequisites: the group, the jail base directory and exactly one
# Match block — after running the installer's function TWICE, because a re-run
# that duplicates the block is the failure this is here to catch.
assert_sftp_prerequisites() {
  run_installer_step 'install_sftp_prerequisites' || fail "install_sftp_prerequisites failed on its first run"
  run_installer_step 'install_sftp_prerequisites' || fail "install_sftp_prerequisites failed on its second run"

  getent group maran-sftp >/dev/null \
    || fail "the maran-sftp group was not created (DistroAdapter::sftp_group)"

  [ -d /var/lib/maran-sftp ] \
    || fail "the SFTP jail base directory /var/lib/maran-sftp was not created"

  local ownership_and_mode
  ownership_and_mode="$(stat -c '%U:%G:%a' /var/lib/maran-sftp)"
  [ "$ownership_and_mode" = "root:root:700" ] \
    || fail "/var/lib/maran-sftp is ${ownership_and_mode}, not root:root:700"

  # The directory's OWN mode was all this used to ask, and that is precisely how the defect
  # got past it: the base was root:root 0755 and correct, while the panel-owned
  # /var/lib/maran above it made sshd refuse every chroot. OpenSSH walks the whole path, so
  # the assertion has to walk it too.
  assert_ancestors_are_root_only /var/lib/maran-sftp

  local blocks
  # `|| true` because `grep -c` exits 1 when it counts NOTHING, and `set -e`
  # would then kill this script inside the command substitution — before the
  # named `fail` below is ever reached. Measured: with 86-sftp.sh's
  # install_sshd_match_block stubbed out, this script exited 1 and printed no
  # diagnosis at all, so the build failed without saying which installer step
  # had stopped working. An exit status is not evidence of which check fired.
  blocks="$(grep -c '^Match Group maran-sftp$' "$SSHD_CONFIG" || true)"
  [ "$blocks" -eq 1 ] \
    || fail "sshd_config carries ${blocks} 'Match Group maran-sftp' blocks after two runs, not 1"

  local directive
  for directive in \
    '^    ChrootDirectory %h$' \
    '^    ForceCommand internal-sftp$' \
    '^    AllowTcpForwarding no$' \
    '^    X11Forwarding no$'; do
    grep -q "$directive" "$SSHD_CONFIG" \
      || fail "the Match block is missing a directive matching: ${directive}"
  done

  sshd -t || fail "sshd rejects the config the installer produced"
}

# assert_sftp_validates_before_replacing: a broken sshd_config must survive the
# installer untouched rather than being replaced by a broken one with our block
# appended. Proved by breaking it on purpose, because "it validates first" is a
# claim about a failure path, and a failure path nothing exercises is a comment.
#
# An operator locked out of a server by an installer has lost the server, which
# is why this is asserted here and not left to a reading of the code.
assert_sftp_validates_before_replacing() {
  local pristine
  pristine="$(mktemp)"
  cp "$SSHD_CONFIG" "$pristine"
  printf 'ThisIsNotAnSshdDirective yes\n' >> "$SSHD_CONFIG"

  if run_installer_step 'install_sshd_match_block' >/dev/null 2>&1; then
    cp "$pristine" "$SSHD_CONFIG"
    rm -f "$pristine"
    fail "install_sshd_match_block replaced a config that 'sshd -t' rejects"
  fi
  grep -q '^ThisIsNotAnSshdDirective yes$' "$SSHD_CONFIG" \
    || fail "install_sshd_match_block modified sshd_config despite failing validation"

  cp "$pristine" "$SSHD_CONFIG"
  rm -f "$pristine"
  sshd -t || fail "the pristine sshd_config was not restored"
  echo "install_sshd_match_block left an invalid sshd_config untouched, as it must."
}

# run_nginx_step: runs step 80's code THE WAY install.sh runs it — a plain command in a child
# shell with `set -euo pipefail`, the step file sourced, and the values install.sh decides
# exported into the environment first — and hands back its status and its output.
#
# A CHILD PROCESS, for the reason run_installer_step gives at length: `exit` and `set -e` mean
# different things inside this script's `if` than they do in the installer. Step 80 aborts a
# refused install with `exit 1`, which is observable across a process boundary and is invisible
# inside a `( … )` used as an `if` condition.
#
# 80-nginx.sh alone rather than added to run_installer_step's list: the panel port and the api
# socket are environment step 80 needs and the other four steps do not, and step 80's top-level
# `readonly` declarations have no business in their children.
#
# The port and the socket come from install.sh, never from literals here, for the same reason
# assert_panel_port_has_one_authority exists: a check that carries its own copy of a value stops
# following the one place that decides it.
run_nginx_step() {
  local snippet="$1" path_prefix="${2:-}"
  local search_path="$PATH"
  # An optional directory placed AHEAD of the step's PATH. One case needs to interfere with a
  # tool the step runs rather than with the step itself — see
  # nginx_shim_that_interrupts_the_validation — and passing it here keeps every other case
  # driving the step through exactly the same environment install.sh gives it.
  [ -z "$path_prefix" ] || search_path="${path_prefix}:${PATH}"
  MARAN_PANEL_PORT="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")" \
    MARAN_API_SOCKET_PATH="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")" \
    MARAN_GROUP="$POLYGON_SERVICE_GROUP" \
    LIB_DIR="$INSTALLER_LIB" \
    PATH="$search_path" \
    bash -c 'set -euo pipefail
. "$1"
. "$2"
eval "$3"' _ "$COMMON_LIB" "$NGINX_STEP" "$snippet"
}

# nginx_shim_that_interrupts_the_validation: writes, into the directory `$1`, an `nginx` that
# kills the shell which ran it with signal `$2` and then does what the real one would have done.
#
# It is how this script produces the interrupt an operator actually produces — Ctrl-C at the
# terminal, an SSH session that drops, an OOM kill of `sudo bash install.sh` — reduced to the
# one instant that matters: after the candidate has been renamed onto the served path and
# before the step has decided anything about it. `$PPID` inside the shim is the step's own
# shell, because `nginx -t` runs as its direct child inside the step's `if`.
#
# `kill` FIRST and then the real binary, deliberately: bash defers a trapped signal until the
# foreground command finishes, so the validation really runs, really prints its verdict, and the
# step's shell meets the signal at the point in the window where a real interrupt would arrive.
# With KILL there is nothing to defer and the shell dies at once, which is the other case below.
#
# IT FIRES ONCE, like the one Ctrl-C an operator presses, and that is load-bearing rather than
# tidy. Step 80's restoration asks `nginx -t` whether the tree loads with the file it is about to
# put back — that is the check this suite's newest case exists for — and a shim that killed on
# every `-t` would kill that verification too: measured, the step then died of the SHIM's signal
# in the middle of its handler, so the exit-status check below would have read 143 off the shim
# rather than off the step's own re-raise, and the mutant that deletes the re-raise would have
# stayed green.
#
# The real binary's absolute path is resolved HERE and written into the shim, because the shim
# runs with its own directory first on PATH and a plain `nginx` inside it would call itself.
nginx_shim_that_interrupts_the_validation() {
  local directory="$1" signal="$2" real
  real="$(command -v nginx)"
  cat > "${directory}/nginx" <<EOF
#!/bin/sh
if [ "\$1" = -t ] && [ ! -e "${directory}/fired" ]; then
  : > "${directory}/fired"
  kill -${signal} "\$PPID"
fi
exec ${real} "\$@"
EOF
  chmod 755 "${directory}/nginx"
}

# nginx_service_records: which of the polygon systemctl's records for the nginx unit exist, as a
# readable list, or nothing at all when the service was left alone.
#
# All in one reader, because "the service was never touched" is one question and asking it several
# different ways in several places is several chances to ask it weakly. Three things are what a
# container can observe: `enable` writes the enablement file, `reload` writes the reload file, and
# `start`/`restart`/`stop` write the ActiveState one. The reload one is the one that used to be
# missing, and it is the one that matters most here: a reload is the single event by which a
# configuration file reaches a RUNNING server, and it changes neither of the other two.
#
# BOTH SPELLINGS OF THE UNIT, and that is a fix rather than thoroughness. systemd treats `nginx`
# and `nginx.service` as one unit; the polygon's systemctl deliberately does not — it keeps one
# state file per literal name, because normalising them would rename the files the agent's own
# monitor suite writes (docker/polygon/stand-ins/systemctl-stand-in.sh says so at length). So a gate reading
# only the short name is blind to a step that reloads `nginx.service`, and measured: with the
# step's own reload line moved inside the swap window and spelled `nginx.service`, this assertion
# passed green on both families while a vhost `nginx -t` had never seen reached the running server.
# Both spellings work identically on a real host and installer/lib/70-services.sh already uses the
# suffixed form for the panel's own units, so the next edit to step 80 could pick either.
nginx_service_records() {
  local records="" unit
  for unit in nginx nginx.service; do
    [ ! -e "${UNIT_STATE_DIRECTORY}/${unit}.enabled" ] || records="${records} enabled"
    [ ! -e "${UNIT_STATE_DIRECTORY}/${unit}.reloaded" ] || records="${records} reloaded"
    [ ! -e "${UNIT_STATE_DIRECTORY}/${unit}" ] || records="${records} started-or-restarted"
  done
  printf '%s' "${records# }"
}

# forget_nginx_service_records: remove every record nginx_service_records reads, both spellings.
#
# Beside that reader rather than spelled out at each case, and for the same reason it reads both
# names: a case that cleared only the short spelling would leave a `nginx.service` marker written
# by the case before it, and the next case's "the service was never touched" would go red for
# somebody else's reason — the mirror of the blindness this pair was written to fix.
forget_nginx_service_records() {
  local unit
  for unit in nginx nginx.service; do
    rm -f "${UNIT_STATE_DIRECTORY}/${unit}.enabled" \
      "${UNIT_STATE_DIRECTORY}/${unit}.reloaded" \
      "${UNIT_STATE_DIRECTORY}/${unit}"
  done
}

# prepare_host_for_the_nginx_step: the state a real server is in by the time step 80 runs, taken
# from the installer's own step 40 rather than invented here.
#
# Step 80 needs exactly two things from that step: the service group, because it installs the
# panel's private key root:<that group>, and /var/log/maran, because the vhost names it in access_log
# and error_log and `nginx -t` opens both. The group is created by step 40's own function. The
# directory is made here with step 40's own owner, group and mode instead of by calling
# create_directory_layout, and that is stated rather than hidden: that function also re-modes
# /run/maran to 0750 root:<service group>, which this image sets 0755 on purpose for the php-pool suites,
# and step 80 never reads it.
#
# `-o root` and not the service account, matching step 40 since the log-directory split: the two files the
# vhost names here are opened by the ROOT nginx master, so the directory holding them is root's
# and the panel writes under /var/log/maran/panel instead
# (docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md). Preparing the
# host the OLD way would leave `nginx -t` passing here against an ownership no install produces.
prepare_host_for_the_nginx_step() {
  run_user_step 'create_service_user >/dev/null
install -d -o root -g "$MARAN_GROUP" -m 0750 /var/log/maran'
}

# nginx_reads_configuration_file: whether nginx's own dump of the configuration it loads names
# `$1`.
#
# `nginx -T` prints one `# configuration file <path>:` line per file it actually parsed, so this
# asks NGINX which files it reads instead of asking a glob written here whether it thinks it
# matches. That is the exact question the defect below turned on, and nothing this script could
# compute about include patterns answers it as well as the binary that follows them.
#
# The dump is read into a variable and matched with `case` rather than piped into `grep -q`, and
# the mechanism is written down with the measurement that produced it, because this check was
# already once fixed with an explanation that did not survive being tested.
#
# `grep -q` exits at its first match and closes the read end of the pipe. `nginx` is usually still
# writing — the matched line is `# configuration file <path>:`, and every byte of that file and of
# every file included after it comes AFTER it — so nginx's next `write` takes SIGPIPE and exits
# 141. Under this script's `set -o pipefail` the pipeline's status is that death rather than grep's
# answer, and the function reports "nginx does not read this file" about a file nginx has just been
# seen reading.
#
# Measured, both polygon images, `PIPESTATUS` captured in the same run as the failure:
#
#   alma9, 300 runs of the piped form:   26 wrong answers, every one of them `nginx=141 grep=0`
#                                        — grep FOUND the line and pipefail overrode it
#   dump 14 946 bytes, of which 6 988 remained to be written after the matched line
#   ubuntu24, 300 runs:                  0 wrong answers in that run, 12/200 and 23/200 in others
#                                        — it is a race, and its rate is not stable
#   both families, padding added under conf.d so 300 KB follows the match: 200/200 wrong, and the
#   form used below: 0/200
#
# What this does NOT depend on is the dump exceeding the 64 KiB pipe buffer: a write to a pipe
# whose reader has closed fails whether or not the writer would have blocked. That was the reason
# offered when this was first fixed, and it is wrong — it predicts 0 failures at these images'
# 15-17 KiB dumps, and the measurement above is 26 in 300. Recorded so that the next person to see
# this function flake is not sent to look at buffer sizes.
nginx_reads_configuration_file() {
  local dump
  dump="$(nginx -T 2>/dev/null || true)"
  case "$dump" in
    *"# configuration file $1:"*) return 0 ;;
  esac
  return 1
}

# nginx_gate_outcome: drives step 80 through the five cases below and prints what went wrong, or
# prints nothing at all. It is separate from the assertion so that every way out of it passes
# through the one restore of the shipped template and the host in the caller.
#
# `$1` is the path step 80 serves the vhost from, obtained from the step's own nginx_conf_dest.
# stamp_as_maran_vhost: rewrite `$1` so that step 80's vhost_is_ours accounts for it — the marker
# `$2` on line 1, carrying the SHA-256 of every byte after it, replacing whatever line 1 held.
#
# Cases below need BOTH answers from that predicate on demand, and neither can be produced by hand:
# a file this installer wrote is one whose digest matches, and there is no way to write one without
# computing the digest. The marker prefix is read out of the STEP's own constant rather than spelled
# again here, for the same reason the two suffixes are — a check carrying its own copy of a value
# stops following the one place that decides it.
stamp_as_maran_vhost() {
  local file="$1" prefix="$2" body digest
  body="$(mktemp)"
  tail -n +2 -- "$file" > "$body"
  digest="$(sha256sum "$body" | cut -d' ' -f1)"
  { printf '%s%s\n' "$prefix" "$digest"; cat "$body"; } > "$file"
  rm -f "$body"
}

# swap_recorder: writes, into the directory `$1`, a `sync` and an `nginx` that each append what they
# were asked to do to `$1/log`, in order, and then do what the real ones would have done.
#
# It is how this script observes a durability the filesystem gives no way to read back. `sync FILE`
# and `sync DIRECTORY` are coreutils' fsync of exactly those objects — traced on alma9, each issues
# one `fsync(3)` — so "the served vhost and its directory were fsynced" is answerable by recording
# which operands the step passed. Nothing else in this suite can see the difference between a step
# that flushes the file it installs and one that flushes only the copy beside it, which is how a
# header comment claiming the whole of rules/rust.md's protocol survived three rounds over a swap
# that was a plain `install` and `mv`.
#
# `nginx` IS RECORDED TOO, and it is what makes the recording mean anything. A check that merely
# asked whether `/etc/nginx/conf.d` appears among the operands passes on a step that never syncs it
# around the swap at all, because the rollback copy's own write and its later removal sync that same
# directory twice more: measured, deleting the directory sync from the swap left that check GREEN.
# The property is not "this directory was synced at some point", it is "the staged file and then its
# directory were flushed, and only then was the vhost validated" — an ordering, which needs the
# validation in the same log to be visible at all.
#
# The real binaries' absolute paths are resolved HERE and written in, because the shims run with
# their own directory first on PATH and a plain `sync` or `nginx` inside one would call itself.
swap_recorder() {
  local directory="$1" real_sync real_nginx
  real_sync="$(command -v sync)"
  real_nginx="$(command -v nginx)"
  cat > "${directory}/sync" <<EOF
#!/bin/sh
for operand in "\$@"; do
  printf '%s\n' "\$operand" >> "${directory}/log"
done
exec ${real_sync} "\$@"
EOF
  cat > "${directory}/nginx" <<EOF
#!/bin/sh
printf 'nginx %s\n' "\$*" >> "${directory}/log"
exec ${real_nginx} "\$@"
EOF
  chmod 755 "${directory}/sync" "${directory}/nginx"
}

nginx_gate_outcome() {
  local dest="$1"
  # A directive nginx has never had, appended at the END of the template so that breaking it does
  # not depend on the vhost's internal shape, and distinctive enough to be searched for by name
  # across the whole configuration tree afterwards.
  local broken="this_directive_is_not_nginxs_and_never_was"
  # The three files the polygon's systemctl writes for the nginx unit. They are named here and
  # read through nginx_service_records below; the reload one is the newest and the reason this
  # function was rewritten — the suite used to read the enablement file alone and call it "the
  # service never touched".
  local enabled_marker="${UNIT_STATE_DIRECTORY}/nginx.enabled"
  local reload_marker="${UNIT_STATE_DIRECTORY}/nginx.reloaded"
  local state_marker="${UNIT_STATE_DIRECTORY}/nginx"
  # The two names the step swaps the vhost through, taken from the STEP's own constants rather
  # than written again here: a check carrying its own copy of a value stops following the one
  # place that decides it, which is the same reason assert_panel_port_has_one_authority exists.
  local candidate_suffix previous_suffix adopted_suffix foreign_suffix marker_prefix
  candidate_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_CANDIDATE_SUFFIX"')"
  previous_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_PREVIOUS_SUFFIX"')"
  adopted_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_ADOPTED_SUFFIX"')"
  foreign_suffix="$(run_nginx_step 'printf %s "$MARAN_VHOST_FOREIGN_SUFFIX"')"
  marker_prefix="$(run_nginx_step 'printf %s "$MARAN_VHOST_MARKER_PREFIX"')"
  local output rendered good shim records status

  # 1. THE POSITIVE CONTROL, and it is not optional. A gate that refuses everything satisfies
  #    both negative cases below and fails only here; without this half the suite could not tell
  #    a working gate from one that had stopped accepting the product's own configuration.
  forget_nginx_service_records
  if ! output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    printf '%s\n' "step 80 refused the panel vhost this repository SHIPS, so the installer cannot finish on
this family at all:
${output}"
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    printf '%s\n' "step 80 reported success and left an nginx configuration that does not load:
$(nginx -t 2>&1)"
    return
  fi
  # The proposition the whole defect was about: the file that is now served is a file nginx
  # actually opens. Staged as `maran.conf.staging`, it was not — and `nginx -t` passed anyway.
  if ! nginx_reads_configuration_file "$dest"; then
    printf '%s\n' "step 80 reported success, but nginx's own dump of the configuration it loads does not name
${dest}. The installed vhost is a file nginx never opens, which is the defect itself: the thing
validated and the thing served are two different files."
    return
  fi
  # And the other direction: what is served is byte-for-byte what was rendered, so nothing
  # between the render and the served path can rewrite the vhost after it was validated.
  rendered="$(mktemp)"
  if ! run_nginx_step "render_vhost '${rendered}'" >/dev/null 2>&1; then
    rm -f "$rendered"
    printf '%s\n' "render_vhost failed on the shipped template outside step_nginx, so this check cannot compare
what step 80 served with what it rendered."
    return
  fi
  if ! cmp -s "$rendered" "$dest"; then
    rm -f "$rendered"
    printf '%s\n' "the file step 80 left at ${dest} is not byte-for-byte what render_vhost produces from the
shipped template, so what 'nginx -t' accepted and what nginx serves are two different files."
    return
  fi
  rm -f "$rendered"
  if [ ! -e "$enabled_marker" ]; then
    printf '%s\n' "step 80 finished without enabling nginx. The negative cases below read the ABSENCE of
${enabled_marker} as 'the install stopped before it touched the service', and a step that never
enables anything at all would make every one of them pass for a reason that is not the one they
name."
    return
  fi
  if [ ! -e "$reload_marker" ]; then
    printf '%s\n' "step 80 finished without reloading nginx: the new vhost is on disk and the running server was
never told about it. The negative cases below read the ABSENCE of ${reload_marker} as 'the install
stopped before it reached the service', which is the check that catches a reload INSIDE the swap
window — the one way a configuration 'nginx -t' has not seen reaches a running server. A step that
never reloads anything would retire that question instead of asking it."
    return
  fi
  # The third record the absence checks read is the ActiveState one, written by `start`, `restart`
  # and `stop`. A successful step_nginx never reaches those verbs in this image — its
  # `systemctl reload nginx` succeeds here, so the `|| systemctl restart nginx` fallback does not
  # run — so the file is proved WRITABLE here rather than left as an absence nothing could have
  # produced. That fallback is the dangerous half of the step's reload line on a real host: a
  # restart against a vhost `nginx -t` has not seen stops an nginx that then does not come back.
  rm -f "$state_marker"
  systemctl restart nginx
  if [ ! -e "$state_marker" ]; then
    printf '%s\n' "the polygon's systemctl did not record a restart of nginx at ${state_marker}, so every check
below that reads its absence cannot fail for the reason it names. Fix the stand-in, not the check."
    return
  fi
  rm -f "$state_marker"

  # The vhost now in place is the one a re-install has to roll back to.
  good="$(mktemp)"
  cp -p "$dest" "$good"

  # 1a. THE DURABILITY REACHES THE FILE THAT IS SERVED, not only the copy beside it.
  #
  #     rules/rust.md "Config writes: render → swap → validate", step 3: fsync the temporary file
  #     AND its containing directory, "so a crash cannot leave a rename pointing at unflushed
  #     bytes". Step 80 spent three rounds giving that treatment to <dest>.previous alone while the
  #     served vhost went in through a bare `install` and `mv`, under a header comment claiming the
  #     whole protocol and a doc comment citing the rule by line number. The failure is not exotic:
  #     a SUCCESSFUL install, then a power cut inside the ext4 writeback window, leaves the
  #     directory entry for the panel vhost pointing at unflushed data — a zero-length or
  #     null-padded file — and nginx will not START. This same nginx serves every customer site on
  #     the host through maran-sites.conf, so it is the whole box at the next boot, with the
  #     rollback copy already deleted because the install had succeeded.
  #
  #     Observed through a recording `sync`, because a filesystem gives no way to read back whether
  #     an fsync happened. What is required is the pair: the staged file before the rename, and the
  #     directory the rename commits in.
  local recorder log log_one_line wanted
  recorder="$(mktemp -d)"
  swap_recorder "$recorder"
  forget_nginx_service_records
  if ! output="$(run_nginx_step 'step_nginx' "$recorder" 2>&1)"; then
    rm -rf "$recorder"
    rm -f "$good"
    printf '%s\n' "step 80 failed while its durability was being observed, so this case proves nothing:
${output}"
    return
  fi
  log="$(cat "${recorder}/log" 2>/dev/null || true)"
  rm -rf "$recorder"
  # The three events of the swap, adjacent and in this order: flush the staged file, flush the
  # directory the rename commits in, then validate. Adjacency is what stops each line being
  # satisfied by some other part of the step — the rollback copy's own write and its removal both
  # sync this same directory, and the whole point of the case is to tell those apart from the swap.
  # Joined onto one line first, and that is a correction rather than a style: `grep -F` given a
  # multi-line pattern treats each line as a SEPARATE pattern and matches any one of them, so the
  # sequence check written that way passed on both fsync mutants — measured. `|` appears in no path
  # and in no nginx argument this step passes, so it is safe as the joiner.
  log_one_line="$(printf '%s|' "$log" | tr '\n' '|')"
  wanted="${dest}${candidate_suffix}|$(dirname "$dest")|nginx -t|"
  if [ "${log_one_line#*"$wanted"}" = "$log_one_line" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 did not flush the vhost it installs the way rules/rust.md 'Config writes: render →
swap → validate' step 3 requires: fsync the staged file AND its containing directory, 'so a crash
cannot leave a rename pointing at unflushed bytes'. Expected these three, adjacent and in order:
${wanted}
What the step actually did, in order (every fsync operand and every nginx invocation, '|'-joined):
${log_one_line:-(nothing)}
A successful install followed by a power cut inside the writeback window then leaves the directory
entry for the panel vhost pointing at unflushed data — a zero-length or null-padded file — and
nginx will not START. This nginx serves every customer site on the host through maran-sites.conf,
so it is the whole box at the next boot, and the rollback copy is already gone because the install
succeeded. Three rounds of this step gave that treatment to the rollback copy alone, under a header
comment claiming the whole protocol."
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the durability observation changed what step 80 serves; the cases below can no longer start
from the vhost case 1 installed."
    return
  fi

  # 1b0. AN OPERATOR'S OWN BACKUP UNDER THE STEP'S OWN NAME, AND AN INSTALL THAT SUCCEEDS.
  #
  #      `maran.conf.previous` is the name a human picks for a backup, and the step's own comment
  #      has conceded that since round 2. Round 3's answer was to treat whatever sits there as its
  #      working state: it copied the served vhost OVER the operator's file and then deleted it when
  #      the install succeeded. The operator's backup was gone, with no message naming it.
  #
  #      The step now decides that by PROVENANCE — vhost_is_ours, the marker its own render_vhost
  #      stamps and the digest of the bytes after it — rather than by anything about the state of
  #      the host. A file it cannot account for is moved aside and never deleted, by this step or by
  #      uninstall.sh. This case pins the predicate from the side case 4 cannot: case 4 fails if the
  #      predicate is always FALSE (a real leftover stops being preferred), and this one fails if it
  #      is always TRUE (a stranger's file is consumed and deleted).
  local backup_copy
  cp -p "$good" "${dest}${previous_suffix}"
  printf '\n# an operator backup, taken by hand before touching anything\n' >> "${dest}${previous_suffix}"
  backup_copy="$(mktemp)"
  cp -p "${dest}${previous_suffix}" "$backup_copy"
  forget_nginx_service_records
  if ! output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good" "$backup_copy"
    printf '%s\n' "step 80 refused to finish an install because a file it did not write was sitting at
${dest}${previous_suffix}. Somebody else's file under one of this step's working names is a thing to
move aside and name, not a reason to stop an install:
${output}"
    return
  fi
  if ! grep -qF "${foreign_suffix}" <<<"$output"; then
    rm -f "$good" "$backup_copy"
    printf '%s\n' "step 80 met a file at ${dest}${previous_suffix} that it did not write and never told the
operator where it went:
${output}"
    return
  fi
  local survivor found_backup=0
  while IFS= read -r survivor; do
    if cmp -s "$backup_copy" "$survivor"; then
      found_backup=1
    fi
  done < <(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)
  if [ "$found_backup" -ne 1 ]; then
    rm -f "$good" "$backup_copy"
    printf '%s\n' "step 80 finished an install and destroyed an operator's own backup at
${dest}${previous_suffix}. Nothing on this host still holds those bytes:
$(ls -1 "$(dirname "$dest")" | grep -F "$(basename "$dest")" || true)
The step decides what is its own working state by PROVENANCE — the marker its render stamps — and a
file it cannot account for is moved aside, never consumed and never removed. Treating whatever sits
under that name as scratch is how a re-install eats the backup an operator took before running it.
Its output:
${output}"
    return
  fi
  rm -f "$backup_copy"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the operator-backup case changed what step 80 serves; the cases below can no longer start
from the vhost case 1 installed."
    return
  fi

  # 1b1. A DIRECTORY UNDER ONE OF THE STEP'S OWN WORKING NAMES, AND AN INSTALL THAT SUCCEEDS.
  #
  #      Measured on alma9 against round 3: the rollback copy's `mv` moved the staged file INTO the
  #      directory and succeeded, so the step recorded a rollback copy that is not a file; the swap
  #      and the validation then passed; and the `rm -f` at the end failed on a directory and killed
  #      the shell under `set -e` — AFTER the guard was disarmed, so nothing printed the step's name.
  #      The operator got exit 1 from an install that had worked, a vhost on disk the running server
  #      was never told about (neither `systemctl enable` nor `reload` runs after that point), and a
  #      stray copy of the panel vhost left inside the directory.
  mkdir -p "${dest}${previous_suffix}"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -ne 0 ]; then
    rm -rf "${dest}${previous_suffix}"
    rm -f "$good"
    printf '%s\n' "step 80 exited ${status} because a DIRECTORY was sitting at ${dest}${previous_suffix}. Its output:
${output}
Everything the step exists to do had already worked. A working name occupied by something that is
not a regular file is a thing to move aside and name, not a way to end an install non-zero after it
has succeeded — and an install that dies there never reaches 'systemctl enable' or 'reload', so the
vhost is on disk and the running server has not been told."
    return
  fi
  records="$(nginx_service_records)"
  case "$records" in
    *reload*) ;;
    *)
      rm -rf "${dest}${previous_suffix}"
      rm -f "$good"
      printf '%s\n' "step 80 reported success with a directory at ${dest}${previous_suffix} but never reloaded
nginx — the polygon's systemctl recorded: ${records:-nothing}. The new vhost is on disk and the
running server was never told about it."
      return
      ;;
  esac
  rm -rf "${dest}${previous_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -exec rm -rf {} +
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the directory case changed what step 80 serves; the cases below can no longer start from the
vhost case 1 installed."
    return
  fi

  # 1b. AN INTERRUPT WITH THE SHIPPED TEMPLATE, and the ONLY case that can see the second half of
  #     the interrupt guarantee: that the step DIES of the signal rather than resuming.
  #
  #     Every other interrupt case here uses a broken template, so the step fails for that reason
  #     too and a swallowed signal is invisible. Measured with `kill -s "$signal" "$BASHPID"`
  #     replaced by `return "$status"`: the guard restored, the step carried on past its own
  #     handler, enabled and reloaded nginx and printed "panel reachable on port 8443" — with no
  #     panel vhost anywhere on the machine, because the guard had just removed it. Exit 0, a
  #     success line, and nothing served.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" TERM
  status=0
  output="$(run_nginx_step 'step_nginx' "$shim" 2>&1)" || status=$?
  rm -rf "$shim"
  # The premise, asserted rather than assumed, and the reason is measured: the shim fires ONCE, and
  # step 80 runs an `nginx -t` before the swap whenever a leftover rollback copy is on the host. In
  # that state the single fire lands there, the step dies before it has written anything, and every
  # check below is satisfied by a run that never armed the guard — the status is still 143 and the
  # served file is still byte-for-byte the one that was there, because nothing touched it. The
  # sentence the step prints on that path names it exactly.
  case "$output" in
    *"was not written to at all"*)
      rm -f "$good"
      printf '%s\n' "this case cannot test what it names: step 80 stopped BEFORE it wrote anything to ${dest}, so
the interrupt landed outside the swap window and the guard was never armed. The status and byte
checks below would pass on a step with no guard at all. Its output:
${output}"
      return
      ;;
  esac
  if [ "$status" -ne 143 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was sent SIGTERM in the middle of its own validation and exited ${status}, not 143.
An installer that answers a signal with anything but dying of it has decided to carry on: at 0 it
went on to enable and reload nginx and to report the panel reachable, with whatever the guard had
just taken off the served path. 128 + SIGTERM is the only status that says the step stopped because
it was told to. Its output:
${output}"
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted with the SHIPPED template and ${dest} is no longer the vhost that was
there before it ran. The guard restored the wrong thing, or nothing."
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted during its validation and touched the nginx service anyway — the
polygon's systemctl recorded: ${records}. Reached at all, that line means the step ran past its own
interrupt handler."
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "an interrupted install with the shipped template left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)"
    return
  fi

  # 1c. A FAILURE BEFORE THE SWAP, WITH A ROLLBACK COPY ALREADY ON THE HOST — the guard must not
  #     restore a served path nothing has written to.
  #
  #     The guard is armed before the candidate is written, deliberately: the window between the
  #     rename and the arm is the hole it exists to close. So there is a stretch in which it is
  #     armed and the served file is still untouched, and a restoration that fired there would
  #     REPLACE a vhost this step never wrote. Measured before the fix, with a leftover rollback
  #     copy present and an `install` that fails the way a full /etc fails: the served path took
  #     the leftover copy and the step printed "is as it was before this step ran".
  #
  #     The leftover has to be one the step will hold aside as its first choice on a rollback
  #     rather than overwrite — which means the tree must not load AND the file must be one the
  #     step can account for — so a third party's broken file goes into conf.d for the length of
  #     this case and comes out again at the end of it, and the leftover is re-stamped after it is
  #     altered so that it stays a vhost this installer wrote. That is also the honest shape of the
  #     bug: an operator re-running the installer on a host something else has already broken.
  local intruder="/etc/nginx/conf.d/zz-not-maran.conf"
  printf 'a_directive_belonging_to_nobody on;\n' > "$intruder"
  cp -p "$good" "${dest}${previous_suffix}"
  printf '\n# leftover-rollback-copy-from-an-earlier-run\n' >> "${dest}${previous_suffix}"
  stamp_as_maran_vhost "${dest}${previous_suffix}" "$marker_prefix"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step "install_validated_vhost '/nonexistent/render' '${dest}'" 2>&1)" || status=$?
  rm -f "$intruder"
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "${dest}${previous_suffix}"
    printf '%s\n' "step 80's install_validated_vhost reported success for a render that does not exist:
${output}"
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good" "${dest}${previous_suffix}"
    printf '%s\n' "step 80 failed BEFORE it wrote anything to ${dest} — the render it was given does not exist —
and the served vhost changed anyway. Its output:
${output}
The interrupt guard restored a path the step had never written to, so a leftover rollback copy from
an earlier run replaced a vhost that was serving the panel, under the words 'as it was'."
    return
  fi
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after a step-80 failure before the swap, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi

  printf '\n%s on;\n' "$broken" >> "$PANEL_VHOST"

  # 2. A BROKEN VHOST OVER A GOOD ONE. Refused, rolled back byte-for-byte, service untouched.
  forget_nginx_service_records
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "step 80 INSTALLED a panel vhost that nginx cannot load, over a working one. Its own output:
${output}
This is the staging-name defect: the validation parsed a tree the candidate was not in."
    return
  fi
  case "$output" in
    *"$broken"*) ;;
    *)
      rm -f "$good"
      printf '%s\n' "step 80 refused the broken vhost, but never named the directive nginx choked on
(${broken}), so it refused for some other reason and this case proves nothing:
${output}"
      return
      ;;
  esac
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost but did not put ${dest} back byte-for-byte. An operator is
left with a served vhost that no run of this installer ever validated."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost and left an nginx that no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost and touched the nginx service anyway — the polygon's systemctl
recorded: ${records}. A refusal that still reaches the service is the failure this gate exists to
prevent, and a RELOAD is the sharp end of it: it is how a vhost 'nginx -t' has not seen reaches a
running server, and the restart the step falls back to when a reload fails stops an nginx that
cannot come back up."
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "the refused vhost is still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)
A rendered candidate or a rollback copy left in the tree is a file the next include pattern
change turns into a live configuration nobody validated."
    return
  fi

  # 2b/2c. A REFUSED RE-INSTALL ON A HOST SOMETHING ELSE HAS ALREADY BROKEN — the state in which
  #        the round-3 step DELETED the working panel vhost it found and told the operator the
  #        fault lay elsewhere.
  #
  #        This is one of the commonest reasons to re-run an installer: a third party's file under
  #        conf.d does not parse, so `nginx -t` fails for a reason that has nothing to do with the
  #        panel vhost. Round 3 chose which rollback copy to take by asking that question — "does
  #        the tree load" — instead of "is this file one I wrote". With any `<dest>.previous` on
  #        the host it therefore took NO copy of the served vhost at all, overwrote it, had its own
  #        render refused, declined the leftover because the tree still did not load, and left the
  #        served path ABSENT. Measured on alma9: the working panel vhost that was serving before
  #        the step ran existed nowhere on the machine afterwards, under the words "the
  #        configuration at fault is not one this step wrote".
  #
  #        Case 1c builds this host and stops one character short of the defect: it fails the run
  #        before the swap, so the guard's `swapped` is 0 and the restoration returns without ever
  #        reaching the arm that breaks. These two reach it, with a render that IS swapped in and
  #        IS refused, once for each kind of leftover — one the step cannot account for (2b) and
  #        one it can (2c) — because round 3 loses the served vhost on both.
  #
  #        What is asserted is the entry state: whatever else the step does, ${dest} afterwards is
  #        byte-for-byte the vhost that was serving when it started. That is the whole contract of
  #        a rollback, and it is the one thing a step may not trade away for a tidier message.
  local backup
  for backup in stranger ours; do
    printf 'a_directive_belonging_to_nobody on;\n' > "$intruder"
    cp -p "$good" "${dest}${previous_suffix}"
    printf '\n# an operator backup, or what a killed run left\n' >> "${dest}${previous_suffix}"
    # `stranger` leaves the appended line unaccounted for, so vhost_is_ours is FALSE — an
    # operator's own `maran.conf.previous`. `ours` re-stamps it, so the predicate is TRUE and the
    # step holds it aside as its first choice on a rollback. Round 3 asked neither question and
    # destroyed the served vhost in both.
    if [ "$backup" = ours ]; then
      stamp_as_maran_vhost "${dest}${previous_suffix}" "$marker_prefix"
    fi
    forget_nginx_service_records
    status=0
    output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
    rm -f "$intruder"
    if [ "$status" -eq 0 ]; then
      rm -f "$good"
      printf '%s\n' "step 80 INSTALLED a panel vhost nginx cannot load, on a host whose configuration was
already broken by a file that is not the panel's (leftover: ${backup}):
${output}"
      return
    fi
    # The premise: a run that failed before the swap never reaches the arm this case is about,
    # which is exactly how case 1c passes today while the defect is live.
    case "$output" in
      *"was not written to at all"*)
        rm -f "$good"
        printf '%s\n' "this case cannot test what it names (leftover: ${backup}): step 80 stopped BEFORE it wrote
anything to ${dest}, so the restoration returned without reaching the arm that chooses a rollback
copy. Its output:
${output}"
        return
        ;;
    esac
    if [ ! -e "$dest" ]; then
      rm -f "$good"
      printf '%s\n' "step 80 re-ran on a host that some OTHER file under conf.d had already broken (leftover:
${backup}), refused its own render — and left ${dest} with no file at all. Its output:
${output}
The panel vhost that was serving before the step ran is now nowhere on this machine. The step
decided which rollback copy to take by asking whether 'nginx -t' passes on the host, which is a
question about the whole tree and not about ${dest}, so it never copied the served file before
overwriting it. 'Is this file mine' and 'does the tree load' are two different questions."
      return
    fi
    if ! cmp -s "$good" "$dest"; then
      rm -f "$good"
      printf '%s\n' "step 80 re-ran on a host that some OTHER file under conf.d had already broken (leftover:
${backup}), refused its own render, and left ${dest} holding something that is not the vhost it
found there. Its output:
${output}"
      return
    fi
    if ! nginx -t >/dev/null 2>&1; then
      rm -f "$good"
      printf '%s\n' "with the intruding file removed again (leftover: ${backup}), nginx still does not load after
step 80's refusal:
$(nginx -t 2>&1)"
      return
    fi
    records="$(nginx_service_records)"
    if [ -n "$records" ]; then
      rm -f "$good"
      printf '%s\n' "step 80 refused on an already-broken host (leftover: ${backup}) and touched the nginx service
anyway — the polygon's systemctl recorded: ${records}."
      return
    fi
    if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
      rm -f "$good"
      printf '%s\n' "after a refusal on an already-broken host (leftover: ${backup}), the vhost nginx rejected is
still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
      return
    fi
    rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
    find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  done

  # 3. AN INTERRUPT INSIDE THE SWAP WINDOW — the candidate renamed onto the served path, the
  #    validation running, and the step's shell killed. Nothing RETURNS, so a rollback written
  #    only on the paths that return does not happen at all.
  #
  #    This is the price of validating after the swap, and it has to be paid rather than argued
  #    away. Measured on both families before the step grew its trap: exit 143, the
  #    never-validated vhost left at the served path permanently, `nginx -t` then [emerg], and
  #    nginx refusing to start — which, because this same nginx serves every customer site
  #    through maran-sites.conf, is every site on the box at the next restart or reboot, not
  #    just the panel. The old staging-name version left `maran.conf.staging`, a file nginx
  #    never opens, so this was a failure the fix INTRODUCED and only a guard removes.
  #
  #    The case is here rather than at the end because it must leave the host exactly as it
  #    found it — that is what it asserts — so the two cases below still start from the served
  #    path holding the vhost case 1 installed.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" TERM
  status=0
  output="$(run_nginx_step 'step_nginx' "$shim" 2>&1)" || status=$?
  rm -rf "$shim"
  if [ "$status" -eq 0 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was killed in the middle of its own validation and still reported SUCCESS:
${output}
Whatever it left on the served path, no run of this installer ever validated it."
    return
  fi
  # The same premise case 1b carries, and for the same measured reason: the shim fires once, and a
  # step that stops before the swap satisfies every check below without ever arming the guard.
  case "$output" in
    *"was not written to at all"*)
      rm -f "$good"
      printf '%s\n' "this case cannot test what it names: step 80 stopped BEFORE it wrote anything to ${dest}, so
the interrupt landed outside the swap window and the guard was never armed. Every check below would
pass on a step with no guard at all. Its output:
${output}"
      return
      ;;
  esac
  if [ "$status" -ne 143 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was sent SIGTERM in the middle of its validation and exited ${status} rather than 143.
The guard restored the served path but did not die of the signal it was sent, so the status the
installer hands its parent no longer says what happened — and the step's own handler is one line
away from resuming an install that was interrupted. Its output:
${output}"
    return
  fi
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted (SIGTERM) between the swap and the end of its validation, and ${dest}
is no longer the vhost that was there before it ran — it holds ${broken} $(grep -cF "$broken" "$dest" || true)
time(s). Nothing on this host will put it back: the panel and every customer site this nginx serves
go down at the next restart or reboot. A rollback that runs only on the paths that RETURN cannot
see this one, which is why ops::safe_write guards its swap with a Drop rather than an \`if\` at each
error path (agent/crates/ops/src/safe_write/rollback_guard.rs)."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 was interrupted during its validation, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after an interrupted install, the vhost nothing validated is still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "an interrupted install left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)
The restoration takes them with it precisely so that the next run does not have to guess which of
two files on the served path a human should trust."
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 was interrupted during its validation and touched the nginx service anyway — the
polygon's systemctl recorded: ${records}. An interrupted install must leave the running server
exactly where it found it."
    return
  fi

  # 4. AN INSTALL KILLED BY THE ONE SIGNAL NO TRAP CAN CATCH, AND THE RUN THAT COMES AFTER IT.
  #
  #    SIGKILL — `kill -9`, an OOM kill, a container stop that runs out of grace — leaves the
  #    never-validated candidate at the served path and the last vhost this installer validated
  #    at <dest>.previous. Nothing in step 80 can prevent that half. What it can do, and what
  #    this case is about, is the NEXT run: it must treat that leftover copy as the good one.
  #
  #    The step used to delete both working files at the top of every run and then back up
  #    whatever was on the served path. After a killed install that is exactly backwards: the
  #    only validated copy on the machine was deleted, the file that replaced it was the one
  #    nothing had ever validated, and a refusal then "rolled back" to it. The host was left
  #    serving a vhost nginx cannot load, by a step whose whole promise is the opposite.
  #    One artefact of this case outlives it and is left deliberately: the killed run cannot
  #    remove the vhost it had rendered into /tmp, so a 0600 root-owned copy of it stays in the
  #    image. That is what a SIGKILLed install leaves on a real host too, and removing it here
  #    would mean this script tidying up after a process it deliberately killed.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" KILL
  if output="$(run_nginx_step 'step_nginx' "$shim" 2>&1)"; then
    rm -rf "$shim"
    rm -f "$good"
    printf '%s\n' "step 80 was killed with SIGKILL in the middle of its validation and still reported SUCCESS:
${output}"
    return
  fi
  rm -rf "$shim"
  # The premise, asserted rather than assumed: if SIGKILL did not leave the host in the state
  # this case is about, everything below it would pass while testing nothing.
  if cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "this case cannot test what it names: after SIGKILL inside the validation, ${dest} still holds
the previously validated vhost, so no run of step 80 ever meets the leftover state below. Either the
kill missed the window or the step no longer swaps before it validates."
    return
  fi
  if ! cmp -s "$good" "${dest}${previous_suffix}"; then
    rm -f "$good"
    printf '%s\n' "this case cannot test what it names: after SIGKILL, ${dest}${previous_suffix} is not the vhost
that was validated before it. The next run has no good copy to prefer, so the check below would pass
for the wrong reason."
    return
  fi
  # And now the run that comes after it, with the template still unloadable, so that the run has
  # to roll back and the file it rolls back TO is the whole question.
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  case "$output" in
    *"$broken"*) ;;
    *)
      rm -f "$good"
      printf '%s\n' "the run after a killed install refused, but never named the directive nginx choked on
(${broken}), so it refused for some other reason and this case proves nothing:
${output}"
      return
      ;;
  esac
  if ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install rolled back to the WRONG file: ${dest} now holds ${broken}
$(grep -cF "$broken" "$dest" || true) time(s) instead of the last vhost this installer validated. The
run deleted ${dest}${previous_suffix} — the only validated copy on the machine — took its backup from
the never-validated file the killed run had left on the served path, and restored that. An operator
who interrupts an install and re-runs it is left serving a configuration nginx refuses."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after the run that followed a killed install, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "the run after a killed install refused and touched the nginx service anyway — the polygon's
systemctl recorded: ${records}."
    return
  fi

  # 4b. A ROLLBACK COPY THAT DOES NOT PARSE — the file the restoration is about to serve, checked.
  #
  #     Case 4 establishes that the run after a killed install prefers `<dest>.previous` to the
  #     never-validated file on the served path. This is the other half of that sentence: the step
  #     did not write that copy, did not watch it being written, and cannot assume it is whole. A
  #     `cp` interrupted by a hard reset leaves a truncated file — the ordinary ext4 outcome — and
  #     the previous version of this step adopted it, moved it onto the served path, and printed
  #     "is as it was before this step ran". Measured on both families: `nginx -t` afterwards was
  #     `[emerg] unexpected end of file`, and nginx would not start. That is the whole box at the
  #     next reboot, by way of the code written to prevent exactly that.
  #
  #     The state is produced rather than described: SIGKILL inside the validation, which leaves
  #     the candidate served and the validated vhost at `.previous`, and then that copy truncated
  #     to the first 3000 of its bytes.
  forget_nginx_service_records
  shim="$(mktemp -d)"
  nginx_shim_that_interrupts_the_validation "$shim" KILL
  run_nginx_step 'step_nginx' "$shim" >/dev/null 2>&1 || true
  rm -rf "$shim"
  if [ ! -e "${dest}${previous_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "this case cannot test what it names: after SIGKILL inside the validation there is no
${dest}${previous_suffix} for it to truncate, so the step never meets a rollback copy it did not write."
    return
  fi
  local fragment
  fragment="$(mktemp)"
  head -c 3000 "${dest}${previous_suffix}" > "$fragment"
  cp -p "$fragment" "${dest}${previous_suffix}"
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "$fragment"
    printf '%s\n' "the run after a killed install INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  if [ -e "$dest" ] && cmp -s "$fragment" "$dest"; then
    rm -f "$good" "$fragment"
    printf '%s\n' "step 80 served a rollback copy it never read. ${dest} now holds the truncated
${dest}${previous_suffix} — 3000 bytes of a vhost that does not parse — and the step said so in these
words:
${output}
nginx will not START with that file, and this nginx serves every customer site on the host through
maran-sites.conf, so the whole box goes at the next reboot. A restoration that installs an
unvalidated file is worse than none: the operator has been told the machine is as they left it."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good" "$fragment"
    printf '%s\n' "after a refusal whose rollback copy does not parse, nginx no longer loads:
$(nginx -t 2>&1)
The step's job there is to leave a host that BOOTS: the panel unreachable is a loss, an nginx that
will not start is an outage of every site on the machine."
    return
  fi
  # The step must SAY which file it declined and why. It refuses the fragment twice over now: the
  # marker its own render_vhost stamps does not match a truncated file, so it is parked without ever
  # reaching the served path at all, and the never-validated candidate it finds on the served path
  # is then tried and refused by `nginx -t`. Either sentence is a naming of the file at fault; a
  # silent decline is not.
  case "$output" in
    *"does not parse"* | *"not a panel vhost this installer wrote"*) ;;
    *)
      rm -f "$good" "$fragment"
      printf '%s\n' "step 80 declined to serve a rollback copy it could not account for — correctly — but never
said so. Its output was:
${output}
The operator is left with no panel vhost and no sentence naming the file that caused it."
      return
      ;;
  esac
  # The declined copy is still ON THE MACHINE. A step that made the host boot by DELETING the only
  # other copy of a panel vhost is the round-3 defect this suite's newest case is about, and a
  # truncated file is still evidence an operator may want.
  if [ -z "$(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)" ]; then
    rm -f "$good" "$fragment"
    printf '%s\n' "step 80 declined the rollback copy it could not account for and then made it disappear: there
is no ${dest}${foreign_suffix}* on the host. A file the step did not write is not a file the step may
delete, and the operator has been left with neither a panel vhost nor the copy that caused it."
    return
  fi
  case "$output" in
    *"already broken before this step ran"* | *"was already failing before"*)
      rm -f "$good" "$fragment"
      printf '%s\n' "step 80 told the operator their host's nginx was already broken, about a file the step
itself installed:
${output}"
      return
      ;;
  esac
  rm -f "$fragment"
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4c. A LEFTOVER HELD ASIDE AND NEVER HANDED BACK — the state this round's own fix creates.
  #
  #     Preferring a validated leftover to the entry bytes means the two cannot share one name, so
  #     the step moves the leftover to `<dest>.adopted` and writes its own copy of the served file
  #     to `<dest>.previous`. That introduces a window of its own: a run killed between those two
  #     renames leaves the last validated panel vhost under a name the NEXT run has never had to
  #     think about. Treated as this step's own scratch it would be deleted on sight — which is
  #     round 2's defect (the only validated copy on the machine, removed by the run that came to
  #     help) reappearing under a new name. The next run promotes it back instead.
  #
  #     This case exists because the fix for the finding above is the kind of change that leaves a
  #     smaller version of the same bug behind, which is what the three rounds before this one did.
  cp -p "$good" "${dest}${adopted_suffix}"
  printf '\n%s on;\n' "$broken" >> "$dest"
  forget_nginx_service_records
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "the run after an install killed while it held a leftover aside INSTALLED a vhost nginx cannot
load:
${output}"
    return
  fi
  if [ ! -e "$dest" ] || ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "an install was killed between holding the last validated panel vhost aside at
${dest}${adopted_suffix} and handing it back, and the run that followed did not pick it up: ${dest}
$([ -e "$dest" ] && echo 'holds something else' || echo 'does not exist'). Its output:
${output}
A file the step moved aside is not the step's scratch to delete on the next run — that is round 2's
defect, which removed the only validated copy on the machine, wearing the name this round's own fix
introduced."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after the run that followed an install killed mid-hand-aside, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  if [ -e "${dest}${candidate_suffix}" ] || [ -e "${dest}${previous_suffix}" ] \
     || [ -e "${dest}${adopted_suffix}" ]; then
    rm -f "$good"
    printf '%s\n' "that run left step 80's working files behind:
$(ls -1 "${dest}${candidate_suffix}" "${dest}${previous_suffix}" "${dest}${adopted_suffix}" 2>/dev/null)"
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after that run, the vhost nginx rejected is still somewhere under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
    return
  fi

  # 4d. A SYMLINK UNDER ONE OF THIS STEP'S WORKING NAMES, pointing at a stamped panel vhost
  #     OUTSIDE /etc/nginx. The step must not follow it, must not serve it, and must not delete
  #     the entry copy on the strength of having accounted for it.
  #
  #     `[ -f ]` is not "is a regular file": it FOLLOWS the link and is true of any symlink
  #     resolving to one. vhost_is_ours tested exactly that for four rounds while its doc comment
  #     said regular file, so a link at `<dest>.previous` was accounted for, adopted, renamed onto
  #     the served path, and the entry copy of the vhost that was actually serving was deleted
  #     afterwards. Measured on both families before the fix, with the served path ending as
  #     `maran.conf -> /root/hidden.conf` and the step announcing that it held "the last panel
  #     vhost an install of this product validated".
  #
  #     Three things are asserted, because the harm is three-fold. The served path is never a
  #     symlink — delete the target, or boot with its filesystem unmounted, and nginx does not
  #     START, which is every customer site on this host through maran-sites.conf. The `nginx -t`
  #     proof stays a statement about bytes rather than about a name: with a link on the served
  #     path, what nginx loaded was the target at one instant and anything that can write the
  #     target afterwards changes what is served, which is the "the file validated and the file
  #     served are two different files" state case 1 exists to forbid. And the entry bytes survive.
  #
  #     The link's target is checked too: this step is not entitled to consume, rewrite or remove
  #     a file an operator put outside the directory the installer owns.
  local outside="/root/a-panel-vhost-outside-etc-nginx.conf"
  cp -p "$good" "$outside"
  # The premise, asserted rather than assumed: the target IS a file the step accounts for. If it
  # were not, the case below would pass because the marker did not match and would say nothing at
  # all about symlinks.
  if ! run_nginx_step "vhost_is_ours '${outside}'"; then
    rm -f "$good" "$outside"
    printf '%s\n' "this case cannot test what it names: ${outside} is a copy of the vhost step 80 itself
rendered and vhost_is_ours does not account for it, so a symlink pointing at it would be declined
for the wrong reason."
    return
  fi
  printf '\n%s on;\n' "$broken" >> "$dest"
  ln -s "$outside" "${dest}${previous_suffix}"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "$outside"
    printf '%s\n' "with a symlink at ${dest}${previous_suffix}, step 80 INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  if [ -L "$dest" ]; then
    local target
    target="$(readlink -f "$dest" 2>/dev/null || true)"
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 followed a symlink it found under one of its own working names and made the SERVED
path a link out of the directory it owns: ${dest} -> ${target}. Its output:
${output}
Delete that target, or boot with its filesystem unmounted, and nginx does not START — this nginx
serves every customer site on the host through maran-sites.conf. And the 'nginx -t' the step ran is
no longer a statement about the bytes it serves: it is a statement about whatever the link resolved
to at one instant, which anything that can write that path may change afterwards. That is the
'the file validated and the file served are two different files' state this suite's first case
exists to forbid, reached through a predicate that says 'regular file' and tests '[ -f ]'."
    return
  fi
  if [ ! -e "$outside" ] || ! cmp -s "$good" "$outside"; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 met a symlink under one of its working names and then consumed, rewrote or removed
what it pointed AT: ${outside} $([ -e "$outside" ] && echo 'has changed' || echo 'is gone'). A file
outside the directory this step owns is not this step's to touch, whatever name inside the directory
happens to point at it."
    return
  fi
  if [ ! -e "${dest}${previous_suffix}" ] || ! grep -qF "$broken" "${dest}${previous_suffix}"; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 met a symlink at ${dest}${previous_suffix}, declined to serve it — correctly — and
still destroyed the bytes that were on the served path when it started: they are not at
${dest}${previous_suffix}. Its output:
${output}
The entry copy is taken before anything is touched precisely so that no predicate about a LEFTOVER
can cost the operator the vhost that was serving."
    return
  fi
  if [ -z "$(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)" ]; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 declined the symlink at ${dest}${previous_suffix} but did not park it: there is no
${dest}${foreign_suffix}* on the host. A name this step only borrows, occupied by something it
cannot account for, is moved aside and named to the operator — never deleted, never followed."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good" "$outside"
    printf '%s\n' "after step 80 met a symlink under one of its working names, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good" "$outside"
    printf '%s\n' "step 80 refused with a symlink under one of its working names and touched the nginx service
anyway — the polygon's systemctl recorded: ${records}."
    return
  fi
  rm -f "$outside"
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4f. A DANGLING SYMLINK UNDER ONE OF THIS STEP'S WORKING NAMES — the intersection of case 1b
  #     and case 4d, which is exactly where five rounds of this suite had no coverage.
  #
  #     Case 1b names the harm — an install that finished and destroyed an operator's own backup at
  #     `<dest>.previous` — and asserts it for a REGULAR FILE. Case 4d asserts the symlink handling,
  #     and its link has a target that EXISTS. Each reads like coverage of this name; between them
  #     they left the one state where the file is a symlink AND its target is gone, and the code
  #     failed there for five rounds while both cases passed. Two green cases whose union looks
  #     total are worth less than either of them looks: the predicate that decides which of them a
  #     run takes — `[ -e ]` — is precisely the one that answers differently for a dangling link.
  #
  #     `[ -e ]` RESOLVES the link, so it is false for a dangling one and the whole leftover branch
  #     is skipped: vhost_is_ours is never asked, park_foreign_file is never reached, nothing is
  #     printed, and the unconditional entry copy then renames onto that path — `rename(2)` replaces
  #     the SYMLINK, not its absent target. Measured before the fix on both families: no `.foreign`
  #     anywhere, the link gone, and the step exiting 0 with the ordinary success line.
  #
  #     The state is the ordinary one, not a contrivance: `maran.conf.previous` is the name a human
  #     picks for a backup, and a backup pointer into a volume that is later unmounted, or at a file
  #     later deleted, is a dangling link. Nothing about it is visible to the operator afterwards,
  #     which is why the install must say the name it moved it to.
  local nowhere="/mnt/a-volume-that-is-not-mounted/panel-backup.conf"
  [ ! -e "$nowhere" ] || rm -f "$nowhere"
  ln -s "$nowhere" "${dest}${previous_suffix}"
  # The premise, asserted rather than assumed: a link that RESOLVES is case 4d's state and would
  # test nothing new here.
  if [ -e "${dest}${previous_suffix}" ] || [ ! -L "${dest}${previous_suffix}" ]; then
    rm -f "$good" "${dest}${previous_suffix}"
    printf '%s\n' "this case cannot test what it names: ${dest}${previous_suffix} is not a DANGLING symlink, so it
is case 4d's state over again and says nothing about the gate that decides whether case 4d's
handling is reached at all."
    return
  fi
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  local parked_link
  parked_link="$(find "$(dirname "$dest")" -maxdepth 1 \
    -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null | head -n 1)"
  if [ -z "$parked_link" ] || [ ! -L "$parked_link" ]; then
    rm -f "$good"
    rm -f "${dest}${previous_suffix}"
    printf '%s\n' "step 80 met a DANGLING symlink at ${dest}${previous_suffix} and did not park it: there is no
symlink under ${dest}${foreign_suffix}* on this host. It exited ${status} saying:
${output}
The link is gone — the entry copy was renamed straight onto it, and rename(2) replaces the link
itself. That is case 1b's harm — an install that finished and destroyed an operator's own backup at
<dest>.previous — reached through the one input case 1b and case 4d do not have between them: 1b
uses a regular file, 4d uses a link whose target exists, and the test [ -e ] resolves the link, so a
dangling one skips the branch that both of those cases exercise."
    return
  fi
  if [ "$(readlink "$parked_link")" != "$nowhere" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 parked the dangling symlink at ${dest}${previous_suffix} but not as it found it: it now
points at $(readlink "$parked_link") rather than at ${nowhere}. A link an operator made is moved,
whole, or it is not preserved at all."
    return
  fi
  case "$output" in
    *"$parked_link"*) : ;;
    *)
      rm -f "$good"
      printf '%s\n' "step 80 moved an operator's dangling symlink to ${parked_link} and never said so:
${output}
A file moved aside under a name the operator chose is only 'left alone' if they are told where it
went; the pointer is the whole of what a dangling link is worth."
      return
      ;;
  esac
  # The render is refused here, because the template has carried `$broken` since case 2 and this
  # case runs among the others that need it. That is not a weaker test of the same thing: the
  # destroying rename is write_rollback_copy's, which happens BEFORE the swap and therefore on the
  # refusing path too. What the refusal adds is the second half — the entry bytes come back with an
  # operator's link parked beside them rather than consumed on the way.
  if [ "$status" -eq 0 ]; then
    rm -f "$good"
    printf '%s\n' "step 80 INSTALLED a vhost nginx cannot load while a dangling symlink sat at
${dest}${previous_suffix}:
${output}"
    return
  fi
  if [ -L "$dest" ] || ! cmp -s "$good" "$dest"; then
    rm -f "$good"
    printf '%s\n' "step 80 met a dangling symlink at ${dest}${previous_suffix}, refused its own render — correctly
— and did not put the served path back: ${dest} is $([ -L "$dest" ] && echo 'a symlink' || echo 'not
the vhost that was there'). The entry copy is taken before anything is touched precisely so that no
predicate about a LEFTOVER can cost the operator the vhost that was serving, and a leftover that is
a dangling link is still a leftover."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 met a dangling symlink under one of its working names, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4g. A DANGLING SYMLINK ON THE SERVED PATH ITSELF, which is the same defect one name over.
  #
  #     `<dest>` is not one of the three working names — it is the name this step exists to write —
  #     and the gate that decides whether the entry copy is taken asked `[ -e ]` about it too. So a
  #     link there was consumed exactly as a link at `<dest>.previous` was: `install` follows it and
  #     copies the TARGET's bytes into the rollback copy, the swap's `mv -f` then replaces the LINK,
  #     and a DANGLING one is not even looked at, because `[ -e ]` resolved it to nothing. Measured
  #     before the fix on both families: no `.foreign` anywhere, `maran.conf` a regular file, and
  #     the operator's pointer gone with no line naming it.
  #
  #     What the step does now is what claim_working_name does for `<dest>.candidate` and
  #     vhost_is_ours does for `<dest>.previous`: park the link, name it, and write a regular file.
  #     No entry copy is taken — there is nothing of this step's to copy — so what is asserted here
  #     is the link's survival and the operator being told, not a restoration.
  rm -f "$dest"
  ln -s "$nowhere" "$dest"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  parked_link="$(find "$(dirname "$dest")" -maxdepth 1 \
    -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null | head -n 1)"
  if [ -z "$parked_link" ] || [ ! -L "$parked_link" ] || [ "$(readlink "$parked_link")" != "$nowhere" ]; then
    rm -f "$good"
    rm -f "$dest"
    printf '%s\n' "step 80 found a dangling symlink on the SERVED path and destroyed it: there is no symlink to
${nowhere} under ${dest}${foreign_suffix}* on this host. It exited ${status} saying:
${output}
The served path is this step's to write, not this step's to consume: an operator's link there is
moved aside and named, like anything else under a name this step needs and did not put there."
    return
  fi
  if [ -L "$dest" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 left a symlink on the served path at ${dest}. Nothing this step writes is ever a link
out of the directory it owns — case 4d's whole argument."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 met a dangling symlink on the served path, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  rm -f "$dest"
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 4e. A MARKER-ONLY FILE — a stamp that agrees with itself and attests nothing.
  #
  #     `tail -n +2` on a file consisting of the marker line ALONE yields nothing, and the SHA-256
  #     of nothing is a perfectly well-formed digest. So a 72-byte residue carrying it satisfied
  #     every question vhost_is_ours asked: the predicate agreed with itself and was still wrong.
  #     Measured on both families before the fix — the file was adopted, `nginx -t` passed with it
  #     (one comment line is valid nginx), it was left SERVED, the entry copy was deleted, and the
  #     operator was told ${dest} held "the last panel vhost an install of this product validated"
  #     while it held no `server` block and no `listen` at all: the panel unreachable, under a
  #     sentence asserting the opposite.
  #
  #     The state is not exotic in origin — a crash truncates on block boundaries — but the defect
  #     is in the predicate, so it is produced directly. The lower bound the step now applies is
  #     the vhost's own shape, the `listen` line, which no marker-only residue can have.
  printf '\n%s on;\n' "$broken" >> "$dest"
  printf '%s%s\n' "$marker_prefix" "$(printf '' | sha256sum | cut -d' ' -f1)" \
    > "${dest}${previous_suffix}"
  local hollow
  hollow="$(mktemp)"
  cp -p "${dest}${previous_suffix}" "$hollow"
  forget_nginx_service_records
  status=0
  output="$(run_nginx_step 'step_nginx' 2>&1)" || status=$?
  if [ "$status" -eq 0 ]; then
    rm -f "$good" "$hollow"
    printf '%s\n' "with a marker-only rollback copy present, step 80 INSTALLED a vhost nginx cannot load:
${output}"
    return
  fi
  if [ -e "$dest" ] && cmp -s "$hollow" "$dest"; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 served a file that is nothing but this step's own provenance marker. ${dest} now
holds $(wc -c < "$dest") bytes with no 'server' block and no 'listen' line, and the step said:
${output}
'nginx -t' passes with it, because one comment under conf.d is valid nginx — which is exactly why
the digest cannot be the whole question. The SHA-256 of an empty body is a real digest, so the stamp
agreed with itself; the panel is unreachable and the vhost that was serving has been deleted on the
strength of it."
    return
  fi
  if [ -e "$dest" ] && ! grep -q -e '^[[:space:]]*listen[[:space:]]' "$dest"; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 left ${dest} holding a file with no 'listen' line at all:
$(cat "$dest")
Whatever else is true of a panel vhost, it listens on the panel's port; a served file that does not
is a panel nobody can reach, reported as an install that rolled back cleanly."
    return
  fi
  if [ ! -e "${dest}${previous_suffix}" ] || ! grep -qF "$broken" "${dest}${previous_suffix}"; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 declined the marker-only rollback copy — correctly — and still destroyed the bytes
that were on the served path when it started: they are not at ${dest}${previous_suffix}. Its output:
${output}"
    return
  fi
  if [ -z "$(find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" 2>/dev/null)" ]; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 declined the marker-only rollback copy and then made it disappear: there is no
${dest}${foreign_suffix}* on the host. A file the step cannot account for is moved aside, not
deleted — it is evidence, and it is not this step's to remove."
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good" "$hollow"
    printf '%s\n' "after step 80 met a marker-only rollback copy, nginx no longer loads:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good" "$hollow"
    printf '%s\n' "step 80 refused with a marker-only rollback copy present and touched the nginx service anyway
— the polygon's systemctl recorded: ${records}."
    return
  fi
  rm -f "$hollow"
  rm -f "${dest}${previous_suffix}" "${dest}${candidate_suffix}" "${dest}${adopted_suffix}"
  find "$(dirname "$dest")" -maxdepth 1 -name "$(basename "$dest")*${foreign_suffix}*" -delete
  cp -p "$good" "$dest"
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "this case did not put ${dest} back the way the cases after it need it:
$(nginx -t 2>&1)"
    return
  fi

  # 5. A BROKEN VHOST ON A HOST THAT HAS NONE — the FIRST install, where there is nothing to roll
  #    back to and the step must leave the served path empty rather than occupied.
  rm -f "$dest"
  forget_nginx_service_records
  if output="$(run_nginx_step 'step_nginx' 2>&1)"; then
    rm -f "$good"
    printf '%s\n' "on a host with no panel vhost at all, step 80 INSTALLED one that nginx cannot load:
${output}"
    return
  fi
  if [ -e "$dest" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 refused the broken vhost on a first install and left it at ${dest} anyway:
$(cat "$dest")"
    return
  fi
  if ! nginx -t >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after step 80 refused a first install, nginx no longer loads at all:
$(nginx -t 2>&1)"
    return
  fi
  records="$(nginx_service_records)"
  if [ -n "$records" ]; then
    rm -f "$good"
    printf '%s\n' "step 80 refused a first install and touched the nginx service anyway — the polygon's systemctl
recorded: ${records}."
    return
  fi
  if grep -rlF "$broken" /etc/nginx >/dev/null 2>&1; then
    rm -f "$good"
    printf '%s\n' "after a refused first install, the broken vhost is still under /etc/nginx:
$(grep -rlF "$broken" /etc/nginx)"
    return
  fi
  rm -f "$good"

}
