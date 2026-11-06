#!/usr/bin/env bash
# Cases 10 to 18 of `maran deploy-local selftest` (issue #410): the blind spots a hostile review found
# in cases 1 to 9. SOURCED by scripts/lib/deploy-local-selftest.sh, which owns the fixtures (the
# DEPLOY_ST_* globals), the fake ssh and scp, and the helpers used here (deploy_st_run, deploy_st_exec,
# deploy_st_expect_run, deploy_st_expect_refused, deploy_st_expect_accepted, deploy_st_logged,
# deploy_st_said, deploy_st_mode, deploy_st_pass, deploy_st_fail).
#
# Every refusal below is paired with an inverse control: the same command with the offending input made
# acceptable ACCEPTS. A "repository" that differs from the real one is a FAKE ROOT directory, handed to
# the verb by setting `root` inside the subshell that runs it (deploy_st_with_root); the verb has no
# other seam for it, and none was added, so the real repository is never written.
#
# UNOBSERVED HERE: see deploy-local-selftest.sh (a real ssh, sudo and install; cmd_build).

# deploy_st_with_root: <root> <command...>. Runs the command with `root` set to another directory. It
# only ever runs inside the subshell of deploy_st_exec, so the assignment cannot leak.
deploy_st_with_root() {
  root="$1"
  shift
  "$@"
}

# deploy_st_tamper: <file> <command...>. Runs the command with FAKE_TAMPER naming a file the fake scp
# appends to when it is called, i.e. a change made to that file behind the verb's back mid-run.
deploy_st_tamper() {
  export FAKE_TAMPER="$1"
  shift
  "$@"
}

# deploy_st_run_root: <root> <deploy_local_main arguments...>. Resets the fakes' records and runs the
# verb as if the repository root were <root>.
deploy_st_run_root() {
  local fake_root="$1"
  shift
  deploy_st_reset
  deploy_st_exec deploy_st_with_root "$fake_root" deploy_local_main "$@"
}

# deploy_st_make_fake_root: builds DEPLOY_ST_FAKEROOT, a minimal repository (an installer tree whose
# release-signing.pub is the throwaway TEST public key, and the remote script) that the verb can run
# against. A key whose public half equals that file is, for that run, "the production key".
deploy_st_make_fake_root() {
  local fake_root="${DEPLOY_ST_WORK}/fakeroot"
  install -d "${fake_root}/installer/keys" "${fake_root}/scripts/lib"
  echo '#!/bin/bash' > "${fake_root}/installer/install.sh"
  cp -- "$DEPLOY_ST_PUB" "${fake_root}/installer/keys/release-signing.pub"
  cp -- "${root}/scripts/lib/deploy-local-remote.sh" "${fake_root}/scripts/lib/"
  DEPLOY_ST_FAKEROOT="$fake_root"
}

# deploy_st_never_logged: <extended regex> <label>. Fails when a fake ssh/scp call line matches.
deploy_st_never_logged() {
  if deploy_st_logged "$1"; then
    deploy_st_fail "$2: a fake call matched /$1/: $(cat "$DEPLOY_ST_LOG")"
  fi
}

# deploy_st_check_log_shape: <label>. Walks EVERY line of the fakes' log (not one grep): each is an ssh
# or scp call, carries StrictHostKeyChecking=yes, has ` -- ` in it, has the destination only AFTER that
# `--`, and the log is not so short that the loop looked at nothing.
deploy_st_check_log_shape() {
  local label="$1" line pre post n=0
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    n=$((n + 1))
    case "$line" in ssh\ * | scp\ *) ;; *) deploy_st_fail "${label}: a logged line is neither ssh nor scp: ${line}" ;; esac
    [[ "$line" == *" -- "* ]] || deploy_st_fail "${label}: no ' -- ' in: ${line}"
    pre="${line%% -- *}"
    post="${line#* -- }"
    [[ "$pre" != *"@dev-host.example"* ]] || deploy_st_fail "${label}: the destination comes before ' -- ': ${line}"
    [[ "$post" == *"@dev-host.example"* ]] || deploy_st_fail "${label}: no destination after ' -- ': ${line}"
    [[ "$line" == *"StrictHostKeyChecking=yes"* ]] || deploy_st_fail "${label}: no StrictHostKeyChecking=yes in: ${line}"
  done < "$DEPLOY_ST_LOG"
  [ "$n" -ge 3 ] || deploy_st_fail "${label}: only ${n} logged call(s) - the loop checked too little to mean anything."
  deploy_st_pass "${label}: all ${n} logged calls have ' -- ' before the destination and StrictHostKeyChecking=yes"
}

