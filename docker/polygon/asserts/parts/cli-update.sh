#!/usr/bin/env bash
# step 66: `maran update` and `maran rollback` refuse what they should, before anything is touched
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in. It is a part of its own and
# not an addition to cli-recovery.sh because that file already sits at the size where a reviewer
# stops reading (rules/architecture.md, "Files stay small"); it reuses that file's helpers
# (run_cli_step, run_as_nobody, cli_pbtest_run, remove_the_installed_break_glass_command) and so is
# sourced after it.
#
# The two verbs replace the code a root process runs, so the properties worth a gate here are the
# ones that keep an operator's text from reaching a command and that keep a refused run from having
# done anything: who may run them, what an unknown, repeated or contradictory option does, what a
# value that is not a plain path does, and what the verbs leave on the disk when they refuse.
# Everything below RUNS the installed wrapper and looks at its answers and at the disk; the one
# thing it reads as text is stated where it is read. Every refusal is paired with an INVERSE
# CONTROL, because a parser mutated to refuse everything passes every test that only hands it bad
# input.

# Where the stand-in for curl records that it was started. A refused run must never reach the
# network, and the outbound request is the one command `maran update` is built around, so "never
# reached a command" is observed on it: the real curl is moved aside for the duration of the
# assertion and a stand-in that records its argv and fails takes its place.
readonly CLI_UPDATE_CURL_LOG="/tmp/maran-polygon-update-curl-argv"
# The verbs' refusal wording is the CLI's own and is matched here by pattern. What IS fixed is the
# shape: a refusal exits 1 (a refusal through cli_fail) or 64 (a usage error, which names the verb
# and prints its usage line), and says what was wrong in words the pattern given to each case
# below recognises.
# CLI_UPDATE_ARGUMENT_REFUSALS is the set of phrases an argument refusal uses; an inverse control
# must not print any of them.
readonly CLI_UPDATE_ARGUMENT_REFUSALS='unknown option|unknown argument|unexpected argument|given twice|more than once|needs a value|must not be empty or start|cannot be combined|channel is stable or beta|usage: maran (update|rollback)'

# The wording the lock refusal uses. update, rollback, panel-backup and panel-restore share one lock
# (installer/cli/lib/panel-archive.sh), so the sentence names all four; it is matched whole because
# it is the one message an operator who started a second run reads, and a message that still named
# only "backup or restore" would send them looking for the wrong process.
readonly CLI_UPDATE_LOCK_REFUSAL='another panel backup, restore, update or rollback is running'
# The lock file the four verbs take (MARAN_PANEL_LOCK_FILE under MARAN_CLI_STAGING_BASE), a literal
# on purpose: reading it out of the library would agree with the library by construction.
readonly CLI_UPDATE_LOCK_FILE="/var/lib/maran-cli-staging/panel-archive.lock"

# The count the refusal table is held to. Raise it with the table: a case deleted from the list is
# a refusal no longer made, and a `for` over fewer cases exits 0 and prints nothing different.
readonly CLI_UPDATE_REFUSAL_CASES=37
CLI_UPDATE_REFUSALS_RUN=0
CLI_UPDATE_LAYOUT_BEFORE=""

# cli_upd_layout_state: one line per path the two verbs are allowed to create, saying what is
# there (type, owner, mode, link target) or that it is absent, and the releases directory's
# listing. Compared before and after every refusal: a refusal that left a release directory, a
# pointer, a state file or a dump directory behind was not a refusal.
cli_upd_layout_state() {
  local path
  for path in /usr/local/maran/releases /usr/local/maran/current /usr/local/maran/api \
    /usr/local/maran/agent /usr/local/maran/frontend /etc/maran/release.state /var/backups/maran-update; do
    if [ -e "$path" ] || [ -L "$path" ]; then
      stat -c '%n %F %u:%g %a -> %N' -- "$path"
    else
      printf '%s absent\n' "$path"
    fi
  done
  # find exits 1 when either directory is absent, which is the ordinary state of a fresh image, and
  # under `set -o pipefail` that status would become this function's: the bare assignment that
  # captures the "before" snapshot would then end the whole build with no message. An absent
  # directory is already reported above as `absent`, so the listing may be empty without a failure.
  { find /usr/local/maran/releases /var/backups/maran-update -mindepth 1 2>/dev/null || true; } | sort
}

