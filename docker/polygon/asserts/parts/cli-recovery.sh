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
readonly CLI_LIB_NAMES="common.sh status.sh reset.sh diagnostics.sh panel-archive.sh panel-backup.sh panel-restore.sh"
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
  # Exactly the seven libraries: an extra file in a directory root sources from is a file root runs.
  libs_found="$(find "$CLI_LIB_INSTALLED_DIR" -mindepth 1 | wc -l)"
  [ "$libs_found" -eq 7 ] \
    || fail "${CLI_LIB_INSTALLED_DIR} holds ${libs_found} entries, expected exactly 7 ($CLI_LIB_NAMES)"
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
  [ "$(printf '%s\n' $code_files | wc -l)" -eq 8 ] \
    || fail "the fixed-argv search covered $(printf '%s\n' $code_files | wc -l) files, expected the wrapper and its 7 libraries"
  checked=$((checked + 1))

  # The one place the panel's binary is started is a fixed argv ending in the typed words, and the
  # wrapper pins its own PATH. Counted with a pattern that also matches the real line, so a rename
  # of either cannot leave this passing over nothing.
  [ "$(sed 's/^[[:space:]]*#.*$//' "${CLI_LIB_INSTALLED_DIR}/common.sh" | grep -cE '^[[:space:]]*exec runuser -u "\$MARAN_CLI_SERVICE_USER" -- "\$MARAN_CLI_BINARY" --cli "\$@"$')" -eq 1 ] \
    || fail "common.sh no longer starts the panel with exactly 'exec runuser -u \"\$MARAN_CLI_SERVICE_USER\" -- \"\$MARAN_CLI_BINARY\" --cli \"\$@\"'"
  # runuser appears on exactly four lines, each a fixed argv as the service account: the panel's
  # --cli mode, its --migrate mode (panel-restore), pg_dump (panel-backup) and pg_restore
  # (panel-restore). A fifth is something new that runs as somebody, and is read before it passes.
  [ "$(cat $code_files | sed 's/^[[:space:]]*#.*$//' | grep -c 'runuser')" -eq 4 ] \
    || fail "the break-glass command mentions runuser on a line other than the four known ones (--cli, --migrate, pg_dump, pg_restore)"
  [ "$(cat $code_files | sed 's/^[[:space:]]*#.*$//' | grep -cE 'runuser -u "\$MARAN_CLI_SERVICE_USER" -- (pg_dump|pg_restore|"\$MARAN_PANEL_MIGRATE_BINARY" --migrate|"\$MARAN_CLI_BINARY" --cli)')" -eq 4 ] \
    || fail "a runuser line no longer runs one of the four fixed commands as the service account"
  grep -qxF 'PATH=/usr/sbin:/usr/bin:/sbin:/bin' "$CLI_WRAPPER_INSTALLED" \
    || fail "the wrapper no longer pins PATH=/usr/sbin:/usr/bin:/sbin:/bin on a line of its own"
  checked=$((checked + 1))

  remove_the_installed_break_glass_command
  [ ! -e "$CLI_WRAPPER_INSTALLED" ] || fail "${CLI_WRAPPER_INSTALLED} is still present after this assertion removed it"

  echo "The break-glass command: ${CLI_WRAPPER_INSTALLED} is 0700 root:root and its 7 libraries 0644 root:root under root-only 0755 directories, step 66 created no setuid file and no sudoers entry, a non-root caller gets exit 77 (and the same copy run as root is accepted), an unknown verb gets 64, hostile verbs and identifiers create nothing (witness file shown creatable), panel-restore refuses without its confirmation and panel-backup refuses a passphrase file others can read (and accepts a 0600 one as far as the absent panel), a hostile PATH cannot supply the euid check, and no line of the wrapper or its libraries evals or starts 'sh -c' (the search shown to find all four planted shapes and none of the look-alikes). ${checked} checks made."
  echo "UNOBSERVED HERE: the fixed-argv check reads TEXT. It sees 'eval' and a shell started with -c; it does not see an unquoted expansion of operator input, a here-document that is later sourced, or a command built across lines. The shell quoting itself was reviewed by reading and by the hostile inputs above, not by a formal analysis."
  echo "UNOBSERVED HERE: this image boots no systemd and holds no panel, so status, admin-reset-password, collect-diagnostics, panel-backup and panel-restore are run only as far as their argument handling. Their behaviour against a live panel is scored by scripts/lib/install-polygon.sh (lockout-recovery and diagnostics stages). SELinux contexts on the wrapper and /usr/local/maran/cli are not exercised by a container build."
}