# deploy_st_case_stage_path: case 10. A host that answers `mktemp -d` with anything but the exact
# accepted shape is FAILED with nothing copied and nothing removed on the host (an `rm -rf` of a path the
# host chose is the attack); an answer of the exact shape is used, and removed at the end.
deploy_st_case_stage_path() {
  echo
  echo "-- case 10: a stage path of the wrong shape is FAILED, nothing is copied or removed on the host --"
  local answer
  for answer in /home/victim /tmp/maran-deploy-stage.short /tmp/maran-deploy-stage.AbC123xyz01 \
      /tmp/maran-deploy-stage.AbC123xyz0/../../etc /tmp/other-stage.AbC123xyz0 ""; do
    deploy_st_mode "stage=${answer}"
    deploy_st_run "${DEPLOY_ST_ARGS[@]}"
    deploy_st_expect_run "a host answering '${answer}' for mktemp" 1 'FAILED - the host answered an unusable stage directory' nonempty
    deploy_st_logged ' mktemp -d /tmp/maran-deploy-stage\.XXXXXXXXXX' \
      || deploy_st_fail "case 10: mktemp was not asked for in the exact template: $(cat "$DEPLOY_ST_LOG")"
    deploy_st_never_logged 'rm -rf' "case 10: '${answer}' was removed on the host"
    deploy_st_never_logged '^scp ' "case 10: files were copied to '${answer}'"
  done
  deploy_st_pass "six hostile stage answers ended FAILED with no scp and no rm -rf logged"
  deploy_st_mode ok
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "a host answering a stage path of the exact shape" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_logged '^scp .* deploy@dev-host\.example:/tmp/maran-deploy-stage\.AbC123xyz0/$' \
    || deploy_st_fail "case 10: the files were not copied into the accepted stage: $(cat "$DEPLOY_ST_LOG")"
  deploy_st_logged '^ssh .*rm -rf -- /tmp/maran-deploy-stage\.AbC123xyz0$' \
    || deploy_st_fail "case 10: the accepted stage was not removed."
  deploy_st_pass "a stage path of the exact shape was used and removed (inverse control)"
}

