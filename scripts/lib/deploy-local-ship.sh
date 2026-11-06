#!/usr/bin/env bash
# The build, sign, package and ship stages of `maran deploy-local` (issue #410). SOURCED by
# scripts/lib/deploy-local.sh, which owns the verdict helpers (deploy_ok, deploy_failed,
# deploy_did_not_run), the DL_* state and the order the stages run in. It lives beside it because the
# parser and the stages together passed the 300-line target (rules/architecture.md).
#
# Every ssh/scp call is an argv array with `--` before the destination, so no value is parsed as an
# option and no shell string is built on the workstation. Values that reach a command come from the
# validators in deploy-local-guard.sh; the remote stage path is validated before it is ever used.
# Threat note: docs/superpowers/notes/2026-11-06-deploy-local-threat-note.md.

# dl_ssh_arrays: builds the three argv prefixes from the validated port and identity. All three set
# StrictHostKeyChecking=yes: a host key that is not already known is a refusal, never auto-accepted.
# DL_SSH is non-interactive (BatchMode=yes: key authentication only, a password prompt is a failure,
# never a hang). DL_SSH_TTY is the one call that may prompt for the sudo password (--ask-sudo-password):
# it allocates a terminal and drops BatchMode ONLY so that sudo can prompt, and sets
# PasswordAuthentication=no and KbdInteractiveAuthentication=no so the ssh login itself still cannot
# fall back to a password. DL_SCP is DL_SSH with -P for the port.
dl_ssh_arrays() {
  local ident=() common=(-o StrictHostKeyChecking=yes -o ConnectTimeout=15)
  [ -z "$DL_IDENT" ] || ident=(-i "$DL_IDENT" -o IdentitiesOnly=yes)
  DL_SSH=(ssh -o BatchMode=yes "${common[@]}" -p "$DL_PORT" "${ident[@]}")
  DL_SSH_TTY=(ssh -t -o PasswordAuthentication=no -o KbdInteractiveAuthentication=no "${common[@]}" -p "$DL_PORT" "${ident[@]}")
  DL_SCP=(scp -o BatchMode=yes "${common[@]}" -P "$DL_PORT" "${ident[@]}")
}

# dl_show ARGS...: prints one planned command as a line starting with "+ ", shell-quoted so the line is
# exact. Used by the dry run only.
dl_show() {
  printf '+'
  printf ' %q' "$@"
  printf '\n'
}

# dl_dry_run: prints every command a real run would execute, in order, and runs none of them. The host
# and the key PATH appear here and nowhere else; key contents are never read.
dl_dry_run() {
  local target="${DL_USER}@${DL_HOST}" arch="${DL_ARCH:-ARCH_FROM_HOST}" stage="STAGE_FROM_REMOTE_MKTEMP"
  local bundle="TMPDIR/bundle"
  [ -n "$DL_ARCH" ] || echo "arch: <arch detected on the host>"
  [ -n "$DL_ARCH" ] || dl_show "${DL_SSH[@]}" -- "$target" uname -m
  [ "$DL_ASK_SUDO" -eq 1 ] || dl_show "${DL_SSH[@]}" -- "$target" sudo -n true
  if [ -z "$DL_BUNDLE" ]; then
    dl_show cmd_build --arch "$arch" --channel "$DL_CHANNEL" ${DL_VERSION:+--version "$DL_VERSION"} --out "$bundle"
  else
    dl_show cp -a -- "$DL_BUNDLE" "$bundle"
  fi
  dl_show cmd_sign --key "$DL_KEY" --bundle "$bundle"
  dl_show cmd_package --bundle "$bundle" --arch "$arch" --out TMPDIR/bundle.tar.gz --pubkey TMPDIR/test.pub
  dl_show "${DL_SSH[@]}" -- "$target" mktemp -d /tmp/maran-deploy-stage.XXXXXXXXXX
  dl_show "${DL_SCP[@]}" -- TMPDIR/bundle.tar.gz TMPDIR/installer.tar.gz "$root/scripts/lib/deploy-local-remote.sh" "$target:$stage/"
  if [ "$DL_ASK_SUDO" -eq 1 ]; then
    dl_show "${DL_SSH_TTY[@]}" -- "$target" sudo bash "$stage/deploy-local-remote.sh" "$stage"
  else
    dl_show "${DL_SSH[@]}" -- "$target" sudo -n bash "$stage/deploy-local-remote.sh" "$stage"
  fi
  [ "$DL_KEEP" -eq 1 ] || dl_show "${DL_SSH[@]}" -- "$target" rm -rf -- "$stage"
}

