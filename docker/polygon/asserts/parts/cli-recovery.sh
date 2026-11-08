#!/usr/bin/env bash
# step 66: the break-glass `maran` command, installed root-only and refusing what it should
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The command is a bash script that runs as root and receives text an operator typed, so the
# properties worth a gate are the ones that keep that text from being interpreted: who may run it,
# what an unknown verb does, what shape of command is ever built, and what PATH it trusts. The
# assertions here RUN the installed wrapper and look at its answers; the one that reads text (the
# fixed-argv check) says so in its own output.

# The paths step 66 installs to. Literals on purpose, the same arrangement the directory modes
# follow: an assertion that read them out of 66-cli.sh would agree with that file by construction.
readonly CLI_WRAPPER_INSTALLED="/usr/local/sbin/maran"
readonly CLI_LIB_INSTALLED_DIR="/usr/local/maran/cli/lib"
# Twenty libraries: the seven that existed before the scheduled panel backup, its four (the
# configuration reader, retention, the upload and the schedule verb), and the nine of the self-update
# (issue #128: release-version, release-verify, release-unpack, release-layout, panel-dump,
# update-dumps, update-health, update, rollback). The number is CLI_LIB_COUNT and every message and
# census below uses it, so a library added to the list without raising it fails the directory census
# instead of passing a stale count.
readonly CLI_LIB_NAMES="common.sh status.sh reset.sh diagnostics.sh panel-archive.sh panel-backup-config.sh panel-backup-retention.sh panel-backup-upload.sh panel-backup.sh panel-backup-schedule.sh panel-restore.sh release-version.sh release-verify.sh release-unpack.sh release-layout.sh panel-dump.sh update-dumps.sh update-health.sh update.sh rollback.sh"
readonly CLI_LIB_COUNT=20
# The file a hostile argument tries to create. Its absence means something only because the same
# directory is shown to accept a file (see assert_the_break_glass_command_never_interprets_its_input).
readonly CLI_INJECTION_WITNESS="/tmp/maran-polygon-cli-injected"

# run_cli_step: step 66's own step_cli, run the way install.sh runs a step (a child with
# errexit set, the shared helpers sourced, SCRIPT_DIR naming the installer package).
run_cli_step() {
  SCRIPT_DIR="$INSTALLER_ROOT" bash -c 'set -euo pipefail
. "$1"
. "$2"
step_cli' _ "$COMMON_LIB" "$CLI_STEP"
}

# remove_the_installed_break_glass_command: takes the wrapper and its libraries back out, by the
# paths this part installed them to, so an assertion leaves the image as it found it.
remove_the_installed_break_glass_command() {
  rm -f -- "$CLI_WRAPPER_INSTALLED"
  rm -rf -- /usr/local/maran/cli
}

# cli_code_hits: the lines of the given files that would make this script interpret text, one
# `file:line:text` per hit, comments excluded. `eval`, and a shell started with `-c` (alone or
# among other single-letter options), are the two ways a root bash script turns data into code.
#
# Comments are stripped first because the files' own headers say "no eval, no sh -c"; a search
# over the raw text finds the prose and calls it a defect. `-E`, not a basic-regex alternation:
# `\|` in a BRE is a GNU extension and `|` is a literal there (rules/testing.md).
cli_code_hits() {
  local file
  for file in "$@"; do
    sed 's/^[[:space:]]*#.*$//' "$file" \
      | { grep -nE '(^|[^[:alnum:]_])eval([^[:alnum:]_]|$)|(^|[^[:alnum:]_/.-])(sh|bash|dash|zsh)[[:space:]]+(-[A-Za-z]+[[:space:]]+)*-[A-Za-z]*c([[:space:]]|$)' || true; } \
      | sed "s|^|${file}:|"
  done
}

# run_as_nobody: a command as the unprivileged `nobody`, status passed through. runuser is
# util-linux's, present on both families; its absence is a named failure, not a skip.
run_as_nobody() {
  command -v runuser >/dev/null 2>&1 \
    || fail "runuser is not in this image, so the break-glass command cannot be run as a non-root caller. util-linux provides it on both families; without it the refusal below would be asserted about nobody"
  runuser -u nobody -- "$@"
}