# deploy_st_case_verb_keys: case 11, THROUGH deploy_local_main. A signing key under HOME/.maran-release
# (present or absent), a key whose public half equals the repository's (a fake root), the key named by
# MARAN_RELEASE_SIGNING_KEY, an ssh identity under HOME/.maran-release or inside the repository, and an
# encrypted key are each refused with the fakes unused; a key outside all of those succeeds.
deploy_st_case_verb_keys() {
  echo
  echo "-- case 11: the production-key and identity refusals hold through the verb, with the fakes unused --"
  local rel="${DEPLOY_ST_HOME}/.maran-release" dev="${DEPLOY_ST_HOME}/.maran-dev" fake_root
  local base=(dev-host.example --user deploy --bundle "$DEPLOY_ST_BUNDLE" --arch x86_64)
  install -d "$rel" "$dev"
  cp -- "$DEPLOY_ST_KEY" "${rel}/copy.key"
  cp -- "$DEPLOY_ST_KEY" "${dev}/ok.key"
  deploy_st_mode ok
  deploy_st_run "${base[@]}" --key "${rel}/copy.key"
  deploy_st_expect_run "--key under HOME/.maran-release (a copy of the throwaway key)" 1 'lives in the production release key directory' empty
  deploy_st_run "${base[@]}" --key "${rel}/absent.key"
  deploy_st_expect_run "--key under HOME/.maran-release (file absent)" 1 'lives in the production release key directory' empty
  deploy_st_run "${base[@]}" --key "${dev}/ok.key"
  deploy_st_expect_run "--key under HOME/.maran-dev" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_pass "a key under HOME/.maran-release was refused through the verb with the fakes unused; the same key under HOME/.maran-dev succeeded (inverse control)"

  deploy_st_make_fake_root
  fake_root="$DEPLOY_ST_FAKEROOT"
  deploy_st_run_root "$fake_root" "${base[@]}" --key "$DEPLOY_ST_KEY"
  deploy_st_expect_run "a key whose public half equals the repository's installer/keys/release-signing.pub" 1 'public half equals installer/keys/release-signing' empty
  deploy_st_run_root "$fake_root" "${base[@]}" --key "$DEPLOY_ST_OTHER_KEY"
  deploy_st_expect_run "a key whose public half differs from the repository's" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_pass "the repository-public-key comparison refused its own key through the verb and accepted another (the fake root stands in for the repository; the verb has no other seam)"

  DEPLOY_ST_ENVKEY="$DEPLOY_ST_KEY"
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "--key equal to MARAN_RELEASE_SIGNING_KEY" 1 'named by MARAN_RELEASE_SIGNING_KEY' empty
  DEPLOY_ST_ENVKEY=""

  deploy_st_run "${base[@]}" --key "$DEPLOY_ST_KEY" --identity "${rel}/id_ed25519" --dry-run
  deploy_st_expect_run "--identity under HOME/.maran-release" 1 'ssh identity lives in the production release key directory' empty
  deploy_st_run "${base[@]}" --key "$DEPLOY_ST_KEY" --identity "${root}/rules/README.md" --dry-run
  deploy_st_expect_run "--identity inside the repository" 1 'refusing an identity path inside the repository' empty
  cp -- "$DEPLOY_ST_IDENTITY" "${dev}/id_dev"
  deploy_st_run "${base[@]}" --key "$DEPLOY_ST_KEY" --identity "${dev}/id_dev" --dry-run
  deploy_st_expect_run "--identity under HOME/.maran-dev" 0 'dry run, nothing was executed' empty
  deploy_st_pass "an identity under the production directory or inside the repository was refused; one under HOME/.maran-dev passed (inverse control)"

  openssl pkcs8 -topk8 -v2 aes-256-cbc -passout pass:x -in "$DEPLOY_ST_KEY" -out "${DEPLOY_ST_WORK}/keys/enc.key" >/dev/null 2>&1
  head -n 3 "${DEPLOY_ST_WORK}/keys/enc.key" | grep -q 'ENCRYPTED' \
    || deploy_st_fail "case 11: the fixture is not an encrypted key - the case would test nothing."
  DEPLOY_ST_LIMIT=20
  deploy_st_run "${base[@]}" --key "${DEPLOY_ST_WORK}/keys/enc.key"
  DEPLOY_ST_LIMIT=120
  deploy_st_expect_run "an encrypted key (within 20 seconds, never waiting for a passphrase)" 1 'passphrase-protected' empty
  deploy_st_pass "an encrypted key was refused by name without a passphrase prompt; the unencrypted key above succeeded (inverse control)"
}

# deploy_st_case_config_missing: case 12. An explicit --config that is absent is FAILED by name with a
# host given; the same run with no --config (the default file absent) goes on; a dangling symlink at the
# config path is refused as not a regular file rather than skipped.
deploy_st_case_config_missing() {
  echo
  echo "-- case 12: an explicit --config that is absent is FAILED by name; the absent default is not --"
  deploy_st_mode ok
  deploy_st_run dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --config "${DEPLOY_ST_WORK}/absent.conf" --dry-run
  deploy_st_expect_run "an absent explicit --config, host given" 1 'config file not found' empty
  deploy_st_run dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --dry-run
  deploy_st_expect_run "the same run without --config (default file absent)" 0 'dry run, nothing was executed' empty
  ln -sf "${DEPLOY_ST_WORK}/nowhere" "${DEPLOY_ST_WORK}/dangling.conf"
  deploy_st_run dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --config "${DEPLOY_ST_WORK}/dangling.conf" --dry-run
  deploy_st_expect_run "a dangling symlink as --config" 1 'not a regular file' empty
  deploy_st_pass "absent explicit config FAILED, absent default config went on (inverse control), dangling symlink refused"
}