# dl_unreachable_if_255 STATUS: ssh answers 255 for its own failure (no route, refused, host key not
# known, authentication refused), as opposed to the remote command's status. That is a fact about the
# connection, not a finding about the code, so it is DID NOT RUN (2) with the manual remedy.
dl_unreachable_if_255() {
  [ "$1" -ne 255 ] || deploy_did_not_run "host unreachable or ssh authentication failed; run ssh ${DL_USER}@${DL_HOST} once by hand to accept the host key"
}

# dl_detect_arch: --arch (or the config) wins; otherwise asks the host with `uname -m` and maps
# x86_64 -> x86_64, aarch64|arm64 -> aarch64. Any other answer is FAILED. This is the first remote
# call of a real run, so a connection failure surfaces here.
dl_detect_arch() {
  [ -z "$DL_ARCH" ] || return 0
  local out st=0
  out="$("${DL_SSH[@]}" -- "${DL_USER}@${DL_HOST}" uname -m)" || st=$?
  dl_unreachable_if_255 "$st"
  [ "$st" -eq 0 ] || deploy_failed "could not read the host architecture (uname -m exited ${st})"
  case "$out" in
    x86_64) DL_ARCH="x86_64" ;;
    aarch64|arm64) DL_ARCH="aarch64" ;;
    *) # Remote output is untrusted: only printable bytes, at most 40, ever reach the terminal.
       deploy_failed "unsupported host architecture: $(printf %s "$out" | tr -cd '[:print:]' | cut -c1-40)" ;;
  esac
}

# dl_probe_sudo: without --ask-sudo-password, proves `sudo -n` works BEFORE anything is built or copied,
# so a host that wants a password costs seconds and leaves nothing behind. DID NOT RUN (2) when it does
# not. With --ask-sudo-password sudo itself prompts on the terminal later, so there is nothing to probe.
dl_probe_sudo() {
  [ "$DL_ASK_SUDO" -eq 0 ] || return 0
  local st=0
  "${DL_SSH[@]}" -- "${DL_USER}@${DL_HOST}" sudo -n true || st=$?
  dl_unreachable_if_255 "$st"
  [ "$st" -eq 0 ] || deploy_did_not_run "sudo needs a password on ${DL_HOST}; configure passwordless sudo for ${DL_USER} on that development host, or pass --ask-sudo-password"
}

# dl_stage NAME CMD ARGS...: runs one release stage (a cmd_* function, which ends the process through
# release_failed and installs its own traps) in a SUBSHELL so this verb's cleanup trap survives. errexit
# is switched OFF around the call, so the status can be read, and switched ON explicitly inside the
# subshell, so a bare failing command in a stage ends it. Status 2 stays DID NOT RUN; anything else
# non-zero is FAILED naming the stage.
dl_stage() {
  local name="$1" st=0
  shift
  set +e
  ( set -e; "$@" )
  st=$?
  set -e
  [ "$st" -ne 2 ] || deploy_did_not_run "stage ${name} did not run (reason printed above)"
  [ "$st" -eq 0 ] || deploy_failed "stage ${name} failed with status ${st} (reason printed above)"
}

# dl_sign_cleared BUNDLE: signs with the validated TEST key. MARAN_RELEASE_SIGNING_KEY is cleared for this one
# call as belt and braces; what actually keeps an exported variable from substituting another key is that
# cmd_sign's --key argument wins over the variable, which the selftest observes (case 4).
dl_sign_cleared() {
  MARAN_RELEASE_SIGNING_KEY="" cmd_sign --key "$DL_KEY" --bundle "$1"
}

# dl_stage_installer: copies the installer tree into the temp dir and replaces ONLY THE COPY's
# release-signing.pub with the TEST public key. Before the copy, a repository keys directory or key file
# that is a symlink is refused; after it, the same for the copy. The copy's file is removed first so a
# symlink in the copy can never be written through to the repository. Then asserts the copy equals the test key and
# differs from the repository key (FAILED otherwise), and tars it. The repository file is never opened
# for writing; dl_check_repo_pub proves that by digest.
dl_stage_installer() {
  local copy="$DL_TMP/installer"
  { [ ! -L "$root/installer/keys" ] && [ ! -L "$root/installer/keys/release-signing.pub" ]; } \
    || deploy_failed "refusing: installer/keys or installer/keys/release-signing.pub is a symlink"
  cp -a "$root/installer" "$copy" || deploy_failed "could not copy the installer tree"
  { [ ! -L "$copy/keys" ] && [ ! -L "$copy/keys/release-signing.pub" ]; } \
    || deploy_failed "refusing: the installer copy's keys path is a symlink"
  rm -f -- "$copy/keys/release-signing.pub"
  cp -- "$DL_TMP/test.pub" "$copy/keys/release-signing.pub" || deploy_failed "could not place the test key in the installer copy"
  cmp -s -- "$DL_TMP/test.pub" "$copy/keys/release-signing.pub" || deploy_failed "installer copy does not carry the test key"
  ! cmp -s -- "$DL_REPO_PUB" "$copy/keys/release-signing.pub" || deploy_failed "installer copy still carries the repository key"
  tar -czf "$DL_TMP/installer.tar.gz" -C "$DL_TMP" installer || deploy_failed "could not pack the installer copy"
}