# assert_the_break_glass_command_installs_root_only_and_refuses_what_it_should: step 66 runs, and
# the file it leaves is root's alone, answers a non-root caller with 77, answers a verb it does not
# know with a refusal, interprets nothing it is given, and trusts no PATH but its own.
#
# Every refusal is paired with an INVERSE CONTROL that the same invocation is ACCEPTED when it
# should be: a wrapper mutated to refuse everything passes every test that only hands it bad input.
#
# UNOBSERVED HERE: this image boots no systemd and holds no panel, so the verbs are only run as far
# as their argument handling; what `status`, `admin-reset-password` and `collect-diagnostics` do
# against a live panel is the install-polygon lockout-recovery and diagnostics stages. SELinux
# contexts on the wrapper and on /usr/local/maran/cli (AlmaLinux enforces) are not exercised by a
# container build either.
assert_the_break_glass_command_installs_root_only_and_refuses_what_it_should() {
  local output status lib checked=0 libs_found hits planted_dir fakebin copy hostile
  local code_files="$CLI_WRAPPER_INSTALLED"

  # 1. The step itself, and what it left.
  output="$(run_cli_step 2>&1)" \
    || fail "step 66's step_cli failed in this image: ${output}"
  case "$output" in
    *"Installed ${CLI_WRAPPER_INSTALLED}"*) ;;
    *) fail "step 66 ran but did not report installing ${CLI_WRAPPER_INSTALLED}: ${output}" ;;
  esac
  checked=$((checked + 1))

  assert_file_is "$CLI_WRAPPER_INSTALLED" "0:0:700"
  cmp -s "$CLI_WRAPPER_SOURCE" "$CLI_WRAPPER_INSTALLED" \
    || fail "${CLI_WRAPPER_INSTALLED} differs from installer/cli/maran: the step installed something other than what the package carries"
  checked=$((checked + 1))

  for lib in $CLI_LIB_NAMES; do
    assert_file_is "${CLI_LIB_INSTALLED_DIR}/${lib}" "0:0:644"
    cmp -s "${CLI_LIB_SOURCE_DIR}/${lib}" "${CLI_LIB_INSTALLED_DIR}/${lib}" \
      || fail "${CLI_LIB_INSTALLED_DIR}/${lib} differs from installer/cli/lib/${lib}"
    code_files="${code_files} ${CLI_LIB_INSTALLED_DIR}/${lib}"
    checked=$((checked + 1))
  done
  # The directory the libraries live in, and the one above it: a library root can replace is a
  # library anyone who can write the directory can replace.
  for lib in /usr/local/maran/cli "$CLI_LIB_INSTALLED_DIR"; do
    [ -d "$lib" ] && [ ! -L "$lib" ] \
      || fail "${lib} is not a real directory"
    [ "$(stat -c '%u %a' -- "$lib")" = "0 755" ] \
      || fail "${lib} is '$(stat -c '%u %a' -- "$lib")', must be '0 755' (root-owned, writable by root alone)"
    checked=$((checked + 1))
  done
  # Exactly the listed libraries: an extra file in a directory root sources from is a file root runs.
  # The vacuity guard is on the list itself: the count the directory is held to is the count of names
  # actually in CLI_LIB_NAMES, so a stale constant cannot agree with a stale list.
  [ "$(printf '%s\n' $CLI_LIB_NAMES | wc -l)" -eq "$CLI_LIB_COUNT" ] \
    || fail "CLI_LIB_NAMES lists $(printf '%s\n' $CLI_LIB_NAMES | wc -l) libraries but CLI_LIB_COUNT is ${CLI_LIB_COUNT}"
  libs_found="$(find "$CLI_LIB_INSTALLED_DIR" -mindepth 1 | wc -l)"
  [ "$libs_found" -eq "$CLI_LIB_COUNT" ] \
    || fail "${CLI_LIB_INSTALLED_DIR} holds ${libs_found} entries, expected exactly ${CLI_LIB_COUNT} ($CLI_LIB_NAMES)"
  checked=$((checked + 1))

  # 2. The step creates nothing else: no setuid or setgid file, no sudoers entry. The check's own
  # control is the same command over a directory with a setuid file planted in it.
  planted_dir="$(mktemp -d)"
  : > "${planted_dir}/setuid-control"
  chmod 4755 "${planted_dir}/setuid-control"
  [ -n "$(find "$planted_dir" -type f -perm /6000)" ] \
    || fail "the setuid search (find -perm /6000) does not find a file with mode 4755 planted in ${planted_dir}, so its empty answer over the installed tree means nothing"
  rm -rf -- "$planted_dir"
  hits="$(find "$CLI_WRAPPER_INSTALLED" /usr/local/maran/cli -perm /6000 2>/dev/null || true)"
  [ -z "$hits" ] || fail "step 66 left a setuid or setgid file: ${hits}"
  checked=$((checked + 1))

  planted_dir="$(mktemp -d)"
  printf 'ALL ALL=(root) NOPASSWD: /usr/local/sbin/maran\n' > "${planted_dir}/zz-control"
  /usr/bin/grep -rlE '/usr/local/sbin/maran|maran-cli' -- "$planted_dir" >/dev/null \
    || fail "the sudoers search (/usr/bin/grep -rlE '/usr/local/sbin/maran|maran-cli') does not find a planted entry, so its empty answer over /etc/sudoers.d means nothing"
  rm -rf -- "$planted_dir"
  if [ -d /etc/sudoers.d ] \
     && /usr/bin/grep -rlE '/usr/local/sbin/maran|maran-cli' -- /etc/sudoers.d >/dev/null 2>&1; then
    fail "a sudoers entry names the break-glass command; step 66 must create none (it is root-only by file mode, not by a delegation)"
  fi
  checked=$((checked + 1))

  # 3. A non-root caller. The installed wrapper is 0700 root, so nobody cannot even start it: that
  # is the first line of defence and is asserted as such. The euid check inside it is the second,
  # reachable only through a readable COPY, which is how it is observed.
  status=0
  output="$(run_as_nobody "$CLI_WRAPPER_INSTALLED" help 2>&1)" || status=$?
  [ "$status" -ne 0 ] \
    || fail "nobody ran ${CLI_WRAPPER_INSTALLED} successfully: ${output}"
  case "$output" in
    *"Usage: maran"*) fail "nobody got the break-glass command's help text out of a 0700 root file: ${output}" ;;
  esac
  checked=$((checked + 1))

  copy="$(mktemp -d)"
  chmod 755 "$copy"
  cp -- "$CLI_WRAPPER_INSTALLED" "${copy}/maran-copy"
  chmod 755 "${copy}/maran-copy"
  status=0
  output="$(run_as_nobody "${copy}/maran-copy" status 2>&1)" || status=$?
  [ "$status" -eq 77 ] \
    || fail "a non-root caller of the wrapper got exit ${status}, not 77 (EX_NOPERM). Output: ${output}"
  case "$output" in
    *"must be run as root"*) ;;
    *) fail "a non-root caller got exit 77 without the explanation: ${output}" ;;
  esac
  # Nothing but that one line: no service state, no panel report reaches a caller that is not root.
  [ "$(printf '%s\n' "$output" | wc -l)" -eq 1 ] \
    || fail "a non-root caller received more than the one refusal line: ${output}"
  checked=$((checked + 1))

  # Inverse control: the SAME copy, as root, is accepted. A wrapper that refused everyone would
  # have passed the block above.
  status=0
  output="$("${copy}/maran-copy" help 2>&1)" || status=$?
  [ "$status" -eq 0 ] \
    || fail "root running the same copy of the wrapper got exit ${status}, so the 77 above may be the wrapper refusing everyone: ${output}"
  case "$output" in
    *"Usage: maran"*) ;;
    *) fail "root running the wrapper's help got no usage text: ${output}" ;;
  esac
  checked=$((checked + 1))

  # 4. The caller's PATH is not trusted. A `id` on a hostile PATH that says "uid 0" would pass the
  # euid check if the wrapper looked `id` up on the PATH it was handed. Control first: the fake
  # really does win when nothing resets the PATH.
  fakebin="${copy}/fakebin"
  mkdir -p "$fakebin"
  printf '#!/bin/sh\necho 0\n' > "${fakebin}/id"
  chmod 755 "${fakebin}/id"
  [ "$(run_as_nobody env "PATH=${fakebin}:/usr/bin:/bin" id -u)" = "0" ] \
    || fail "the fake id on ${fakebin} does not shadow the real one even without the wrapper, so the PATH check below would pass whatever the wrapper does"
  status=0
  output="$(run_as_nobody env "PATH=${fakebin}:/usr/bin:/bin" "${copy}/maran-copy" status 2>&1)" || status=$?
  [ "$status" -eq 77 ] \
    || fail "with a fake id first on PATH a non-root caller got exit ${status}, not 77: the wrapper trusts the caller's PATH. Output: ${output}"
  checked=$((checked + 1))

  # 5. A verb it does not know, and arguments built to be interpreted. Each must be refused with a
  # non-zero status and must not run anything; the witness file is created by the payload.
  : > "$CLI_INJECTION_WITNESS"
  [ -e "$CLI_INJECTION_WITNESS" ] || fail "${CLI_INJECTION_WITNESS} cannot be created, so its absence below would prove nothing"
  rm -f -- "$CLI_INJECTION_WITNESS"

  status=0
  output="$("$CLI_WRAPPER_INSTALLED" frobnicate 2>&1)" || status=$?
  [ "$status" -eq 64 ] \
    || fail "an unknown verb got exit ${status}, not 64 (EX_USAGE): ${output}"
  case "$output" in
    *"unknown command"*) ;;
    *) fail "an unknown verb was refused without saying so: ${output}" ;;
  esac
  checked=$((checked + 1))

  for hostile in \
    "status;touch ${CLI_INJECTION_WITNESS}" \
    "\$(touch ${CLI_INJECTION_WITNESS})" \
    "-x" \
    "help;touch ${CLI_INJECTION_WITNESS}"; do
    status=0
    output="$("$CLI_WRAPPER_INSTALLED" "$hostile" 2>&1)" || status=$?
    [ "$status" -eq 64 ] \
      || fail "the verb '${hostile}' got exit ${status}, not 64: ${output}"
    [ ! -e "$CLI_INJECTION_WITNESS" ] \
      || { rm -f -- "$CLI_INJECTION_WITNESS"; fail "the verb '${hostile}' CREATED ${CLI_INJECTION_WITNESS}: the wrapper interpreted its verb"; }
    checked=$((checked + 1))
  done

  # Typed values: refused by the validation regex before the panel is ever reached. Each of these
  # reaches the account-name check, which runs before the terminal check and before any call.
  for hostile in \
    "a b; touch ${CLI_INJECTION_WITNESS}" \
    "\$(touch ${CLI_INJECTION_WITNESS})" \
    "x\`touch ${CLI_INJECTION_WITNESS}\`" \
    "$(printf 'a\nb')"; do
    status=0
    output="$("$CLI_WRAPPER_INSTALLED" admin-reset-password "$hostile" --allow-non-terminal 2>&1)" || status=$?
    [ "$status" -eq 1 ] \
      || fail "admin-reset-password with a hostile identifier got exit ${status}, not 1: ${output}"
    case "$output" in
      *"characters this command does not accept"*) ;;
      *) fail "admin-reset-password refused a hostile identifier for the wrong reason: ${output}" ;;
    esac
    [ ! -e "$CLI_INJECTION_WITNESS" ] \
      || { rm -f -- "$CLI_INJECTION_WITNESS"; fail "a hostile identifier CREATED ${CLI_INJECTION_WITNESS}: the wrapper interpreted a typed value"; }
    checked=$((checked + 1))
  done
  # A leading dash is an option, refused as one; it never reaches the panel as a value.
  status=0
  output="$("$CLI_WRAPPER_INSTALLED" admin-reset-password -rf 2>&1)" || status=$?
  [ "$status" -eq 1 ] && case "$output" in *"unknown option"*) true ;; *) false ;; esac \
    || fail "admin-reset-password -rf got exit ${status} and '${output}', expected a refusal of the option"
  checked=$((checked + 1))
  # Inverse control for the regex: a plain account name is NOT refused by it (it fails later, for
  # the panel not being installed in this image, which is a different message).
  status=0
  output="$("$CLI_WRAPPER_INSTALLED" admin-reset-password plain.admin@example.test --allow-non-terminal 2>&1)" || status=$?
  case "$output" in
    *"characters this command does not accept"*) fail "the identifier regex refused an ordinary address, so the refusals above may be it refusing everything: ${output}" ;;
  esac
  [ "$status" -ne 0 ] \
    || fail "admin-reset-password succeeded in an image with no panel installed: ${output}"
  checked=$((checked + 1))

  # 5b. The panel archive verbs refuse before anything is read or written: a restore without the
  # explicit confirmation, and a passphrase file anybody else could read. The inverse control is a
  # passphrase file that passes every rule and is then refused for a DIFFERENT reason (no panel is
  # installed in this image), so the mode refusal is not the verb refusing every file.
  status=0
  output="$("$CLI_WRAPPER_INSTALLED" panel-restore --passphrase-file /root/none /root/none.tar.gpg 2>&1)" || status=$?
  [ "$status" -eq 1 ] && case "$output" in *"--replace-this-panel"*) true ;; *) false ;; esac \
    || fail "panel-restore without --replace-this-panel got exit ${status} and '${output}', expected the confirmation refusal"
  checked=$((checked + 1))
  copy="$(mktemp -d)"
  printf '%s\n' 'a-passphrase-long-enough-for-the-floor' > "${copy}/passphrase"
  chmod 0644 "${copy}/passphrase"
  status=0
  output="$("$CLI_WRAPPER_INSTALLED" panel-backup --passphrase-file "${copy}/passphrase" --out /root 2>&1)" || status=$?
  [ "$status" -eq 1 ] && case "$output" in *"mode 0600 or 0400"*) true ;; *) false ;; esac \
    || fail "panel-backup with a world-readable passphrase file got exit ${status} and '${output}', expected the mode refusal"
  chmod 0600 "${copy}/passphrase"
  status=0
  output="$("$CLI_WRAPPER_INSTALLED" panel-backup --passphrase-file "${copy}/passphrase" --out /root 2>&1)" || status=$?
  case "$output" in
    *"mode 0600 or 0400"*|*"at least"*) fail "panel-backup refused a 0600 passphrase file of the right length, so the refusal above may be it refusing every file: ${output}" ;;
  esac
  [ "$status" -ne 0 ] || fail "panel-backup succeeded in an image with no panel installed: ${output}"
  ls /root/maran-panel-backup-* >/dev/null 2>&1 \
    && fail "panel-backup left an archive behind in an image with no panel installed"
  rm -rf -- "$copy"
  checked=$((checked + 1))

  # 6. Fixed argv: nothing in the wrapper or its libraries turns data into code. TEXT, not
  # behaviour. The control first: the same search finds each of the four shapes in a planted file
  # and none of three look-alikes (a variable that merely contains the letters, a comment, a word).
  copy="$(mktemp -d)"
  printf '%s\n' 'eval "$x"' 'sh -c "$y"' '  bash -c "$z"' 'bash -ec "$q"' > "${copy}/planted.sh"
  printf '%s\n' 'evaluate=1' '# eval "$x"  and  sh -c' 'echo "bash is a shell"' 'sh_command=1' > "${copy}/clean.sh"
  hits="$(cli_code_hits "${copy}/planted.sh" | wc -l)"
  [ "$hits" -eq 4 ] \
    || fail "the fixed-argv search found ${hits} of the 4 interpreting shapes planted in a control file (eval, sh -c, bash -c, bash -ec); its empty answer over the real wrapper would mean nothing"
  hits="$(cli_code_hits "${copy}/clean.sh" | wc -l)"
  [ "$hits" -eq 0 ] \
    || fail "the fixed-argv search flagged ${hits} line(s) of a control file containing only look-alikes (a variable named evaluate, a comment, prose); it refuses what it should accept"
  rm -rf -- "$copy"
  checked=$((checked + 2))

  hits="$(cli_code_hits $code_files)"
  [ -z "$hits" ] \
    || fail "the break-glass command builds code from data in ${hits} (rules/security.md item 3: no shell strings, argv arrays only)"
  [ "$(printf '%s\n' $code_files | wc -l)" -eq $((CLI_LIB_COUNT + 1)) ] \
    || fail "the fixed-argv search covered $(printf '%s\n' $code_files | wc -l) files, expected the wrapper and its ${CLI_LIB_COUNT} libraries"
  checked=$((checked + 1))

  # The wrapper pins its own PATH, and every start of a binary as the service account is a fixed
  # argv. Counted with patterns that also match the real lines, so a rename of either cannot leave
  # this passing over nothing.
  # runuser appears on exactly four lines, each a fixed argv as the service account: the ONE
  # helper that runs a panel binary as that account (cli_run_as_service in common.sh, used for the
  # --cli mode, the restore's --migrate and the update's --migrate alike), and pg_dump and
  # pg_restore and psql (panel-dump.sh, the one place they are spelled). A fifth is something new that runs
  # as somebody, and is read before it passes. Counted per file so that one file's lines cannot
  # stand in for another's.
  [ "$(cat $code_files | sed 's/^[[:space:]]*#.*$//' | grep -c 'runuser')" -eq 4 ] \
    || fail "the break-glass command mentions runuser on a line other than the four known ones (cli_run_as_service in common.sh, pg_dump, pg_restore and psql in panel-dump.sh)"
  [ "$(sed 's/^[[:space:]]*#.*$//' "${CLI_LIB_INSTALLED_DIR}/common.sh" | grep -cE 'runuser -u "\$MARAN_CLI_SERVICE_USER" --')" -eq 1 ] \
    || fail "common.sh no longer runs a panel binary as the service account on exactly one 'runuser -u \"\$MARAN_CLI_SERVICE_USER\" --' line (cli_run_as_service)"
  [ "$(sed 's/^[[:space:]]*#.*$//' "${CLI_LIB_INSTALLED_DIR}/panel-dump.sh" | grep -cE 'runuser -u "\$MARAN_CLI_SERVICE_USER" -- pg_dump ')" -eq 1 ] \
    || fail "panel-dump.sh no longer runs pg_dump as the service account on exactly one 'runuser -u \"\$MARAN_CLI_SERVICE_USER\" -- pg_dump' line"
  [ "$(sed 's/^[[:space:]]*#.*$//' "${CLI_LIB_INSTALLED_DIR}/panel-dump.sh" | grep -cE 'runuser -u "\$MARAN_CLI_SERVICE_USER" -- pg_restore ')" -eq 1 ] \
    || fail "panel-dump.sh no longer runs pg_restore as the service account on exactly one 'runuser -u \"\$MARAN_CLI_SERVICE_USER\" -- pg_restore' line"
  grep -qxF 'PATH=/usr/sbin:/usr/bin:/sbin:/bin' "$CLI_WRAPPER_INSTALLED" \
    || fail "the wrapper no longer pins PATH=/usr/sbin:/usr/bin:/sbin:/bin on a line of its own"
  checked=$((checked + 1))

  remove_the_installed_break_glass_command
  [ ! -e "$CLI_WRAPPER_INSTALLED" ] || fail "${CLI_WRAPPER_INSTALLED} is still present after this assertion removed it"

  echo "The break-glass command: ${CLI_WRAPPER_INSTALLED} is 0700 root:root and its ${CLI_LIB_COUNT} libraries 0644 root:root under root-only 0755 directories, step 66 created no setuid file and no sudoers entry, a non-root caller gets exit 77 (and the same copy run as root is accepted), an unknown verb gets 64, hostile verbs and identifiers create nothing (witness file shown creatable), panel-restore refuses without its confirmation and panel-backup refuses a passphrase file others can read (and accepts a 0600 one as far as the absent panel), a hostile PATH cannot supply the euid check, and no line of the wrapper or its libraries evals or starts 'sh -c' (the search shown to find all four planted shapes and none of the look-alikes). ${checked} checks made."
  echo "UNOBSERVED HERE: the fixed-argv check reads TEXT. It sees 'eval' and a shell started with -c; it does not see an unquoted expansion of operator input, a here-document that is later sourced, or a command built across lines. The shell quoting itself was reviewed by reading and by the hostile inputs above, not by a formal analysis."
  echo "UNOBSERVED HERE: this image boots no systemd and holds no panel, so status, admin-reset-password, collect-diagnostics, panel-backup, panel-restore, update and rollback are run only as far as their argument handling (the last two in parts/cli-update.sh). Their behaviour against a live panel is scored by scripts/lib/install-polygon.sh (lockout-recovery and diagnostics stages). SELinux contexts on the wrapper and /usr/local/maran/cli are not exercised by a container build."
}