# deploy_st_case_guard_fail_closed: case 13, at the guard function. An empty, garbage or missing
# repository public key, an empty argument, and a file that merely LOOKS like a P-384 key are refused;
# the real key against the real repository public key is accepted.
deploy_st_case_guard_fail_closed() {
  echo
  echo "-- case 13: the key guard fails closed when it cannot compare, and accepts when it can --"
  local repo_pub="${root}/installer/keys/release-signing.pub" dir="${DEPLOY_ST_WORK}/failclosed"
  install -d "$dir"
  : > "${dir}/empty.pub"
  echo "this is not a public key" > "${dir}/garbage.pub"
  printf -- '-----BEGIN EC PRIVATE KEY-----\nsecp384r1 P-384 nistp384\n-----END EC PRIVATE KEY-----\n' > "${dir}/looks-p384.key"
  deploy_st_expect_refused "an EMPTY repository public key" 'could not derive the public half' \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" "${dir}/empty.pub"
  deploy_st_expect_refused "a GARBAGE repository public key" 'could not derive the public half' \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" "${dir}/garbage.pub"
  deploy_st_expect_refused "a repository public key that does not exist" 'could not derive the public half' \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" "${dir}/absent.pub"
  deploy_st_expect_refused "an empty repository public key argument" 'no signing key or repository public key' \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" ""
  deploy_st_expect_refused "a garbage file that looks like a P-384 key" 'not a readable P-384' \
    deploy_refuse_production_key "${dir}/looks-p384.key" "$repo_pub"
  deploy_st_expect_accepted "the throwaway key against the real repository public key" \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" "$repo_pub"
}

# deploy_st_case_symlinked_root: case 14. Containment holds when the repository root is a symlink and
# when the key is spelled through one (deploy_path_within resolves both sides), at the guard and through
# the verb; and a change to the repository public key made mid-run is FAILED by dl_check_repo_pub, even
# when the install also failed. All against the fake root, never the real repository.
deploy_st_case_symlinked_root() {
  echo
  echo "-- case 14: containment survives a symlinked repository root; a mid-run change to the repository key is FAILED --"
  local fake_root link="${DEPLOY_ST_WORK}/rootlink" pub tamper_args
  [ -d "${DEPLOY_ST_FAKEROOT:-}" ] || deploy_st_make_fake_root
  fake_root="$DEPLOY_ST_FAKEROOT"
  pub="${fake_root}/installer/keys/release-signing.pub"
  ln -sfn "$fake_root" "$link"
  cp -- "$DEPLOY_ST_KEY" "${fake_root}/inside.key"
  deploy_st_expect_refused "a key inside the real directory, the root given as a symlink to it" 'must live outside the repository' \
    deploy_st_with_root "$link" deploy_require_outside_repo "${fake_root}/inside.key" key
  deploy_st_expect_refused "a key spelled through a symlink to the root, the root given as the real directory" 'must live outside the repository' \
    deploy_st_with_root "$fake_root" deploy_require_outside_repo "${link}/inside.key" key
  deploy_st_expect_accepted "a key outside the symlinked root" \
    deploy_st_with_root "$link" deploy_require_outside_repo "$DEPLOY_ST_KEY" key
  deploy_st_mode ok
  deploy_st_run_root "$link" dev-host.example --user deploy --bundle "$DEPLOY_ST_BUNDLE" --arch x86_64 --key "${fake_root}/inside.key"
  deploy_st_expect_run "--key inside the repository, the root a symlink, through the verb" 1 'outside the repository' empty
  deploy_st_pass "a symlinked root did not let a key inside the repository through, at the guard or through the verb"

  tamper_args=(dev-host.example --user deploy --bundle "$DEPLOY_ST_BUNDLE" --arch x86_64 --key "$DEPLOY_ST_OTHER_KEY")
  deploy_st_run_root "$fake_root" "${tamper_args[@]}"
  deploy_st_expect_run "a run that leaves the repository public key alone" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_reset
  deploy_st_exec deploy_st_tamper "$pub" deploy_st_with_root "$fake_root" deploy_local_main "${tamper_args[@]}"
  deploy_st_expect_run "a repository public key changed mid-run" 1 'changed during the run' nonempty
  cp -- "$DEPLOY_ST_PUB" "$pub"
  deploy_st_mode install-fails
  deploy_st_reset
  deploy_st_exec deploy_st_tamper "$pub" deploy_st_with_root "$fake_root" deploy_local_main "${tamper_args[@]}"
  deploy_st_expect_run "a changed repository key AND a failing install" 1 'changed during the run' nonempty
  cp -- "$DEPLOY_ST_PUB" "$pub"
  deploy_st_mode ok
  deploy_st_pass "dl_check_repo_pub reported the change (also when the install failed) and the untouched run succeeded (inverse control)"
}