# cli_upd_run: the installed wrapper with the given arguments and standard input closed, so a
# confirmation prompt can never hang the image build; status in CLI_PBTEST_STATUS and
# stdout+stderr in CLI_PBTEST_OUTPUT (the globals cli_pbtest_run uses).
cli_upd_run() {
  CLI_PBTEST_STATUS=0
  CLI_PBTEST_OUTPUT="$("$CLI_WRAPPER_INSTALLED" "$@" 2>&1 </dev/null)" || CLI_PBTEST_STATUS=$?
}

# cli_upd_expect_nothing_done: the disk is as cli_upd_layout_state found it and the injection
# witness was never created; unless $2 is "reached-curl", the curl stand-in was never started
# either (a well-formed `update --check` legitimately reaches it, and its record is then cleared).
#   $1 = case name, $2 = optional "reached-curl".
cli_upd_expect_nothing_done() {
  local name="$1" mode="${2:-}"
  [ ! -e "$CLI_INJECTION_WITNESS" ] \
    || { rm -f -- "$CLI_INJECTION_WITNESS"; fail "the case '${name}' CREATED ${CLI_INJECTION_WITNESS}: an operator's value was interpreted"; }
  if [ "$mode" = "reached-curl" ]; then
    rm -f -- "$CLI_UPDATE_CURL_LOG"
  else
    [ ! -e "$CLI_UPDATE_CURL_LOG" ] \
      || fail "the case '${name}' started curl ($(cat "$CLI_UPDATE_CURL_LOG")): a refused run reached the network"
  fi
  [ "$(cli_upd_layout_state)" = "$CLI_UPDATE_LAYOUT_BEFORE" ] \
    || fail "the case '${name}' changed the release layout, the release state or the update dump directory"
}

# cli_upd_expect_refusal: runs `maran <verb> <args>` and requires a refusal that names the verb and
# the reason, created nothing, started nothing and never echoed a hostile value back.
#   $1 = case name, $2 = verb, $3 = ERE (case-insensitive) the message must match, $4... = arguments.
# A usage error (64) must also name the verb; a refusal through cli_fail (1) need not, its text is
# about the value.
cli_upd_expect_refusal() {
  local name="$1" verb="$2" reason="$3"
  shift 3
  cli_upd_run "$verb" "$@"
  case "$CLI_PBTEST_STATUS" in
    1|64) ;;
    *) fail "the case '${name}' got exit ${CLI_PBTEST_STATUS}, not the 1 of a refusal or the 64 of a usage error: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  if [ "$CLI_PBTEST_STATUS" -eq 64 ]; then
    grep -qiF -- "maran ${verb}" <<<"$CLI_PBTEST_OUTPUT" \
      || fail "the usage error of the case '${name}' does not name the verb ('maran ${verb}'): ${CLI_PBTEST_OUTPUT}"
  fi
  grep -qiE -- "$reason" <<<"$CLI_PBTEST_OUTPUT" \
    || fail "the case '${name}' was refused for the wrong reason (expected a message matching '${reason}'): ${CLI_PBTEST_OUTPUT}"
  case "$CLI_PBTEST_OUTPUT" in
    *"touch"*|*"$CLI_INJECTION_WITNESS"*) fail "the refusal of the case '${name}' echoed the hostile value back to the operator and the journal: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  cli_upd_expect_nothing_done "$name"
  CLI_UPDATE_REFUSALS_RUN=$((CLI_UPDATE_REFUSALS_RUN + 1))
}

# cli_upd_expect_past_argument_handling: the last run was NOT refused by the argument handling:
# none of the argument-refusal phrases is in the output, and the verb was not taken for an unknown
# command. It says nothing about what the verb then did (an image with no release to update to
# fails somewhere after this, which is the point of the control and not a defect).
#   $1 = case name.
cli_upd_expect_past_argument_handling() {
  local name="$1"
  grep -qiE -- "$CLI_UPDATE_ARGUMENT_REFUSALS|unknown command" <<<"$CLI_PBTEST_OUTPUT" \
    && fail "the well-formed case '${name}' was refused by the argument handling, so every refusal in this assertion may be the parser refusing everything: ${CLI_PBTEST_OUTPUT}"
  [ "$CLI_PBTEST_STATUS" -ne 77 ] \
    || fail "the well-formed case '${name}' was refused as a non-root caller while running as root: ${CLI_PBTEST_OUTPUT}"
  cli_upd_expect_nothing_done "$name" reached-curl
}