# ---------------------------------------------------------------------------------------------
# The scheduled panel backup's verbs (issue #129): `maran panel-backup --scheduled` and
# `maran panel-backup-schedule enable|disable|status`.
#
# The configuration file they read, /etc/maran/panel-backup.conf, steers a root process, so what is
# worth a gate is how it is READ: as data, never as code, and only when root alone could have written
# it. Everything below runs the installed wrapper and looks at its answers. A refusal is paired with
# an inverse control (a valid file that gets past the reader), because a reader mutated to refuse
# everything passes every test that only hands it hostile input.
# ---------------------------------------------------------------------------------------------

# cli_pbtest_run: the installed wrapper with the given arguments; status in CLI_PBTEST_STATUS and
# stdout+stderr in CLI_PBTEST_OUTPUT. Globals, because a `$(...)` would hide the status of the
# command it captures from `set -e` and from the caller.
cli_pbtest_run() {
  CLI_PBTEST_STATUS=0
  [ "$(sed 's/^[[:space:]]*#.*$//' "${CLI_LIB_INSTALLED_DIR}/panel-dump.sh" | grep -cE 'runuser -u "\$MARAN_CLI_SERVICE_USER" -- psql ')" -eq 1 ] \
    || fail "panel-dump.sh no longer runs psql as the service account on exactly one 'runuser -u \"\$MARAN_CLI_SERVICE_USER\" -- psql' line"
  CLI_PBTEST_OUTPUT="$("$CLI_WRAPPER_INSTALLED" "$@" 2>&1)" || CLI_PBTEST_STATUS=$?
}