# deploy_st_case_sudo_modes: case 15. `sudo -n true` failing is DID NOT RUN (2) before anything is built
# or copied; --ask-sudo-password never probes and uses a terminal ssh that keeps password authentication
# off and never uses `sudo -n`; --keep-remote logs no `rm -rf`, and without it one is logged.
deploy_st_case_sudo_modes() {
  echo
  echo "-- case 15: sudo that wants a password, --ask-sudo-password and --keep-remote --"
  local line
  deploy_st_mode sudo-password
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "a host whose sudo wants a password" 2 'DID NOT RUN.*sudo needs a password' nonempty
  deploy_st_never_logged '^scp ' "case 15: files were copied although sudo needs a password"
  deploy_st_never_logged ' mktemp ' "case 15: a stage was created although sudo needs a password"
  deploy_st_mode ok
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "the same run with passwordless sudo" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  line="$(grep -E '^ssh .*deploy-local-remote\.sh' "$DEPLOY_ST_LOG")"
  [[ "$line" == *" sudo -n bash "* && "$line" != *" -t "* && "$line" == *"BatchMode=yes"* ]] \
    || deploy_st_fail "case 15: the passwordless install call is not 'sudo -n' under BatchMode without a terminal: ${line}"
  deploy_st_logged 'rm -rf' || deploy_st_fail "case 15: without --keep-remote no rm -rf was logged."
  deploy_st_pass "sudo -n failing gave DID NOT RUN before any copy; with passwordless sudo the run succeeded and removed the stage (inverse control)"

  deploy_st_mode sudo-password
  deploy_st_run "${DEPLOY_ST_ARGS[@]}" --ask-sudo-password
  deploy_st_expect_run "--ask-sudo-password on a host whose sudo wants a password" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_never_logged 'sudo -n' "case 15: --ask-sudo-password still used sudo -n"
  [ "$(grep -cE '^ssh .*deploy-local-remote\.sh' "$DEPLOY_ST_LOG")" -eq 1 ] \
    || deploy_st_fail "case 15: expected exactly one install call: $(cat "$DEPLOY_ST_LOG")"
  line="$(grep -E '^ssh .*deploy-local-remote\.sh' "$DEPLOY_ST_LOG")"
  [[ "$line" == *" -t "* && "$line" == *"PasswordAuthentication=no"* && "$line" == *"KbdInteractiveAuthentication=no"* \
    && "$line" != *"BatchMode=yes"* && "$line" == *" sudo bash "* ]] \
    || deploy_st_fail "case 15: the --ask-sudo-password install call is wrong: ${line}"
  deploy_st_check_log_shape "the --ask-sudo-password run"
  deploy_st_mode ok

  deploy_st_run "${DEPLOY_ST_ARGS[@]}" --keep-remote
  deploy_st_expect_run "--keep-remote" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_never_logged 'rm -rf' "case 15: --keep-remote still removed the stage"
  deploy_st_said 'left /tmp/maran-deploy-stage\.AbC123xyz0 on the host' || deploy_st_fail "case 15: --keep-remote did not say where it left the stage."
  deploy_st_pass "--ask-sudo-password probed nothing and used ssh -t with password authentication off and no sudo -n; --keep-remote logged no rm -rf"
}