# assert_the_update_and_rollback_verbs_refuse_what_they_should: step 66 runs, and the two verbs it
# wires into the wrapper answer a non-root caller with 77, refuse every malformed, repeated,
# contradictory or hostile argument with a message that names the verb, and leave nothing behind
# when they do.
#
# What it shows, each half with its inverse control: a non-root caller of `update` and `rollback`
# gets exit 77 and one line (the same copy as root is not refused that way); every refusal below
# exits 1 or 64, names the verb and the reason, changes nothing under the release layout, state
# file or dump directory, creates no witness file and starts no curl (and the stand-in is shown to
# record a start, so its silence means something); well-formed spellings of the same options are
# NOT refused by the argument handling. `--offline-tarball` with a hostile value is held against a
# FILE THAT EXISTS under that very name, so "no such file" cannot be what refused it.
#
# UNOBSERVED HERE: this image boots no systemd and holds no installed release, so what the verbs do
# AFTER their argument handling is not run: no manifest is fetched or verified (curl is a stand-in
# that fails), no dump is taken, no release is unpacked, no link is switched, no service is
# restarted. `rollback`'s refusal for having nothing to roll back is observed, its ACCEPTANCE (a
# state with a previous release goes on to switch and restart) is not: that needs services. The
# wording of the refusals is the CLI's and is matched by pattern, so a message reworded to say the
# same thing in other words fails this assertion until the pattern follows it. SELinux contexts are
# not exercised by a container build.
assert_the_update_and_rollback_verbs_refuse_what_they_should() {
  local checked=0 work copy real_curl hostile_path plain_path status output verb args lock_existed
  local staging_existed=0 stub_dir="/usr/bin"

  run_cli_step >/dev/null || fail "step 66's step_cli could not install the command this assertion runs"
  [ -d /var/lib/maran-cli-staging ] && staging_existed=1

  # 0. The witness file is creatable, and the curl stand-in records a start. Both are the controls
  # for every "nothing happened" below.
  : > "$CLI_INJECTION_WITNESS"
  [ -e "$CLI_INJECTION_WITNESS" ] || fail "${CLI_INJECTION_WITNESS} cannot be created, so its absence below would prove nothing"
  rm -f -- "$CLI_INJECTION_WITNESS"
  real_curl="$(command -v curl || true)"
  [ -z "$real_curl" ] || [ "$(dirname -- "$real_curl")" = "$stub_dir" ] \
    || fail "curl is at ${real_curl}, outside ${stub_dir}, so this assertion cannot put its stand-in where the wrapper's pinned PATH finds it first"
  rm -f -- "$CLI_UPDATE_CURL_LOG"
  if [ -n "$real_curl" ]; then
    mv -- "$real_curl" "${real_curl}.polygon-real"
  fi
  cat > "${stub_dir}/curl" <<STUB
#!/bin/sh
# Polygon stand-in: records that it was started and fails like an unreachable host.
printf '%s\n' "\$*" >> ${CLI_UPDATE_CURL_LOG}
exit 6
STUB
  chmod 0755 "${stub_dir}/curl"
  "${stub_dir}/curl" --probe >/dev/null 2>&1 || true
  [ -s "$CLI_UPDATE_CURL_LOG" ] \
    || fail "the curl stand-in does not record a start, so a refused run's empty record below would prove nothing"
  rm -f -- "$CLI_UPDATE_CURL_LOG"
  CLI_UPDATE_LAYOUT_BEFORE="$(cli_upd_layout_state)"
  CLI_UPDATE_REFUSALS_RUN=0

  work="$(mktemp -d)"
  chmod 755 "$work"
  checked=$((checked + 1))

  # 1. The verbs exist: the wrapper's help lists them and neither is an unknown command.
  cli_upd_run help
  grep -qE '(^|[[:space:]])update[[:space:]]' <<<"$CLI_PBTEST_OUTPUT" && grep -qE '(^|[[:space:]])rollback[[:space:]]' <<<"$CLI_PBTEST_OUTPUT" \
    || fail "maran help does not list update and rollback: ${CLI_PBTEST_OUTPUT}"
  checked=$((checked + 1))

  # 1b. `maran help --json` prints the catalogue the installer put in place (issue #155): the bytes
  # of the file in the package, root 0644, and a document a JSON parser accepts. The installed file
  # is compared with the PACKAGE's copy and not with the printed text, so a catalogue that never
  # reached the host fails here instead of agreeing with an empty print. UNOBSERVED HERE: whether
  # the catalogue is complete; that is the CI gate `maran operator-commands`, which reads the sources.
  local catalogue_installed="/usr/local/maran/cli/commands.json"
  [ -f "$catalogue_installed" ] && [ "$(stat -c '%u:%g:%a' -- "$catalogue_installed")" = "0:0:644" ] \
    || fail "${catalogue_installed} is missing or is not root:root 0644"
  cmp -s "${INSTALLER_ROOT}/cli/commands.json" "$catalogue_installed" \
    || fail "${catalogue_installed} differs from installer/cli/commands.json: the step installed something other than the package carries"
  "$CLI_WRAPPER_INSTALLED" help --json </dev/null | cmp -s - "${INSTALLER_ROOT}/cli/commands.json" \
    || fail "maran help --json does not print installer/cli/commands.json byte for byte"
  "$CLI_WRAPPER_INSTALLED" help --json </dev/null | grep -q '^  "schema": 1,$' \
    || fail "maran help --json does not print a schema 1 catalogue (the image has no JSON parser; CI parses the file)"
  cli_upd_run help --json-bogus
  grep -q 'Usage: maran' <<<"$CLI_PBTEST_OUTPUT" && ! grep -q '"schema"' <<<"$CLI_PBTEST_OUTPUT" \
    || fail "maran help with any word but --json must print the usage text, not the catalogue: ${CLI_PBTEST_OUTPUT}"
  checked=$((checked + 1))

  # 2. `update`: an option it does not have, one given twice, a value outside the allow-list, a
  # contradictory pair, a value that is an option, and arguments that are not options at all.
  cli_upd_expect_refusal "update with an unknown option" update 'unknown (option|argument)' --frobnicate
  cli_upd_expect_refusal "update with a hostile option name" update 'unknown (option|argument)' "--check;touch ${CLI_INJECTION_WITNESS}"
  cli_upd_expect_refusal "update with a short option" update 'unknown (option|argument)' -x
  cli_upd_expect_refusal "update with a combined short option" update 'unknown (option|argument)' -cy
  cli_upd_expect_refusal "update with an option and a newline in one argument" update 'unknown (option|argument)' $'--check\n--yes'
  cli_upd_expect_refusal "update with a repeated --check" update 'given twice|more than once|repeated' --check --check
  cli_upd_expect_refusal "update with a repeated --yes" update 'given twice|more than once|repeated' --yes --yes
  cli_upd_expect_refusal "update with a repeated --channel of the same value" update 'given twice|more than once|repeated' --channel stable --channel stable
  cli_upd_expect_refusal "update with two different channels" update 'given twice|more than once|repeated' --channel stable --channel beta
  cli_upd_expect_refusal "update with a repeated --offline-tarball" update 'given twice|more than once|repeated' --offline-tarball /a --offline-tarball /b
  cli_upd_expect_refusal "update with --channel nightly" update 'stable|beta' --channel nightly
  cli_upd_expect_refusal "update with --channel in capitals" update 'stable|beta' --channel STABLE
  cli_upd_expect_refusal "update with an empty --channel" update 'must not be empty|stable|beta' --channel ''
  cli_upd_expect_refusal "update with --channel and no value" update 'needs a value' --channel
  cli_upd_expect_refusal "update with --channel carrying a command substitution" update 'stable|beta' --channel "\$(touch ${CLI_INJECTION_WITNESS})"
  cli_upd_expect_refusal "update with --channel carrying a second command" update 'stable|beta' --channel "stable;touch ${CLI_INJECTION_WITNESS}"
  cli_upd_expect_refusal "update with --channel carrying a newline" update 'stable|beta' --channel $'stable\nbeta'
  cli_upd_expect_refusal "update with --offline-tarball and --channel" update 'offline-tarball and --channel cannot be combined' --offline-tarball /nonexistent/bundle.tar.gz --channel stable
  cli_upd_expect_refusal "update with --channel and --offline-tarball" update 'offline-tarball and --channel cannot be combined' --channel beta --offline-tarball /nonexistent/bundle.tar.gz
  cli_upd_expect_refusal "update with an --offline-tarball value that is an option" update 'must not be empty or start' --offline-tarball -x
  cli_upd_expect_refusal "update with an --offline-tarball value that is another verb option" update 'must not be empty or start' --offline-tarball --yes
  cli_upd_expect_refusal "update with --offline-tarball and no value" update 'needs a value' --offline-tarball
  cli_upd_expect_refusal "update with a positional word" update 'unknown argument|unexpected argument' extra
  cli_upd_expect_refusal "update with a channel name as a positional word" update 'unknown argument|unexpected argument' stable
  cli_upd_expect_refusal "update with an empty positional argument" update 'unknown argument|unexpected argument' ''

  # The injection witness, with the value an operator would hand a root command: a path that holds
  # a semicolon, a command substitution and a newline. The FILE EXISTS under that exact name, so
  # "no such file" cannot be the reason it is refused, and the refusal must be about the value. It
  # update.sh vets the path's characters right after parsing its arguments, before the lock and
  # before any read of the layout or the disk, so these two refusals need nothing else in place.
  hostile_path="${work}/bundle;touch ${CLI_INJECTION_WITNESS}\$(touch ${CLI_INJECTION_WITNESS})"$'\n'"line.tar.gz"
  # The witness path inside the hostile name carries slashes of its own, so the name is a chain of
  # directories and the file only exists once that chain does. Without it the printf below fails
  # with "No such file or directory" and the build dies there, naming nothing.
  mkdir -p -- "$(dirname -- "$hostile_path")"
  printf 'not a bundle\n' > "$hostile_path"
  [ -f "$hostile_path" ] \
    || fail "the hostile bundle path cannot be created in ${work}, so it being refused below would only mean the file was absent"
  cli_upd_expect_refusal "update with a bundle path holding ;, \$(...) and a newline" update 'absolute path of letters' --offline-tarball "$hostile_path"
  cli_upd_expect_refusal "update with a relative bundle path" update 'absolute path of letters' --offline-tarball relative/bundle.tar.gz

  # 3. `rollback`: the same kinds of refusal over its own options.
  cli_upd_expect_refusal "rollback with an unknown option" rollback 'unknown (option|argument)' --frobnicate
  cli_upd_expect_refusal "rollback with a hostile option name" rollback 'unknown (option|argument)' "--yes;touch ${CLI_INJECTION_WITNESS}"
  cli_upd_expect_refusal "rollback given update's --channel" rollback 'unknown (option|argument)' --channel stable
  cli_upd_expect_refusal "rollback with a repeated --yes" rollback 'given twice|more than once|repeated' --yes --yes
  cli_upd_expect_refusal "rollback with a repeated --restore-database" rollback 'given twice|more than once|repeated' --restore-database --restore-database
  cli_upd_expect_refusal "rollback with a positional word" rollback 'unknown argument|unexpected argument' 1.0.0

  # 4. Nothing to roll back: in an image with no release.state and no previous release the verb
  # refuses, with or without the confirmation, and with the database restore asked for. Spelled
  # without a hostile value on purpose: this is the case that is the inverse control of the
  # argument refusals above (the options are well formed and it is refused for a DIFFERENT
  # reason), and it must change nothing and start nothing either.
  cli_upd_expect_refusal "rollback with no previous release" rollback 'no previous release'
  grep -qiE -- "$CLI_UPDATE_ARGUMENT_REFUSALS" <<<"$CLI_PBTEST_OUTPUT" \
    && fail "rollback with no arguments was refused as a usage error, not for having nothing to roll back: ${CLI_PBTEST_OUTPUT}"
  cli_upd_expect_refusal "rollback --yes with no previous release" rollback 'no previous release' --yes
  grep -qiE -- "$CLI_UPDATE_ARGUMENT_REFUSALS" <<<"$CLI_PBTEST_OUTPUT" \
    && fail "rollback --yes was refused as a usage error, not for having nothing to roll back: ${CLI_PBTEST_OUTPUT}"
  cli_upd_expect_refusal "rollback --restore-database --yes with no previous release" rollback 'no previous release' --restore-database --yes
  grep -qiE -- "$CLI_UPDATE_ARGUMENT_REFUSALS" <<<"$CLI_PBTEST_OUTPUT" \
    && fail "rollback --restore-database --yes was refused as a usage error, not for having nothing to roll back: ${CLI_PBTEST_OUTPUT}"

  # 4b. The shared lock. This shell takes it on a descriptor of its own, and a verb started while it
  # is held must refuse with the sentence naming all four verbs and change nothing. `rollback` is
  # the representative: it takes the lock before it reads any state, so the refusal is the lock's
  # and not "no previous release". The inverse control releases the lock and requires the SAME
  # command to get past it (to the state refusal), so a verb mutated to refuse every run for "the
  # lock" is seen. The old two-verb wording is refused by the pattern on purpose.
  lock_existed=0
  [ -e "$CLI_UPDATE_LOCK_FILE" ] && lock_existed=1
  install -d -m 0700 -o root -g root /var/lib/maran-cli-staging
  exec 7> "$CLI_UPDATE_LOCK_FILE"
  flock -n 7 || fail "this assertion could not take ${CLI_UPDATE_LOCK_FILE} itself, so a verb refusing for the lock below would prove nothing"
  cli_upd_expect_refusal "rollback while another panel backup, restore, update or rollback holds the lock" rollback "$CLI_UPDATE_LOCK_REFUSAL"
  exec 7>&-
  cli_upd_run rollback
  case "$CLI_PBTEST_OUTPUT" in
    *"$CLI_UPDATE_LOCK_REFUSAL"*) fail "rollback still reported the lock as held after this assertion released it, so the refusal above may be the verb refusing every run: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  grep -qiE 'no previous release' <<<"$CLI_PBTEST_OUTPUT" \
    || fail "rollback with the lock free was not refused for having nothing to roll back, so it did not get past the lock: ${CLI_PBTEST_OUTPUT}"
  [ "$lock_existed" -eq 1 ] || rm -f -- "$CLI_UPDATE_LOCK_FILE"

  # The refusal table's vacuity guard, on the list itself.
  [ "$CLI_UPDATE_REFUSALS_RUN" -eq $CLI_UPDATE_REFUSAL_CASES ] \
    || fail "the update and rollback refusal list ran ${CLI_UPDATE_REFUSALS_RUN} cases, not the ${CLI_UPDATE_REFUSAL_CASES} it carries. A case dropped from the list is a refusal no longer checked."
  checked=$((checked + CLI_UPDATE_REFUSALS_RUN))

  # 5. The inverse controls: the same options, spelled correctly, get PAST the argument handling.
  # Each goes on to fail or stop somewhere else in an image that has no release host and no
  # installed release (curl is the stand-in), which is exactly what shows the parser did not do it.
  cli_upd_run update --check
  cli_upd_expect_past_argument_handling "update --check"
  cli_upd_run update --check --channel beta
  cli_upd_expect_past_argument_handling "update --check --channel beta"
  cli_upd_run update --channel stable --check --yes
  cli_upd_expect_past_argument_handling "update --channel stable --check --yes"
  plain_path="${work}/bundle.tar.gz"
  printf 'not a bundle\n' > "$plain_path"
  cli_upd_run update --offline-tarball "$plain_path"
  cli_upd_expect_past_argument_handling "update --offline-tarball <plain existing path>"
  case "$CLI_PBTEST_OUTPUT" in
    *"absolute path of letters"*) fail "update --offline-tarball refused an ordinary absolute path for its characters, so the refusal of the hostile path above may be it refusing every path: ${CLI_PBTEST_OUTPUT}" ;;
  esac
  [ "$CLI_PBTEST_STATUS" -ne 0 ] \
    || fail "update --offline-tarball succeeded on a file that is not a signed bundle: ${CLI_PBTEST_OUTPUT}"
  cli_upd_run update --offline-tarball "$plain_path" --yes
  cli_upd_expect_past_argument_handling "update --offline-tarball <plain existing path> --yes"
  cli_upd_run rollback --restore-database --yes
  cli_upd_expect_past_argument_handling "rollback --restore-database --yes"
  checked=$((checked + 6))

  # 6. Root only, through a readable copy (the installed file is 0700 and already stops nobody
  # before bash starts). The check runs BEFORE the arguments are read, so a malformed one still
  # gets 77 and the one line; nothing is read, written or started; the same copy as root is not
  # refused that way.
  copy="${work}/copy"
  mkdir -p "$copy"
  chmod 755 "$copy"
  cp -- "$CLI_WRAPPER_INSTALLED" "${copy}/maran-copy"
  chmod 755 "${copy}/maran-copy"
  for args in "update" "update --check" "update --offline-tarball /nonexistent/bundle.tar.gz --yes" \
    "update --frobnicate" "update --channel nightly" "rollback" "rollback --yes" "rollback --restore-database --yes" \
    "rollback --frobnicate"; do
    status=0
    # The verb and its options are separate words on purpose: unquoted, so they split into argv.
    # shellcheck disable=SC2086
    output="$(run_as_nobody "${copy}/maran-copy" $args 2>&1 </dev/null)" || status=$?
    [ "$status" -eq 77 ] \
      || fail "a non-root caller of 'maran ${args}' got exit ${status}, not 77: ${output}"
    case "$output" in
      *"must be run as root"*) ;;
      *) fail "a non-root caller of 'maran ${args}' got exit 77 without the explanation: ${output}" ;;
    esac
    [ "$(printf '%s\n' "$output" | wc -l)" -eq 1 ] \
      || fail "a non-root caller of 'maran ${args}' received more than the one refusal line: ${output}"
    cli_upd_expect_nothing_done "a non-root caller of 'maran ${args}'"
    checked=$((checked + 1))
  done
  # Inverse control: the SAME copy, as root, is refused for the argument and not as a non-root
  # caller, so the 77 above is the root check and not the verb refusing everyone.
  for args in "update --frobnicate" "rollback --frobnicate"; do
    status=0
    # shellcheck disable=SC2086
    output="$("${copy}/maran-copy" $args 2>&1 </dev/null)" || status=$?
    [ "$status" -ne 77 ] && [ "$status" -ne 0 ] \
      || fail "root running a copy of the wrapper with 'maran ${args}' got exit ${status}, so the 77 above may be the wrapper refusing everyone: ${output}"
    grep -qiE 'unknown (option|argument)' <<<"$output" \
      || fail "root running a copy of the wrapper with 'maran ${args}' was not refused for its argument: ${output}"
    checked=$((checked + 1))
  done

  # 7. Put the image back as it was found: the real curl, the staging directory this run may have
  # created, the wrapper and its libraries. Verified, not assumed.
  rm -f -- "${stub_dir}/curl" "$CLI_UPDATE_CURL_LOG" "$CLI_INJECTION_WITNESS"
  if [ -n "$real_curl" ]; then
    mv -- "${real_curl}.polygon-real" "$real_curl"
    [ -x "$real_curl" ] && ! grep -q 'Polygon stand-in' "$real_curl" 2>/dev/null \
      || fail "${real_curl} is not the real curl after this assertion put it back"
  fi
  [ "$staging_existed" -eq 1 ] || rm -rf -- /var/lib/maran-cli-staging
  rm -rf -- "$work"
  [ "$(cli_upd_layout_state)" = "$CLI_UPDATE_LAYOUT_BEFORE" ] \
    || fail "this assertion left the release layout, the release state or the update dump directory different from how it found them"
  remove_the_installed_break_glass_command
  [ ! -e "$CLI_WRAPPER_INSTALLED" ] || fail "${CLI_WRAPPER_INSTALLED} is still present after this assertion removed it"

  echo "The update and rollback verbs: a non-root caller of either (with a good or a malformed argument) gets exit 77 and one line, and the same copy as root is refused for its argument instead; ${CLI_UPDATE_REFUSALS_RUN} refusals (unknown, hostile, repeated, contradictory and positional arguments, a channel outside stable|beta, --offline-tarball with --channel or with a value that is an option or a path holding ;, \$(...) and a newline, a rollback with nothing to roll back, and a rollback while another panel backup, restore, update or rollback holds the shared lock) each exit 1 or 64, name the verb and the reason, leave the release layout, the state file and the dump directory exactly as found, create no witness file and start no curl (the stand-in shown to record a start); the same options spelled correctly are not refused by the argument handling. ${checked} checks made."
  echo "UNOBSERVED HERE: nothing after the argument handling runs. curl is a stand-in that fails, so no manifest is fetched, verified or read, no dump is taken, no release is unpacked, no link is switched and no service is restarted; rollback's acceptance of a state that HAS a previous release is not observed, only its refusal of one that has none. The refusal wording is matched by pattern. The hostile path is held against an existing file of that name, but a value reaching a command only on the paths past the argument handling would not be seen here."
}