# cli_pbtest_plant_config: writes the installed configuration path with the given mode and content.
# The content goes through `printf %b`, so `\n`, `\r`, `\t` and `\0` in a case below are the control
# characters themselves. The file is root:root; /etc/maran is created only when absent (the
# restoring function removes it again), never re-moded.
#   $1 = mode, $2 = content (printf %b).
cli_pbtest_plant_config() {
  local mode="$1" content="$2"
  [ -d /etc/maran ] || install -d -m 0750 -o root -g root /etc/maran
  rm -f -- "$PANEL_BACKUP_CONFIG_FILE"
  printf '%b' "$content" > "$PANEL_BACKUP_CONFIG_FILE"
  chown root:root -- "$PANEL_BACKUP_CONFIG_FILE"
  chmod "$mode" -- "$PANEL_BACKUP_CONFIG_FILE"
}

# cli_pbtest_expect_refusal: the last cli_pbtest_run was a refusal (exit 1) that names the expected
# problem, created nothing for the injection witness to find, never echoed the hostile payload back,
# and wrote neither a timer drop-in nor an enablement.
#   $1 = case name, $2 = substring the message must carry.
cli_pbtest_expect_refusal() {
  local name="$1" expected="$2"
  [ "$CLI_PBTEST_STATUS" -eq 1 ] \
    || fail "the configuration case '${name}' got exit ${CLI_PBTEST_STATUS}, not the 1 of a refusal: ${CLI_PBTEST_OUTPUT}"
  case "$CLI_PBTEST_OUTPUT" in
    *"$expected"*) ;;
    *) fail "the configuration case '${name}' was refused for the wrong reason (expected '${expected}'): ${CLI_PBTEST_OUTPUT}" ;;
  esac
  [ ! -e "$CLI_INJECTION_WITNESS" ] \
    || { rm -f -- "$CLI_INJECTION_WITNESS"; fail "the configuration case '${name}' CREATED ${CLI_INJECTION_WITNESS}: the reader interpreted a value in the file"; }
  case "$CLI_PBTEST_OUTPUT" in
    *"touch"*|*"$CLI_INJECTION_WITNESS"*) fail "the refusal of the case '${name}' echoed the hostile value back to the operator and the journal: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  [ ! -e "$PANEL_BACKUP_DROPIN_DIR" ] && [ ! -e "${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_TIMER_FILE}.enabled" ] \
    || fail "the refused configuration case '${name}' still wrote the timer drop-in or enabled the timer; nothing may be written before every check has passed"
}

# cli_pbtest_expect_past_the_reader: the last cli_pbtest_run was NOT refused by the configuration
# reader: none of its refusal messages is in the output, and the run got as far as the panel stage,
# which this image does not have (so it ends there, for a reason that is not the file's).
cli_pbtest_expect_past_the_reader() {
  local name="$1" refusal
  for refusal in "does not exist; write it first" "is not a regular file" "must be owned by root:root" \
    "contains a control character" "NUL byte" "is not KEY=VALUE" "sets an unknown key" "more than once" \
    "contains a quote" "is not acceptable" "needs a value" "is not set in" "S3 keys are set" \
    "S3_ENDPOINT is set but" "must have mode" "is larger than"; do
    case "$CLI_PBTEST_OUTPUT" in
      *"$refusal"*) fail "the valid configuration case '${name}' was refused by the reader ('${refusal}'), so every refusal in this assertion may be the reader refusing everything: ${CLI_PBTEST_OUTPUT}" ;;
    esac
  done
  case "$CLI_PBTEST_OUTPUT" in
    *"Maran installed"*) ;;
    *) fail "the valid configuration case '${name}' did not reach the panel stage that this image lacks ('is Maran installed?'), so passing the reader is not shown: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  [ "$CLI_PBTEST_STATUS" -ne 0 ] \
    || fail "a scheduled panel backup succeeded in an image with no panel installed (case '${name}'): ${CLI_PBTEST_OUTPUT}"
}