# deploy_st_case_arch: case 16. The host's `uname -m` is mapped x86_64 -> x86_64 and aarch64|arm64 ->
# aarch64, read off the artefact that was shipped (which architecture's api archive reached scp); any
# other answer, an escape sequence or an over-long one is FAILED without leaking an ESC byte or more
# than 40 characters to the terminal.
deploy_st_case_arch() {
  echo
  echo "-- case 16: architecture mapping, and a hostile uname answer is FAILED without reaching the terminal --"
  local args=(dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --bundle "$DEPLOY_ST_BUNDLE2")
  local want_x86 want_arm esc long answer
  want_x86="$(sha256_of "${DEPLOY_ST_BUNDLE2}/api-x86_64.tar.gz")"
  want_arm="$(sha256_of "${DEPLOY_ST_BUNDLE2}/api-aarch64.tar.gz")"
  [ "$want_x86" != "$want_arm" ] || deploy_st_fail "case 16: the two architectures' api archives are identical - the fixture cannot tell them apart."
  for answer in x86_64 arm64 aarch64; do
    deploy_st_mode "uname=${answer}"
    deploy_st_run "${args[@]}"
    case "$answer" in
      x86_64) deploy_st_expect_run "uname -m x86_64" 0 'installer finished on deploy@dev-host\.example \(x86_64\)' nonempty ;;
      *) deploy_st_expect_run "uname -m ${answer}" 0 'installer finished on deploy@dev-host\.example \(aarch64\)' nonempty ;;
    esac
    deploy_st_logged ' -- deploy@dev-host\.example uname -m$' || deploy_st_fail "case 16: the host was never asked 'uname -m'."
    mkdir -p "${DEPLOY_ST_WORK}/arch-${answer}"
    tar -xzf "${DEPLOY_ST_CAPTURE}/bundle.tar.gz" -C "${DEPLOY_ST_WORK}/arch-${answer}"
    if [ "$answer" = x86_64 ]; then
      [ "$(sha256_of "${DEPLOY_ST_WORK}/arch-${answer}/api.tar.gz")" = "$want_x86" ] \
        || deploy_st_fail "case 16: ${answer} did not ship the x86_64 api archive."
    else
      [ "$(sha256_of "${DEPLOY_ST_WORK}/arch-${answer}/api.tar.gz")" = "$want_arm" ] \
        || deploy_st_fail "case 16: ${answer} did not ship the aarch64 api archive."
    fi
  done
  deploy_st_pass "x86_64 shipped the x86_64 archive; arm64 and aarch64 both shipped the aarch64 one (the x86_64 answer is the inverse control)"

  esc="$(printf '\033')"
  long="$(printf 'A%.0s' $(seq 1 100))"
  printf 'x%sy' "$esc" | grep -q -- "$esc" || deploy_st_fail "case 16: the ESC detector cannot see an ESC byte - it is blind."
  for answer in riscv64 "${esc}[31mevil${esc}[0m" "$long"; do
    deploy_st_mode "uname=${answer}"
    deploy_st_run "${args[@]}"
    deploy_st_expect_run "uname -m answering a hostile value" 1 'FAILED - unsupported host architecture' nonempty
    if cat "$DEPLOY_ST_OUT" "$DEPLOY_ST_ERR" | grep -q -- "$esc"; then
      deploy_st_fail "case 16: an ESC byte from the host reached the terminal output."
    fi
    if cat "$DEPLOY_ST_OUT" "$DEPLOY_ST_ERR" | grep -qE 'A{41}'; then
      deploy_st_fail "case 16: more than 40 characters of the host's answer reached the terminal."
    fi
    deploy_st_never_logged '^scp ' "case 16: files were copied for an unsupported architecture"
  done
  deploy_st_said 'riscv64' && deploy_st_fail "case 16: the last run echoed an earlier answer - the runs are not independent."
  deploy_st_mode ok
  deploy_st_pass "riscv64, an escape sequence and a 100-character answer were FAILED with no ESC byte, at most 40 characters shown and nothing copied"
}