# dl_build_sign_package: the local half of a real run. Builds (unless --bundle), signs with the TEST key,
# derives its public half, packages and verifies the offline bundle against that public half, then
# prepares the installer copy. With --bundle the directory is COPIED into the private temp dir before
# signing, so a bundle the caller already signed is never re-signed in place. Any stage failure is a
# verdict, never a continue.
dl_build_sign_package() {
  local bundle="$DL_TMP/bundle" args=()
  if [ -n "$DL_BUNDLE" ]; then
    cp -a -- "$DL_BUNDLE" "$bundle" || deploy_failed "could not copy the --bundle directory"
  else
    args=(--arch "$DL_ARCH" --channel "$DL_CHANNEL" --out "$bundle")
    [ -z "$DL_VERSION" ] || args+=(--version "$DL_VERSION")
    dl_stage build cmd_build "${args[@]}"
  fi
  dl_stage sign dl_sign_cleared "$bundle"
  openssl pkey -in "$DL_KEY" -pubout -out "$DL_TMP/test.pub" 2>/dev/null \
    || deploy_failed "could not derive the public half of the test key"
  dl_stage package cmd_package --bundle "$bundle" --arch "$DL_ARCH" --out "$DL_TMP/bundle.tar.gz" --pubkey "$DL_TMP/test.pub"
  dl_stage_installer
}

# dl_ship: creates the stage directory on the host (`mktemp -d /tmp/maran-deploy-stage.XXXXXXXXXX` as the
# login user), refuses any answer that does not match that template, copies the three files, and runs the
# remote script under sudo. The stage is recorded for cleanup only after it validated, so a rejected
# answer is FAILED and nothing is removed on the host: the host chose a path we cannot trust. The run's status is read AFTER dl_check_repo_pub so a
# failed install cannot hide a changed repository key. Non-zero from the installer is FAILED with it.
dl_ship() {
  local target="${DL_USER}@${DL_HOST}" out st=0
  out="$("${DL_SSH[@]}" -- "$target" mktemp -d /tmp/maran-deploy-stage.XXXXXXXXXX)" || st=$?
  dl_unreachable_if_255 "$st"
  [ "$st" -eq 0 ] || deploy_failed "could not create a stage directory on the host (mktemp exited ${st})"
  deploy_valid_stage_path "$out" || deploy_failed "the host answered an unusable stage directory (a path we cannot trust); nothing was removed on the host"
  DL_STAGE="$out"
  "${DL_SCP[@]}" -- "$DL_TMP/bundle.tar.gz" "$DL_TMP/installer.tar.gz" \
    "$root/scripts/lib/deploy-local-remote.sh" "$target:$DL_STAGE/" || deploy_failed "copying the files to the host failed"
  st=0
  if [ "$DL_ASK_SUDO" -eq 1 ]; then
    "${DL_SSH_TTY[@]}" -- "$target" sudo bash "$DL_STAGE/deploy-local-remote.sh" "$DL_STAGE" || st=$?
  else
    "${DL_SSH[@]}" -- "$target" sudo -n bash "$DL_STAGE/deploy-local-remote.sh" "$DL_STAGE" || st=$?
  fi
  dl_check_repo_pub
  [ "$st" -eq 0 ] || deploy_failed "the installer on the host exited ${st}"
}

# dl_check_repo_pub: FAILED when the repository's release-signing.pub differs from the digest taken at
# the start of the run. Nothing here writes it; this is the check that notices if anything ever did.
dl_check_repo_pub() {
  [ "$(sha256_of "$DL_REPO_PUB")" = "$DL_PUB_SHA" ] \
    || deploy_failed "the repository's installer/keys/release-signing.pub changed during the run"
}

# dl_cleanup: the EXIT trap (INT, TERM and HUP reach it through `exit`). Removes the local temp dir and,
# unless --keep-remote, the host's stage directory. Best effort, not a guarantee: SIGKILL or a lost
# connection skips it, and a failure to clean is reported, never turned into a different exit status.
dl_cleanup() {
  if [ -n "$DL_STAGE" ] && [ "$DL_KEEP" -eq 0 ]; then
    "${DL_SSH[@]}" -- "${DL_USER}@${DL_HOST}" rm -rf -- "$DL_STAGE" || echo "deploy-local: could not remove ${DL_STAGE} on the host" >&2
  elif [ -n "$DL_STAGE" ]; then
    echo "deploy-local: left ${DL_STAGE} on the host (--keep-remote)" >&2
  fi
  [ -z "$DL_TMP" ] || rm -rf -- "$DL_TMP" || true
}