# pbupload_curl_defect: prints the first reason the upload library at $1 would weaken the one
# outbound call, and prints nothing when it would not. TEXT, comments stripped: the library's header
# names `-k` and `-L` to say they are never used, and a search over raw text finds the sentence.
pbupload_curl_defect() {
  local effective statement flag
  effective="$(sed 's/^[[:space:]]*#.*$//' "$1")"
  # Only the curl statement itself: from the line that starts curl to the first line that does not
  # end in a continuation backslash. The rest of the library is full of `[ ! -L "$x" ]`, which is a
  # test for a symbolic link and not curl's redirect option.
  [ "$(grep -cE '(^|[^[:alnum:]_])curl[[:space:]]' <<<"$effective")" -eq 1 ] \
    || { echo "starts curl on other than exactly one line"; return 0; }
  statement="$(awk '/(^|[^[:alnum:]_])curl[[:space:]]/ { f = 1 } f { print } f && !/\\$/ { exit }' <<<"$effective")"
  for flag in "--proto '=https'" "--tlsv1.2" "--max-redirs 0" "curl -q" "--fail" "--output /dev/null" '--config "$config"'; do
    grep -qF -- "$flag" <<<"$statement" || { echo "no longer passes ${flag} to curl"; return 0; }
  done
  if grep -Eq -- '(^|[[:space:]])(-k|-L|-u|--insecure|--location|--user|--ssl-no-revoke|--proxy-insecure|--cacert|--capath)([[:space:]=]|$)' <<<"$statement"; then
    echo "gives curl an option that skips verification, follows a redirect, changes the trust store or puts a credential on its argument list"; return 0
  fi
  return 0
}

# assert_the_scheduled_panel_backup_verbs_refuse_what_they_should: the arguments, the refusals and
# the root-only boundary of the four new commands, with the installed wrapper and step 67's units.
#
# The wrapper is installed by step 66 and the units by step 67 (both of the installer's own steps) and
# both are taken out again, with the configuration and everything the verbs wrote, before this returns.
#
# UNOBSERVED HERE: this image boots no systemd and holds no panel, so `--scheduled` is run only as far
# as the configuration reader and the first panel check, `enable` writes its drop-in and asks the
# polygon's systemctl stand-in (which records, and enforces nothing), and no archive is taken, pruned
# or uploaded here. Retention's deletions and the S3 request are the install polygon's recovery stage;
# the upload library's flags are checked as TEXT below, and the journal line a failure writes is not
# read. SELinux contexts on the drop-in (AlmaLinux enforces) are not exercised by a container build.
assert_the_scheduled_panel_backup_verbs_refuse_what_they_should() {
  local checked=0 pass_dir pass_file name content expected mode work copy hostile planted_lib
  local status output hostile_count=0 upload_count=0
  local state="${UNIT_STATE_DIRECTORY}/${PANEL_BACKUP_TIMER_FILE}.enabled"
  local dropin="${PANEL_BACKUP_DROPIN_DIR}/schedule.conf"
  local archive="${PANEL_BACKUP_OUT_DIR}/maran-panel-backup-20260101T000000Z-1.tar.gpg"

  panel_backup_note_prior_state
  run_cli_step >/dev/null || fail "step 66's step_cli could not install the command this assertion runs"

  : > "$CLI_INJECTION_WITNESS"
  [ -e "$CLI_INJECTION_WITNESS" ] || fail "${CLI_INJECTION_WITNESS} cannot be created, so its absence below would prove nothing"
  rm -f -- "$CLI_INJECTION_WITNESS"

  # 1. The schedule verb's own arguments, before any unit or file exists. Exactly one word, and one of
  # three; the hostile ones create nothing.
  cli_pbtest_run panel-backup-schedule
  [ "$CLI_PBTEST_STATUS" -eq 1 ] && case "$CLI_PBTEST_OUTPUT" in *"Usage: maran panel-backup-schedule enable|disable|status"*) true ;; *) false ;; esac \
    || fail "panel-backup-schedule with no action got exit ${CLI_PBTEST_STATUS} and '${CLI_PBTEST_OUTPUT}', expected the usage refusal"
  checked=$((checked + 1))
  cli_pbtest_run panel-backup-schedule enable extra
  [ "$CLI_PBTEST_STATUS" -eq 1 ] && case "$CLI_PBTEST_OUTPUT" in *"Usage: maran panel-backup-schedule"*) true ;; *) false ;; esac \
    || fail "panel-backup-schedule enable with an extra word got exit ${CLI_PBTEST_STATUS} and '${CLI_PBTEST_OUTPUT}', expected the usage refusal"
  checked=$((checked + 1))
  for hostile in "frobnicate" "--help" "-x" "status;touch ${CLI_INJECTION_WITNESS}" "\$(touch ${CLI_INJECTION_WITNESS})" "ENABLE"; do
    cli_pbtest_run panel-backup-schedule "$hostile"
    [ "$CLI_PBTEST_STATUS" -eq 1 ] && case "$CLI_PBTEST_OUTPUT" in *"unknown action"*) true ;; *) false ;; esac \
      || fail "panel-backup-schedule '${hostile}' got exit ${CLI_PBTEST_STATUS} and '${CLI_PBTEST_OUTPUT}', expected a refusal of the action"
    [ ! -e "$CLI_INJECTION_WITNESS" ] \
      || { rm -f -- "$CLI_INJECTION_WITNESS"; fail "panel-backup-schedule '${hostile}' CREATED ${CLI_INJECTION_WITNESS}: the verb interpreted its action"; }
    checked=$((checked + 1))
  done
  # Inverse control: a real action is NOT refused as unknown, whatever else it answers.
  cli_pbtest_run panel-backup-schedule status
  case "$CLI_PBTEST_OUTPUT" in
    *"unknown action"*|*"Usage: maran panel-backup-schedule"*) fail "panel-backup-schedule status was refused as a bad action, so the refusals above may be the verb refusing everything: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  checked=$((checked + 1))

  # 2. `enable` before the units exist: refused, naming the missing unit, and nothing written.
  cli_pbtest_run panel-backup-schedule enable
  [ "$CLI_PBTEST_STATUS" -eq 1 ] && case "$CLI_PBTEST_OUTPUT" in *"is not installed"*) true ;; *) false ;; esac \
    || fail "panel-backup-schedule enable without the units installed got exit ${CLI_PBTEST_STATUS} and '${CLI_PBTEST_OUTPUT}', expected the refusal that names the missing unit"
  [ ! -e "$PANEL_BACKUP_DROPIN_DIR" ] && [ ! -e "$state" ] \
    || fail "enable refused for the missing units and still wrote a drop-in or enabled the timer"
  checked=$((checked + 1))

  # From here the units exist, installed by step 67 itself.
  run_panel_backup_step >/dev/null || fail "step 67's step_panel_backup could not install the units this assertion needs"
  pass_dir="$(mktemp -d)"
  pass_file="${pass_dir}/passphrase"
  printf '%s\n' 'a-passphrase-long-enough-for-the-floor' > "$pass_file"
  chmod 0600 "$pass_file"
  printf '%s\n' 'AKIAEXAMPLEKEYID0000' > "${pass_dir}/key-id"
  printf '%s\n' 'example/secret+key=0000000000000000000000' > "${pass_dir}/secret"
  chmod 0600 "${pass_dir}/key-id" "${pass_dir}/secret"

  # 3. No configuration at all, for both the job and `enable`: refused, naming the missing file.
  rm -f -- "$PANEL_BACKUP_CONFIG_FILE"
  cli_pbtest_run panel-backup --scheduled
  cli_pbtest_expect_refusal "no configuration file (the job)" "does not exist"
  checked=$((checked + 1))
  cli_pbtest_run panel-backup-schedule enable
  cli_pbtest_expect_refusal "no configuration file (enable)" "does not exist"
  checked=$((checked + 1))
  cli_pbtest_run panel-backup --scheduled --out /root
  [ "$CLI_PBTEST_STATUS" -eq 1 ] && case "$CLI_PBTEST_OUTPUT" in *"--scheduled takes no other option"*) true ;; *) false ;; esac \
    || fail "panel-backup --scheduled --out /root got exit ${CLI_PBTEST_STATUS} and '${CLI_PBTEST_OUTPUT}', expected the refusal of the mixed form"
  checked=$((checked + 1))

  # 4. A configuration anybody but root could have written is refused BEFORE one byte is parsed. The
  # payload is a command substitution: if the reader parsed or sourced it, the witness would exist and
  # the message would be about the VALUE (a quote or a dollar), not about the mode.
  for mode in 0644 0660 0640 0666 0604; do
    cli_pbtest_plant_config "$mode" "PASSPHRASE_FILE=\$(touch ${CLI_INJECTION_WITNESS})\n"
    cli_pbtest_run panel-backup --scheduled
    cli_pbtest_expect_refusal "configuration mode ${mode} (the job)" "must be owned by root:root with mode 0600 or 0400"
    case "$CLI_PBTEST_OUTPUT" in
      *"contains a quote"*|*"not acceptable"*) fail "a mode ${mode} configuration was refused for its CONTENT, so the reader parsed a file it should have refused unread: ${CLI_PBTEST_OUTPUT}" ;;
    esac
    cli_pbtest_run panel-backup-schedule enable
    cli_pbtest_expect_refusal "configuration mode ${mode} (enable)" "must be owned by root:root with mode 0600 or 0400"
    checked=$((checked + 1))
  done
  # A symbolic link at the configuration's name is never followed, even to a perfectly good file.
  cli_pbtest_plant_config 0600 "PASSPHRASE_FILE=${pass_file}\n"
  mv -- "$PANEL_BACKUP_CONFIG_FILE" "${pass_dir}/real.conf"
  ln -s "${pass_dir}/real.conf" "$PANEL_BACKUP_CONFIG_FILE"
  cli_pbtest_run panel-backup --scheduled
  cli_pbtest_expect_refusal "configuration is a symbolic link" "is not a regular file"
  checked=$((checked + 1))
  rm -f -- "$PANEL_BACKUP_CONFIG_FILE"

  # 5. A 0600 configuration whose CONTENT is hostile: every line a refusal naming the first problem
  # and never the value. `@W@` is the injection witness, `@P@` a real passphrase file.
  while IFS='|' read -r name content expected; do
    [ -n "$name" ] || continue
    content="${content//@W@/$CLI_INJECTION_WITNESS}"
    content="${content//@P@/$pass_file}"
    cli_pbtest_plant_config 0600 "$content"
    cli_pbtest_run panel-backup --scheduled
    cli_pbtest_expect_refusal "$name (the job)" "$expected"
    cli_pbtest_run panel-backup-schedule enable
    cli_pbtest_expect_refusal "$name (enable)" "$expected"
    hostile_count=$((hostile_count + 1))
    checked=$((checked + 1))
  done <<'HOSTILE_CONFIGURATIONS'