# deploy_st_case_log_shape: case 17. Every call of three differently shaped runs (host-detected
# architecture, --ask-sudo-password, --keep-remote) has ` -- ` before the destination and
# StrictHostKeyChecking=yes, checked line by line.
deploy_st_case_log_shape() {
  echo
  echo "-- case 17: every logged ssh and scp call has ' -- ' before the destination and StrictHostKeyChecking=yes --"
  local args=(dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --bundle "$DEPLOY_ST_BUNDLE" --port 2222 --identity "$DEPLOY_ST_IDENTITY")
  deploy_st_mode ok
  deploy_st_run "${args[@]}"
  deploy_st_expect_run "a run that detects the architecture" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_check_log_shape "a run with architecture detection, a port and an identity"
  deploy_st_run "${args[@]}" --arch x86_64 --ask-sudo-password
  deploy_st_expect_run "a run with --ask-sudo-password" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_check_log_shape "a run with --ask-sudo-password"
  deploy_st_run "${args[@]}" --arch x86_64 --keep-remote
  deploy_st_expect_run "a run with --keep-remote" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_check_log_shape "a run with --keep-remote"
}

# deploy_st_stage_false: a stage whose first command is a bare `false`, followed by a line that must never print.
deploy_st_stage_false() {
  false
  echo "stage-continued-after-false"
}

# deploy_st_stage_ok: a stage that succeeds and says so.
deploy_st_stage_ok() {
  echo "stage-ran"
}

# deploy_st_stage_two: a stage that reports "nothing was measured" with status 2.
deploy_st_stage_two() {
  return 2
}

# deploy_st_case_workstation_temp: case 18. With TMPDIR a known empty directory, the workstation's
# temporary files are gone after a successful run and after each kind of failure (the scp log proves
# the directory WAS used); and dl_stage turns a bare failing command into a FAILED naming the stage,
# status 2 into DID NOT RUN, and lets a good stage through.
deploy_st_case_workstation_temp() {
  echo
  echo "-- case 18: the workstation temp directory is removed on every exit path; dl_stage names a failing stage --"
  local tmpd="${DEPLOY_ST_WORK}/known-tmp" mode
  install -d "$tmpd"
  DEPLOY_ST_TMPDIR="$tmpd"
  for mode in ok install-fails stage=/home/victim; do
    deploy_st_mode "$mode"
    deploy_st_run "${DEPLOY_ST_ARGS[@]}"
    [ "$DEPLOY_ST_STATUS" -ne 137 ] || deploy_st_fail "case 18: the run in mode ${mode} hung."
    # Proof that the directory was used: only runs that got as far as scp log its path. The hostile
    # stage answer ends before scp, so for that run emptiness is the only thing observed.
    if [ "$mode" != stage=/home/victim ]; then
      deploy_st_logged "^scp .* ${tmpd}/tmp\\.[A-Za-z0-9]+/bundle\\.tar\\.gz " \
        || deploy_st_fail "case 18: the run in mode ${mode} did not use ${tmpd} - the emptiness below proves nothing: $(cat "$DEPLOY_ST_LOG")"
    fi
    [ -z "$(ls -A "$tmpd")" ] || deploy_st_fail "case 18: after the run in mode ${mode}, ${tmpd} still holds: $(ls -A "$tmpd")"
  done
  DEPLOY_ST_TMPDIR=""
  deploy_st_mode ok
  deploy_st_pass "after a success, a failing install and a hostile stage answer the temp directory was empty again (and, for the first two, shown to have been used)"

  deploy_st_reset
  deploy_st_exec dl_stage probe deploy_st_stage_false
  deploy_st_expect_run "a stage that runs a bare false" 1 'FAILED - stage probe failed with status 1' empty
  if deploy_st_said 'stage-continued-after-false'; then
    deploy_st_fail "case 18: errexit was off inside the stage - the command after a failed one still ran."
  fi
  deploy_st_reset
  deploy_st_exec dl_stage probe deploy_st_stage_two
  deploy_st_expect_run "a stage that exits 2" 2 'DID NOT RUN.*stage probe did not run' empty
  deploy_st_reset
  deploy_st_exec dl_stage probe deploy_st_stage_ok
  deploy_st_expect_run "a stage that succeeds" 0 'stage-ran' empty
  deploy_st_pass "dl_stage: a bare false was FAILED naming the stage, status 2 was DID NOT RUN, a good stage passed (inverse control)"
}