command substitution|PASSPHRASE_FILE=$(touch @W@)\n|contains a quote
backticks|PASSPHRASE_FILE=`touch @W@`\n|contains a quote
double quotes|PASSPHRASE_FILE="/a/b"\n|contains a quote
single quotes|PASSPHRASE_FILE='/a/b'\n|contains a quote
a backslash|KEEP=1\\2\n|contains a quote
a dollar sign|KEEP=$HOME\n|contains a quote
an unknown key|EVIL=1\n|unknown key
a lower-case key|evil=1\n|is not KEY=VALUE
a line with no equals sign|KEEP\n|is not KEY=VALUE
a line indented before its key| KEEP=3\n|is not KEY=VALUE
a duplicate key|KEEP=3\nKEEP=4\n|more than once
CRLF line endings|KEEP=3\r\n|control character
a tab inside a comment|# a\tcomment\n|control character
a NUL byte|KEEP=3\n\0|NUL byte
an empty required value|KEEP=\n|needs a value
KEEP of zero|KEEP=0\n|is not acceptable
KEEP above 365|KEEP=366\n|is not acceptable
KEEP negative|KEEP=-1\n|is not acceptable
KEEP with a leading zero|KEEP=07\n|is not acceptable
KEEP as an exponent|KEEP=1e3\n|is not acceptable
OUT_DIR with a dot-dot|OUT_DIR=/var/backups/../etc\n|is not acceptable
OUT_DIR relative|OUT_DIR=backups\n|is not acceptable
PASSPHRASE_FILE with a space|PASSPHRASE_FILE=/a b\n|is not acceptable
a newline smuggling a second directive into SCHEDULE|SCHEDULE=daily\nExecStartPre=/bin/touch @W@\n|is not KEY=VALUE
SCHEDULE with a trailing command|SCHEDULE=daily; touch @W@\n|is not acceptable
an http endpoint|S3_ENDPOINT=http://s3.example.invalid\n|is not acceptable
an endpoint with userinfo|S3_ENDPOINT=https://user:secret@s3.example.invalid\n|is not acceptable
an endpoint with a query|S3_ENDPOINT=https://s3.example.invalid/?x=1\n|is not acceptable
an endpoint with a path|S3_ENDPOINT=https://s3.example.invalid/bucket\n|is not acceptable
a bucket with a dot-dot|S3_BUCKET=a..b\n|is not acceptable
a prefix climbing out|S3_PREFIX=../x\n|is not acceptable
a prefix with a dot component|S3_PREFIX=a/./b\n|is not acceptable
an upper-case region|S3_REGION=US-East\n|is not acceptable
no PASSPHRASE_FILE|KEEP=3\n|PASSPHRASE_FILE is not set
a PASSPHRASE_FILE that does not exist|PASSPHRASE_FILE=/nonexistent/passphrase\n|is not a regular file
an S3 key without an endpoint|PASSPHRASE_FILE=@P@\nS3_BUCKET=bucket-one\n|S3 keys are set but S3_ENDPOINT is not
an endpoint without a bucket|PASSPHRASE_FILE=@P@\nS3_ENDPOINT=https://s3.example.invalid\n|S3_BUCKET is not
S3 credential files that do not exist|PASSPHRASE_FILE=@P@\nS3_ENDPOINT=https://s3.example.invalid\nS3_BUCKET=bucket-one\nS3_REGION=region-1\nS3_ACCESS_KEY_ID_FILE=/nonexistent/id\nS3_SECRET_FILE=/nonexistent/secret\n|S3_ACCESS_KEY_ID_FILE is not a regular file
HOSTILE_CONFIGURATIONS
  # The vacuity guard on the list: a case deleted from the table is a refusal no longer made.
  [ "$hostile_count" -eq 38 ] \
    || fail "the hostile configuration table ran ${hostile_count} cases, not the 38 it carries. A case dropped from the list is a check that no longer exists."

  # A passphrase file anyone else can read is refused through the job and through enable, and the same
  # file at 0600 is not.
  cli_pbtest_plant_config 0600 "PASSPHRASE_FILE=${pass_file}\n"
  chmod 0644 "$pass_file"
  cli_pbtest_run panel-backup --scheduled
  cli_pbtest_expect_refusal "a world-readable passphrase file (the job)" "must have mode 0600 or 0400"
  cli_pbtest_run panel-backup-schedule enable
  cli_pbtest_expect_refusal "a world-readable passphrase file (enable)" "must have mode 0600 or 0400"
  chmod 0600 "$pass_file"
  checked=$((checked + 1))
  # The same for the secret key file the S3 keys name.
  cli_pbtest_plant_config 0600 "PASSPHRASE_FILE=${pass_file}\nS3_ENDPOINT=https://s3.example.invalid\nS3_BUCKET=bucket-one\nS3_REGION=region-1\nS3_ACCESS_KEY_ID_FILE=${pass_dir}/key-id\nS3_SECRET_FILE=${pass_dir}/secret\n"
  chmod 0644 "${pass_dir}/secret"
  cli_pbtest_run panel-backup --scheduled
  cli_pbtest_expect_refusal "a world-readable S3 secret file" "S3_SECRET_FILE must be owned by root:root"
  chmod 0600 "${pass_dir}/secret"
  checked=$((checked + 1))

  # 6. The inverse controls: configurations that are well formed get past the reader, as far as the
  # panel this image lacks, with the S3 keys present and no connection made (the run ends first).
  cli_pbtest_plant_config 0600 "PASSPHRASE_FILE=${pass_file}\nKEEP=7\nSCHEDULE=*-*-* 03:15:00\n"
  cli_pbtest_run panel-backup --scheduled
  cli_pbtest_expect_past_the_reader "a minimal valid configuration"
  checked=$((checked + 1))
  cli_pbtest_plant_config 0600 "# a comment\n\nPASSPHRASE_FILE=${pass_file}\nOUT_DIR=${PANEL_BACKUP_OUT_DIR}\nKEEP=365\nSCHEDULE=Mon..Fri *-*-* 01:00:00\nS3_ENDPOINT=https://s3.example.invalid:9000\nS3_BUCKET=bucket-one\nS3_REGION=region-1\nS3_PREFIX=panel/nightly\nS3_ACCESS_KEY_ID_FILE=${pass_dir}/key-id\nS3_SECRET_FILE=${pass_dir}/secret\n"
  cli_pbtest_run panel-backup --scheduled
  cli_pbtest_expect_past_the_reader "a configuration with every key and the S3 keys"
  checked=$((checked + 1))
  # Neither run left an archive, a stage or a second copy of the credentials behind.
  ls "${PANEL_BACKUP_OUT_DIR}"/maran-panel-backup-* >/dev/null 2>&1 \
    && fail "a scheduled panel backup run in an image with no panel left an archive in ${PANEL_BACKUP_OUT_DIR}"
  [ -z "$(find "$PANEL_BACKUP_STAGING_DIR" -mindepth 1 -type d 2>/dev/null)" ] \
    || fail "a refused scheduled panel backup left a stage directory in ${PANEL_BACKUP_STAGING_DIR}; the EXIT trap that removes the plaintext did not run"

  # 7. `enable` with a valid configuration: not refused by the reader; it writes the drop-in the real
  # code renders, root-owned and 0644, enables the timer through the stand-in, and `disable` takes
  # the drop-in away and leaves every archive byte for byte. Needs systemd-analyze for the calendar
  # check; without it, `enable` ends there and the rest is said to be unobserved.
  printf 'a planted archive\n' > "$archive"
  chmod 0600 "$archive"
  cli_pbtest_plant_config 0600 "PASSPHRASE_FILE=${pass_file}\nKEEP=7\nSCHEDULE=*-*-* 03:15:00\n"
  cli_pbtest_run panel-backup-schedule enable
  if command -v systemd-analyze >/dev/null 2>&1 && systemd-analyze calendar '*-*-* 03:15:00' >/dev/null 2>&1; then
    [ "$CLI_PBTEST_STATUS" -eq 0 ] \
      || fail "panel-backup-schedule enable refused a valid configuration (exit ${CLI_PBTEST_STATUS}): ${CLI_PBTEST_OUTPUT}"
    assert_file_is "$dropin" "0:0:644"
    # The effective lines only: the drop-in's own comment wording is not a contract.
    [ "$(grep -v '^[[:space:]]*#' "$dropin")" = "$(printf '%s\n' "[Timer]" "OnCalendar=" "OnCalendar=*-*-* 03:15:00")" ] \
      || fail "the drop-in enable wrote is not the one the design renders from the validated schedule: $(cat "$dropin")"
    [ "$(find "$PANEL_BACKUP_DROPIN_DIR" -mindepth 1 | wc -l)" -eq 1 ] \
      || fail "enable left more than the one drop-in in ${PANEL_BACKUP_DROPIN_DIR} (a temp file was not renamed into place)"
    [ -f "$state" ] && [ "$(head -1 "$state")" = "enabled" ] \
      || fail "enable did not ask systemctl to enable the timer (no enablement recorded at ${state})"
    cli_pbtest_run panel-backup-schedule disable
    [ "$CLI_PBTEST_STATUS" -eq 0 ] \
      || fail "panel-backup-schedule disable failed (exit ${CLI_PBTEST_STATUS}): ${CLI_PBTEST_OUTPUT}"
    [ ! -e "$PANEL_BACKUP_DROPIN_DIR" ] \
      || fail "disable left the drop-in or its directory behind: $(ls -A "$PANEL_BACKUP_DROPIN_DIR" 2>/dev/null)"
    [ "$(head -1 "$state")" = "disabled" ] \
      || fail "disable did not ask systemctl to disable the timer (recorded: $(head -1 "$state"))"
    [ -f "$PANEL_BACKUP_CONFIG_FILE" ] \
      || fail "disable removed the configuration file; it removes the timer and the drop-in and nothing else"
    echo "enable and disable: with a valid configuration enable wrote the drop-in (0644 root:root, exactly the rendered schedule) and enabled the timer through the stand-in; disable removed the drop-in and left the configuration."
  else
    case "$CLI_PBTEST_OUTPUT" in
      *"not a calendar expression systemd accepts"*) ;;
      *) fail "panel-backup-schedule enable on a valid configuration in an image with no usable systemd-analyze did not stop at the calendar check: ${CLI_PBTEST_OUTPUT}" ;;
    esac
    [ ! -e "$PANEL_BACKUP_DROPIN_DIR" ] || fail "enable wrote a drop-in although its calendar check refused"
    echo "UNOBSERVED HERE: no usable systemd-analyze in this image, so enable's success path (the drop-in it renders, the enablement it asks for) and disable were not run."
  fi
  printf 'a planted archive\n' | cmp -s - "$archive" \
    || fail "enable or disable changed or removed an archive in ${PANEL_BACKUP_OUT_DIR}; the archives are the point of the feature"
  rm -f -- "$archive" "$state"
  checked=$((checked + 1))

  # A schedule that has the shape of a calendar and is not one: the reader accepts the shape, and
  # systemd (where it can be asked) is the second check. Nothing is written.
  if command -v systemd-analyze >/dev/null 2>&1 && ! systemd-analyze calendar '*-*-* 99:99:99' >/dev/null 2>&1; then
    cli_pbtest_plant_config 0600 "PASSPHRASE_FILE=${pass_file}\nSCHEDULE=*-*-* 99:99:99\n"
    cli_pbtest_run panel-backup-schedule enable
    cli_pbtest_expect_refusal "a schedule systemd rejects" "not a calendar expression systemd accepts"
    checked=$((checked + 1))
  else
    echo "UNOBSERVED HERE: the shape-versus-calendar split (a SCHEDULE the regex accepts and systemd rejects) was not run; this image has no systemd-analyze that rejects '*-*-* 99:99:99'."
  fi

  # 8. `status` reads and writes nothing, and says nothing about the configuration.
  cli_pbtest_plant_config 0600 "PASSPHRASE_FILE=${pass_file}\nS3_ENDPOINT=https://s3.example.invalid\nS3_BUCKET=bucket-one\nS3_REGION=region-1\nS3_ACCESS_KEY_ID_FILE=${pass_dir}/key-id\nS3_SECRET_FILE=${pass_dir}/secret\n"
  work="$(find /etc/systemd/system -name 'maran-panel-backup*' | sort | tr '\n' ' ')"
  cli_pbtest_run panel-backup-schedule status
  case "$CLI_PBTEST_OUTPUT" in
    *"Scheduled panel backup:"*) ;;
    *) fail "panel-backup-schedule status printed no status block: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  case "$CLI_PBTEST_OUTPUT" in
    *"s3.example.invalid"*|*"bucket-one"*|*"$pass_dir"*|*"AKIAEXAMPLEKEYID0000"*) fail "panel-backup-schedule status printed part of the configuration or a credential: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  [ "$work" = "$(find /etc/systemd/system -name 'maran-panel-backup*' | sort | tr '\n' ' ')" ] \
    || fail "panel-backup-schedule status changed the files under /etc/systemd/system"
  checked=$((checked + 1))

  # 9. Root only, for both new verbs, through a readable copy (the installed file is 0700 and already
  # stops nobody before bash starts). One line, nothing read, nothing written; the same copy as root
  # is accepted.
  copy="$(mktemp -d)"
  chmod 755 "$copy"
  cp -- "$CLI_WRAPPER_INSTALLED" "${copy}/maran-copy"
  chmod 755 "${copy}/maran-copy"
  for hostile in "panel-backup-schedule enable" "panel-backup-schedule disable" "panel-backup-schedule status" "panel-backup --scheduled"; do
    status=0
    # The verb and its action are two words on purpose: unquoted, so they split into argv.
    # shellcheck disable=SC2086
    output="$(run_as_nobody "${copy}/maran-copy" $hostile 2>&1)" || status=$?
    [ "$status" -eq 77 ] \
      || fail "a non-root caller of '${hostile}' got exit ${status}, not 77: ${output}"
    case "$output" in
      *"must be run as root"*) ;;
      *) fail "a non-root caller of '${hostile}' got exit 77 without the explanation: ${output}" ;;
    esac
    [ "$(printf '%s\n' "$output" | wc -l)" -eq 1 ] \
      || fail "a non-root caller of '${hostile}' received more than the one refusal line: ${output}"
    [ ! -e "$PANEL_BACKUP_DROPIN_DIR" ] \
      || fail "a non-root caller of '${hostile}' caused a timer drop-in to be written"
    checked=$((checked + 1))
  done
  CLI_PBTEST_STATUS=0
  CLI_PBTEST_OUTPUT="$("${copy}/maran-copy" panel-backup-schedule status 2>&1)" || CLI_PBTEST_STATUS=$?
  case "$CLI_PBTEST_OUTPUT" in
    *"must be run as root"*) fail "root running the same copy was refused as a non-root caller, so the 77 above may be the wrapper refusing everyone: ${CLI_PBTEST_OUTPUT}" ;;
    *"Scheduled panel backup:"*) ;;
    *) fail "root running the same copy of panel-backup-schedule status got no status block: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  checked=$((checked + 1))
  rm -rf -- "$copy"

  # 10. The upload library, as TEXT: the one outbound call keeps its limits. The checker is shown to
  # find each way of weakening it in a copy, and to accept a copy that differs only by a comment.
  planted_lib="${CLI_LIB_INSTALLED_DIR}/panel-backup-upload.sh"
  [ -z "$(pbupload_curl_defect "$planted_lib")" ] \
    || fail "$(pbupload_curl_defect "$planted_lib") (installer/cli/lib/panel-backup-upload.sh): the upload is the one outbound call of the product and it must stay HTTPS-only, redirect-free, verified and without a credential on argv (rules/security.md item 10)"
  work="$(mktemp -d)"
  sed '$a# curl -k -L --insecure --user a:b' "$planted_lib" > "${work}/commented.sh"
  [ -z "$(pbupload_curl_defect "${work}/commented.sh")" ] \
    || fail "the upload checker REFUSED a copy that differs from the real library only by a comment naming -k, -L, --insecure and --user"
  while IFS='|' read -r name content; do
    [ -n "$name" ] || continue
    sed "$content" "$planted_lib" > "${work}/planted.sh"
    cmp -s "$planted_lib" "${work}/planted.sh" \
      && fail "the planted upload control '${name}' changed nothing, so it proves nothing about the checker"
    [ -n "$(pbupload_curl_defect "${work}/planted.sh")" ] \
      || fail "the upload checker ACCEPTED the planted defect '${name}'. A checker that accepts that guards nothing about the one outbound call."
    upload_count=$((upload_count + 1))
    checked=$((checked + 1))
  done <<'UPLOAD_CONTROLS'
plain http allowed|s#--proto '=https' ##
TLS floor removed|s#--tlsv1.2 ##
redirects followed|s#--max-redirs 0#--max-redirs 5#
a redirect-following -L|s#--max-redirs 0#--max-redirs 0 -L#
certificate verification off with -k|s#--fail --silent#--fail -k --silent#
certificate verification off with --insecure|s#--fail --silent#--fail --insecure --silent#
a credential on argv with --user|s#--fail --silent#--fail --user a:b --silent#
the user's .curlrc read|s#curl -q #curl #
the response body kept|s#--output /dev/null#--output /tmp/body#
the credentials file not passed|s#--config "\$config"#--config /dev/null#
UPLOAD_CONTROLS
  [ "$upload_count" -eq 10 ] \
    || fail "the upload library checker ran ${upload_count} planted controls, not the 10 it carries"
  rm -rf -- "$work"
  checked=$((checked + 1))

  rm -rf -- "$pass_dir"
  rm -f -- "$CLI_INJECTION_WITNESS"
  remove_the_installed_break_glass_command
  panel_backup_restore_prior_state
  [ ! -e "$PANEL_BACKUP_CONFIG_FILE" ] && [ ! -e "$PANEL_BACKUP_DROPIN_DIR" ] \
    || fail "this assertion left the panel backup configuration or the timer drop-in behind"

  echo "The scheduled panel backup verbs: panel-backup-schedule takes exactly one of enable, disable or status (hostile and unknown actions get a refusal and create nothing), enable refuses without the units and without a configuration and writes nothing before every check passed, a configuration anybody else could write is refused unread (mode 0644, 0660, 0640, 0666 and 0604, and a symbolic link), 38 hostile 0600 configurations are each refused naming the first problem, the job and enable alike, none of them creating the witness file or echoing its value, two well-formed configurations get past the reader, enable renders and disable removes the drop-in and keeps every archive, status prints no configuration, a non-root caller of all four commands gets exit 77 and one line (the same copy as root is accepted), and the upload library's curl keeps --proto =https, --tlsv1.2, --max-redirs 0, -q and no -k, -L or --user (the checker shown to find ten planted weakenings). ${checked} checks made."
  echo "UNOBSERVED HERE: this image boots no systemd and holds no panel. --scheduled is run only as far as the configuration reader and the first panel check; retention's deletions, the gpg archive and the S3 request are never run here (the install polygon's recovery stage owns them); the journal line a failure writes under the tag maran-cli is not read; the curl check reads TEXT, so a flag assembled at run time is invisible to it and the secrets never being on argv is argued by the library's construction, not measured with ps."
}
