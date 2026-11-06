#!/usr/bin/env bash
# Selftest for `maran deploy-local` (issue #410): `maran deploy-local selftest`.
#
# SOURCED by scripts/lib/deploy-local.sh, after scripts/lib/release-bundle.sh (cmd_sign, cmd_package,
# plant_signed_bundle, sha256_of, release_failed) and scripts/lib/deploy-local-guard.sh. It plants a
# signed bundle with a throwaway P-384 key, puts fake `ssh` and `scp` first on PATH, and drives the
# real deploy_local_main with `--bundle`, so no docker, no host and no network are involved. The fakes
# record every call's argv and copy what scp was given into a capture directory; the checks then read
# those records and the captured files, never the verb's own report of itself.
#
# Every refusal is paired with the inverse control rules/testing.md asks for: the same command with
# the offending input made acceptable ACCEPTS, so a gate mutated to refuse everything cannot pass.
#
# UNOBSERVED HERE: a real ssh, a real sudo and a real install on a host (the remote script is
# checked for its text and the argv it is given, not run as root); and cmd_build, because every case
# passes --bundle, so the build stage is never run and a break in it is invisible to this selftest.
#
# The cases from number 10 on live in deploy-local-selftest-cases.sh, sourced below, so that this file
# stays the harness (fakes, runners, expectations, cases 1 to 9, the driver) and that one the blind spots.
# State lives in DEPLOY_ST_* globals set by deploy_local_selftest and read by the helpers below.

# shellcheck disable=SC1091
. "$root/scripts/lib/deploy-local-selftest-cases.sh"

# deploy_st_fail: <message>. Reports a selftest failure under the deploy verdict prefix and exits 1.
deploy_st_fail() {
  deploy_failed "deploy-local selftest: $*"
}

# deploy_st_pass: <message>. Prints one expected-outcome line.
deploy_st_pass() {
  echo "${DEPLOY_VERDICT}: OK - $* (expected)."
}

# deploy_st_write_fakes: <dir>. Writes the fake ssh and scp. Both append one line of argv to
# $FAKE_LOG; the mode in $FAKE_MODE_FILE selects ok, install-fails (exit 1 for the call that runs the
# remote script), unreachable (exit 255 for every call), sudo-password (`sudo -n true` exits 1),
# uname=<answer> (what `uname -m` prints) or stage=<answer> (what `mktemp -d` prints). By default the
# fake ssh answers `uname -m` with x86_64 and `mktemp -d` with a stage path of the exact accepted shape;
# it answers `mktemp` ONLY for the exact template the verb must ask for, so a changed template fails the
# run instead of being served. The fake scp copies each regular file argument into $FAKE_CAPTURE and,
# when $FAKE_TAMPER names a file, appends to it (a change made behind the verb's back).
deploy_st_write_fakes() {
  local dir="$1"
  install -d "$dir"
  cat > "${dir}/ssh" <<'FAKE'
#!/bin/bash
printf 'ssh' >> "$FAKE_LOG"; printf ' %s' "$@" >> "$FAKE_LOG"; printf '\n' >> "$FAKE_LOG"
mode="$(<"$FAKE_MODE_FILE")"
[ "$mode" = unreachable ] && exit 255
joined=" $* "
case "$joined" in
  *" uname -m "*)
    case "$mode" in uname=*) printf '%s\n' "${mode#uname=}" ;; *) echo x86_64 ;; esac
    exit 0 ;;
  *" mktemp -d /tmp/maran-deploy-stage.XXXXXXXXXX "*)
    case "$mode" in stage=*) printf '%s\n' "${mode#stage=}" ;; *) echo /tmp/maran-deploy-stage.AbC123xyz0 ;; esac
    exit 0 ;;
  *" mktemp "*) echo "fake ssh: unexpected mktemp template" >&2; exit 1 ;;
  *" sudo -n true "*) [ "$mode" = sudo-password ] && exit 1; exit 0 ;;
  *deploy-local-remote.sh*) [ "$mode" = install-fails ] && exit 1 ;;
esac
exit 0
FAKE
  cat > "${dir}/scp" <<'FAKE'
#!/bin/bash
printf 'scp' >> "$FAKE_LOG"; printf ' %s' "$@" >> "$FAKE_LOG"; printf '\n' >> "$FAKE_LOG"
mode="$(<"$FAKE_MODE_FILE")"
[ "$mode" = unreachable ] && exit 255
prev=""
for a in "$@"; do
  # The argument after -i is the identity file: it is not something the verb ships.
  [ "$prev" != "-i" ] && [ -f "$a" ] && cp -- "$a" "$FAKE_CAPTURE/"
  prev="$a"
done
[ -z "${FAKE_TAMPER:-}" ] || echo tampered >> "$FAKE_TAMPER"
exit 0
FAKE
  chmod 0755 "${dir}/ssh" "${dir}/scp"
}

# deploy_st_tool_dir: <dir> <excluded tool>.... Fills <dir> with symlinks to the ordinary tools the
# verb needs, minus the excluded ones, so a PATH can be made that lacks exactly ssh/scp or openssl.
deploy_st_tool_dir() {
  local dir="$1" tool path skip excluded
  shift
  install -d "$dir"
  for tool in bash sh env realpath readlink dirname basename cat grep egrep sed awk gawk tr mkdir rmdir \
      rm cp mv cmp sha256sum sha1sum openssl tar gzip mktemp install id stat head tail sort uniq seq \
      uname chmod date sleep wc cut find ls touch true false tee diff xargs ln whoami hostname expr \
      git printf; do
    skip=0
    for excluded in "$@"; do
      [ "$tool" = "$excluded" ] && skip=1
    done
    [ "$skip" -eq 0 ] || continue
    path="$(command -v "$tool" 2>/dev/null || true)"
    case "$path" in /*) ln -sf "$path" "${dir}/${tool}" ;; esac
  done
}

# deploy_st_reset: truncates the fake ssh/scp log and empties the capture directory, so the next
# run's records are its own.
deploy_st_reset() {
  : > "$DEPLOY_ST_LOG"
  rm -rf -- "${DEPLOY_ST_CAPTURE:?}"
  install -d "$DEPLOY_ST_CAPTURE"
}

# deploy_st_mode: <ok|install-fails|unreachable|sudo-password|uname=X|stage=X>. Sets how the fake ssh
# and scp behave (see deploy_st_write_fakes).
deploy_st_mode() {
  echo "$1" > "$DEPLOY_ST_MODE_FILE"
}

# deploy_st_exec: <command...>. Runs a command in a background subshell with HOME and PATH pointed at
# the selftest's (and TMPDIR at DEPLOY_ST_TMPDIR when that is set), MARAN_RELEASE_SIGNING_KEY set only
# when DEPLOY_ST_ENVKEY is non-empty, errexit on, and stdout/stderr captured to DEPLOY_ST_OUT/
# DEPLOY_ST_ERR. A watchdog kills the subshell after DEPLOY_ST_LIMIT seconds (status 137), so a command
# that waits on a terminal fails the case instead of hanging the selftest. The status lands in
# DEPLOY_ST_STATUS and this function itself always succeeds: a function called in a `||` list loses
# errexit even inside its own subshell, which would make the code under test behave differently from
# the real run.
deploy_st_exec() {
  local pid wd
  set +e
  (
    set -e
    export HOME="$DEPLOY_ST_HOME" PATH="$DEPLOY_ST_PATH"
    export FAKE_LOG="$DEPLOY_ST_LOG" FAKE_MODE_FILE="$DEPLOY_ST_MODE_FILE" FAKE_CAPTURE="$DEPLOY_ST_CAPTURE"
    [ -z "${DEPLOY_ST_TMPDIR:-}" ] || export TMPDIR="$DEPLOY_ST_TMPDIR"
    hash -r
    if [ -n "$DEPLOY_ST_ENVKEY" ]; then
      export MARAN_RELEASE_SIGNING_KEY="$DEPLOY_ST_ENVKEY"
    else
      unset MARAN_RELEASE_SIGNING_KEY
    fi
    "$@"
  ) >"$DEPLOY_ST_OUT" 2>"$DEPLOY_ST_ERR" &
  pid=$!
  (
    trap 'kill "$nap" 2>/dev/null; exit 0' TERM
    sleep "$DEPLOY_ST_LIMIT" & nap=$!
    wait "$nap"
    kill -KILL "$pid" 2>/dev/null
  ) &
  wd=$!
  wait "$pid"
  DEPLOY_ST_STATUS=$?
  kill -TERM "$wd" 2>/dev/null
  wait "$wd" 2>/dev/null
  set -e
}

# deploy_st_run: <deploy_local_main arguments...>. Resets the fakes' records and runs the verb.
deploy_st_run() {
  deploy_st_reset
  deploy_st_exec deploy_local_main "$@"
}

# deploy_st_said: <extended regex>. Succeeds when the last run printed a match on stdout or stderr.
deploy_st_said() {
  cat "$DEPLOY_ST_OUT" "$DEPLOY_ST_ERR" | grep -qE -- "$1"
}

# deploy_st_logged: <extended regex>. Succeeds when a fake ssh/scp call line matches.
deploy_st_logged() {
  grep -qE -- "$1" "$DEPLOY_ST_LOG"
}

# deploy_st_expect_refused: <label> <pattern> <command...>. Runs the command (a guard function) and
# requires a non-zero status whose message matches the pattern, so a refusal for the wrong reason,
# such as a typo in the fixture, cannot pass for the one under test.
deploy_st_expect_refused() {
  local label="$1" pattern="$2"
  shift 2
  deploy_st_exec "$@"
  [ "$DEPLOY_ST_STATUS" -ne 0 ] || deploy_st_fail "${label} was ACCEPTED - the check behind it is decoration."
  deploy_st_said "$pattern" \
    || deploy_st_fail "${label} was refused for another reason than expected (wanted /${pattern}/): $(cat "$DEPLOY_ST_ERR")"
  deploy_st_pass "${label} REFUSED"
}

# deploy_st_expect_accepted: <label> <command...>. The inverse control: the command must succeed.
deploy_st_expect_accepted() {
  local label="$1"
  shift
  deploy_st_exec "$@"
  [ "$DEPLOY_ST_STATUS" -eq 0 ] \
    || deploy_st_fail "${label} was refused (inverse control): $(cat "$DEPLOY_ST_ERR")"
  deploy_st_pass "${label} accepted (inverse control)"
}

# deploy_st_expect_run: <label> <exit status> <pattern> <logged expectation: empty|nonempty>. Checks
# the last deploy_st_run: its exit status, a verdict or message pattern on stdout/stderr, and whether
# the fake ssh/scp were called at all.
deploy_st_expect_run() {
  local label="$1" want="$2" pattern="$3" logged="$4" size
  [ "$DEPLOY_ST_STATUS" -eq "$want" ] \
    || deploy_st_fail "${label}: exit ${DEPLOY_ST_STATUS}, wanted ${want}: $(cat "$DEPLOY_ST_OUT" "$DEPLOY_ST_ERR")"
  deploy_st_said "$pattern" \
    || deploy_st_fail "${label}: no /${pattern}/ in: $(cat "$DEPLOY_ST_OUT" "$DEPLOY_ST_ERR")"
  size="$(wc -c < "$DEPLOY_ST_LOG")"
  if [ "$logged" = empty ] && [ "$size" -ne 0 ]; then
    deploy_st_fail "${label}: the fakes were called but must not have been: $(cat "$DEPLOY_ST_LOG")"
  fi
  if [ "$logged" = nonempty ] && [ "$size" -eq 0 ]; then
    deploy_st_fail "${label}: the fakes were never called - the run did not reach the host."
  fi
}

# deploy_st_case_dry_run: case 1. A dry run prints the commands and runs nothing; the same
# arguments without --dry-run reach the fakes.
deploy_st_case_dry_run() {
  echo "-- case 1: --dry-run prints the commands and calls nothing --"
  deploy_st_mode ok
  deploy_st_run "${DEPLOY_ST_ARGS[@]}" --dry-run
  deploy_st_expect_run "dry run" 0 'DEPLOY-LOCAL VERDICT: OK.*dry run, nothing was executed' empty
  deploy_st_said 'BatchMode=yes' || deploy_st_fail "dry run: the ssh line has no BatchMode=yes."
  deploy_st_said 'sudo -n bash' || deploy_st_fail "dry run: the remote command has no 'sudo -n bash'."
  deploy_st_said 'dev-host\.example' || deploy_st_fail "dry run: the host is not shown."
  if cat "$DEPLOY_ST_OUT" "$DEPLOY_ST_ERR" | grep -qE 'BEGIN [A-Z ]*KEY'; then
    deploy_st_fail "dry run: a PEM marker reached the output - key contents were printed."
  fi
  deploy_st_pass "dry run printed ssh -o BatchMode=yes ... sudo -n bash, the host, no key contents, and called nothing"
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "the same arguments without --dry-run" 0 'VERDICT: OK' nonempty
  deploy_st_pass "without --dry-run the fakes were called (inverse control)"
  deploy_st_run dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --dry-run
  deploy_st_said 'arch detected on the host' \
    || deploy_st_fail "dry run without --arch did not print the <arch detected on the host> placeholder."
  deploy_st_pass "a dry run without --arch asks no host and prints the arch placeholder"
}

# deploy_st_case_full_run: case 2. The shipped files, the test key in the installer copy, the
# untouched repository key, the bundle checked with openssl directly, the install and cleanup calls.
deploy_st_case_full_run() {
  echo
  echo "-- case 2: a full run against the fakes ships a bundle and installer signed and keyed with the TEST key --"
  local repo_pub="${root}/installer/keys/release-signing.pub" before after cap="${DEPLOY_ST_CAPTURE}" ex f
  before="$(sha256_of "$repo_pub")"
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "full run" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_said 'TEST key' || deploy_st_fail "full run: the test-key warning is missing."
  deploy_st_said 'UNOBSERVED HERE' || deploy_st_fail "full run: the verb printed no blind-spot line."
  for f in bundle.tar.gz installer.tar.gz deploy-local-remote.sh; do
    [ -f "${cap}/${f}" ] || deploy_st_fail "full run: scp was never given ${f} (captured: $(ls "$cap"))."
  done
  cmp -s "${cap}/deploy-local-remote.sh" "${root}/scripts/lib/deploy-local-remote.sh" \
    || deploy_st_fail "full run: the shipped remote script is not scripts/lib/deploy-local-remote.sh."
  ex="${DEPLOY_ST_WORK}/captured-installer"
  rm -rf -- "$ex"; install -d "$ex"
  tar -xzf "${cap}/installer.tar.gz" -C "$ex"
  cmp -s "${ex}/installer/keys/release-signing.pub" "$DEPLOY_ST_PUB" \
    || deploy_st_fail "full run: the shipped installer does not carry the throwaway public key."
  if cmp -s "${ex}/installer/keys/release-signing.pub" "$repo_pub"; then
    deploy_st_fail "full run: the shipped installer still carries the repository's production public key."
  fi
  after="$(sha256_of "$repo_pub")"
  [ "$before" = "$after" ] || deploy_st_fail "full run: installer/keys/release-signing.pub in the repository CHANGED."
  # openssl called directly, not the packager's verifier, against the throwaway public key.
  ex="${DEPLOY_ST_WORK}/captured-bundle"
  rm -rf -- "$ex"; install -d "$ex"
  tar -xzf "${cap}/bundle.tar.gz" -C "$ex"
  for f in manifest integrity-manifest; do
    openssl dgst -sha384 -verify "$DEPLOY_ST_PUB" -signature "${ex}/${f}.json.sig" "${ex}/${f}.json" >/dev/null 2>&1 \
      || deploy_st_fail "full run: openssl does not verify the shipped ${f}.json against the throwaway key."
    if openssl dgst -sha384 -verify "$DEPLOY_ST_OTHER_PUB" -signature "${ex}/${f}.json.sig" "${ex}/${f}.json" >/dev/null 2>&1; then
      deploy_st_fail "full run: the shipped ${f}.json verifies against an unrelated key - the verification above sees nothing."
    fi
  done
  deploy_st_logged '^ssh .*sudo -n bash /tmp/maran-deploy-stage\.AbC123xyz0/deploy-local-remote\.sh /tmp/maran-deploy-stage\.AbC123xyz0$' \
    || deploy_st_fail "full run: no 'sudo -n bash <stage>/deploy-local-remote.sh <stage>' call: $(cat "$DEPLOY_ST_LOG")"
  deploy_st_logged '^ssh .*rm -rf -- /tmp/maran-deploy-stage\.AbC123xyz0$' \
    || deploy_st_fail "full run: the stage directory was never removed."
  deploy_st_logged '^ssh .*-- deploy@dev-host\.example' \
    || deploy_st_fail "full run: an ssh call has no '--' before the destination."
  if cat "$DEPLOY_ST_LOG" "$DEPLOY_ST_OUT" "$DEPLOY_ST_ERR" | grep -q 'PRIVATE KEY'; then
    deploy_st_fail "full run: private key text reached a log or the output."
  fi
  deploy_st_pass "scp got the bundle, installer copy and remote script; the copy carries the TEST key and the repository key is byte-identical before and after; openssl verifies the bundle against the test key only; install and cleanup were logged"
}

# deploy_st_case_production_key: case 3, at the guard function. A key whose public half equals the
# stand-in "repository key" is refused; the same key against the real repository key is accepted.
deploy_st_case_production_key() {
  echo
  echo "-- case 3: a key whose public half is the repository key is refused as the production release key --"
  local repo_pub="${root}/installer/keys/release-signing.pub"
  deploy_st_expect_refused "the key whose public half stands in as the repository key" 'public half equals installer/keys/release-signing\.pub' \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" "$DEPLOY_ST_PUB"
  deploy_st_expect_accepted "the same key against the real repository public key" \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" "$repo_pub"
  openssl ecparam -name prime256v1 -genkey -noout -out "${DEPLOY_ST_WORK}/p256.key" >/dev/null 2>&1
  deploy_st_expect_refused "a P-256 key" 'not a readable P-384' \
    deploy_refuse_production_key "${DEPLOY_ST_WORK}/p256.key" "$repo_pub"
  deploy_st_expect_refused "a key file that does not exist" 'not a readable regular file' \
    deploy_refuse_production_key "${DEPLOY_ST_WORK}/absent.key" "$repo_pub"
}

# deploy_st_case_key_paths: case 4. The path rules decide before openssl opens anything (a spy on
# PATH records every openssl call), and a MARAN_RELEASE_SIGNING_KEY exported to a different valid key
# does not change which key signed: the package verifies under the --key public key and not the other.
deploy_st_case_key_paths() {
  echo
  echo "-- case 4: keys under the production directory or the repository are refused by path, unopened --"
  local repo_pub="${root}/installer/keys/release-signing.pub" spy="${DEPLOY_ST_WORK}/spy" real_openssl size
  real_openssl="$(command -v openssl)"
  install -d "$spy" "${DEPLOY_ST_HOME}/.maran-release" "${DEPLOY_ST_HOME}/.maran-dev"
  printf '#!/bin/bash\necho called >> "%s/spy.log"\nexec "%s" "$@"\n' "$spy" "$real_openssl" > "${spy}/openssl"
  chmod 0755 "${spy}/openssl"
  cp -- "$DEPLOY_ST_KEY" "${DEPLOY_ST_HOME}/.maran-release/x.key"
  cp -- "$DEPLOY_ST_KEY" "${DEPLOY_ST_HOME}/.maran-dev/x.key"
  ln -sf "${DEPLOY_ST_HOME}/.maran-release/x.key" "${DEPLOY_ST_WORK}/sneaky.key"
  ln -sf "${DEPLOY_ST_HOME}/.maran-dev/x.key" "${DEPLOY_ST_WORK}/honest.key"
  local saved_path="$DEPLOY_ST_PATH"
  DEPLOY_ST_PATH="${spy}:${saved_path}"
  : > "${spy}/spy.log"
  deploy_st_expect_refused "a key under HOME/.maran-release" 'lives in the production release key directory' \
    deploy_refuse_production_key "${DEPLOY_ST_HOME}/.maran-release/x.key" "$repo_pub"
  deploy_st_expect_refused "a symlink that resolves into HOME/.maran-release" 'lives in the production release key directory' \
    deploy_refuse_production_key "${DEPLOY_ST_WORK}/sneaky.key" "$repo_pub"
  deploy_st_expect_refused "a key path inside the repository" 'must live outside the repository' \
    deploy_refuse_production_key "${root}/not-a-real-test.key" "$repo_pub"
  size="$(wc -c < "${spy}/spy.log")"
  [ "$size" -eq 0 ] || deploy_st_fail "case 4: openssl ran (${size} bytes of spy log) while the path refusals were decided - the key was opened."
  deploy_st_pass "openssl was never called while the three path refusals were decided"
  deploy_st_expect_accepted "a key under HOME/.maran-dev" deploy_refuse_production_key "${DEPLOY_ST_HOME}/.maran-dev/x.key" "$repo_pub"
  deploy_st_expect_accepted "a symlink that resolves into HOME/.maran-dev" deploy_refuse_production_key "${DEPLOY_ST_WORK}/honest.key" "$repo_pub"
  [ -s "${spy}/spy.log" ] || deploy_st_fail "case 4: the spy saw no openssl call for the accepted key - it cannot observe the refusals' silence."
  deploy_st_pass "the spy DID see openssl run for the accepted keys, so its silence above is a measurement"
  DEPLOY_ST_PATH="$saved_path"

  DEPLOY_ST_ENVKEY="$DEPLOY_ST_KEY"
  deploy_st_expect_refused "the key named by MARAN_RELEASE_SIGNING_KEY" 'named by MARAN_RELEASE_SIGNING_KEY' \
    deploy_refuse_production_key "$DEPLOY_ST_KEY" "$repo_pub"
  DEPLOY_ST_ENVKEY="$DEPLOY_ST_OTHER_KEY"
  deploy_st_mode ok
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "run with an unrelated MARAN_RELEASE_SIGNING_KEY exported" 0 'VERDICT: OK' nonempty
  mkdir -p "${DEPLOY_ST_WORK}/env-bundle"
  tar -xzf "${DEPLOY_ST_CAPTURE}/bundle.tar.gz" -C "${DEPLOY_ST_WORK}/env-bundle"
  openssl dgst -sha384 -verify "$DEPLOY_ST_PUB" -signature "${DEPLOY_ST_WORK}/env-bundle/manifest.json.sig" \
    "${DEPLOY_ST_WORK}/env-bundle/manifest.json" >/dev/null 2>&1 \
    || deploy_st_fail "case 4: with the environment key exported the bundle is not signed by --key."
  if openssl dgst -sha384 -verify "$DEPLOY_ST_OTHER_PUB" -signature "${DEPLOY_ST_WORK}/env-bundle/manifest.json.sig" \
      "${DEPLOY_ST_WORK}/env-bundle/manifest.json" >/dev/null 2>&1; then
    deploy_st_fail "case 4: the bundle verifies under the exported environment key - it signed the package."
  fi
  DEPLOY_ST_ENVKEY=""
  deploy_st_pass "with another valid key exported the package verifies under the --key public key and not the exported one"
}

# deploy_st_case_config: case 5. The config reader's refusals, each against a valid file; and that
# an argument beats the config on the command line.
deploy_st_case_config() {
  echo
  echo "-- case 5: the config reader is strict, and an argument beats the file --"
  local cfg="${DEPLOY_ST_WORK}/good.conf"
  printf '# comment\nhost=cfg-host.example\nuser=cfguser\nport=2200\nkey=%s\nidentity=%s\narch=x86_64\n\n' \
    "$DEPLOY_ST_KEY" "$DEPLOY_ST_IDENTITY" > "$cfg"
  chmod 0600 "$cfg"
  deploy_st_expect_accepted "a valid config file" deploy_read_config "$cfg"
  grep -q '^host=cfg-host.example$' "$DEPLOY_ST_OUT" || deploy_st_fail "case 5: the valid config's host was not printed back."
  printf 'host=cfg-host.example\npassword=x\n' > "${DEPLOY_ST_WORK}/pw.conf"; chmod 0600 "${DEPLOY_ST_WORK}/pw.conf"
  deploy_st_expect_refused "a config holding password=" 'this file never holds a password' deploy_read_config "${DEPLOY_ST_WORK}/pw.conf"
  printf 'host=cfg-host.example\ncolour=red\n' > "${DEPLOY_ST_WORK}/unk.conf"; chmod 0600 "${DEPLOY_ST_WORK}/unk.conf"
  deploy_st_expect_refused "a config with an unknown key" "unknown config key 'colour'" deploy_read_config "${DEPLOY_ST_WORK}/unk.conf"
  cp -- "$cfg" "${DEPLOY_ST_WORK}/open.conf"; chmod 0664 "${DEPLOY_ST_WORK}/open.conf"
  deploy_st_expect_refused "a group-writable config" 'writable by group or others' deploy_read_config "${DEPLOY_ST_WORK}/open.conf"
  deploy_st_expect_refused "a config path inside the repository" 'must live outside the repository' \
    deploy_require_outside_repo "${root}/rules/README.md" config
  deploy_st_expect_accepted "a config path outside the repository" deploy_require_outside_repo "$cfg" config
  deploy_st_run --config "${root}/rules/README.md" --dry-run
  deploy_st_expect_run "--config pointing into the repository" 1 'outside the repository' empty
  deploy_st_run dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --config "${DEPLOY_ST_WORK}/does-not-exist.conf" --dry-run
  deploy_st_expect_run "an explicit --config that does not exist, with a host given" 1 'config file not found' empty
  deploy_st_run --config "$cfg" --dry-run
  deploy_st_expect_run "config only" 0 'dry run, nothing was executed' empty
  deploy_st_said '-p 2200' || deploy_st_fail "case 5: the config's port 2200 is not in the ssh line."
  deploy_st_said 'cfguser@cfg-host\.example' || deploy_st_fail "case 5: the config's user and host are not in the ssh line."
  deploy_st_run --config "$cfg" --port 2222 --dry-run
  deploy_st_expect_run "config plus --port" 0 'dry run, nothing was executed' empty
  deploy_st_said '-p 2222' || deploy_st_fail "case 5: --port 2222 did not reach the ssh line."
  if deploy_st_said '-p 2200'; then deploy_st_fail "case 5: the config's port beat the argument."; fi
  deploy_st_pass "the argument (2222) beat the config (2200); without the argument the config's value was used (inverse control)"
}

# deploy_st_case_injection: case 6. A host that looks like an ssh option is refused before any call;
# an ordinary host is not.
deploy_st_case_injection() {
  echo
  echo "-- case 6: a host or user that looks like an ssh option is refused before ssh is called --"
  deploy_st_run --host '-oProxyCommand=x' --user deploy --key "$DEPLOY_ST_KEY" --bundle "$DEPLOY_ST_BUNDLE" --arch x86_64
  deploy_st_expect_run "--host -oProxyCommand=x" 1 'not an acceptable host name' empty
  deploy_st_run 'deploy@-oProxyCommand=x' --key "$DEPLOY_ST_KEY" --bundle "$DEPLOY_ST_BUNDLE" --arch x86_64
  deploy_st_expect_run "user@-oProxyCommand=x" 1 'not an acceptable host name' empty
  deploy_st_run dev-host.example --user '-oFoo' --key "$DEPLOY_ST_KEY" --bundle "$DEPLOY_ST_BUNDLE" --arch x86_64
  deploy_st_expect_run "--user -oFoo" 1 'not an acceptable user name' empty
  deploy_st_run dev-host.example --user deploy --port 70000 --key "$DEPLOY_ST_KEY" --bundle "$DEPLOY_ST_BUNDLE" --arch x86_64
  deploy_st_expect_run "--port 70000" 1 'not a port between 1 and 65535' empty
  deploy_st_run dev-host.example --user deploy --port 2222 --key "$DEPLOY_ST_KEY" --dry-run
  deploy_st_expect_run "dev-host.example, deploy, 2222" 0 'dry run, nothing was executed' empty
  deploy_st_pass "an ordinary host, user and port are accepted (inverse control)"
}

# deploy_st_case_failures: cases 7 to 9. A failing install is FAILED with cleanup still logged; a
# PATH without ssh or openssl, and an unreachable host, are DID NOT RUN with status 2.
deploy_st_case_failures() {
  echo
  echo "-- case 7: a failing remote install is FAILED, status 1, and the stage is still removed --"
  deploy_st_mode install-fails
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "a failing install" 1 'DEPLOY-LOCAL VERDICT: FAILED' nonempty
  deploy_st_logged '^ssh .*rm -rf -- /tmp/maran-deploy-stage\.AbC123xyz0$' || deploy_st_fail "case 7: the stage was not removed after the failure."
  deploy_st_pass "the failed install gave FAILED, status 1, and the cleanup call was still logged"
  deploy_st_mode ok
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "the same run with the install succeeding" 0 'DEPLOY-LOCAL VERDICT: OK' nonempty
  deploy_st_pass "with the install succeeding the verdict is OK (inverse control)"

  echo
  echo "-- case 8: a machine without ssh, or without openssl, answers DID NOT RUN, status 2 --"
  local no_ssh="${DEPLOY_ST_WORK}/tools-no-ssh" no_ssl="${DEPLOY_ST_WORK}/tools-no-openssl" saved_path="$DEPLOY_ST_PATH"
  deploy_st_tool_dir "$no_ssh" ssh scp
  deploy_st_tool_dir "$no_ssl" openssl
  DEPLOY_ST_PATH="$no_ssh"
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "a PATH without ssh" 2 'DID NOT RUN' empty
  DEPLOY_ST_PATH="${DEPLOY_ST_FAKES}:${no_ssh}"
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "the same PATH with the fake ssh and scp added" 0 'VERDICT: OK' nonempty
  deploy_st_pass "with ssh present the same arguments proceed and succeed (inverse control)"
  DEPLOY_ST_PATH="${DEPLOY_ST_FAKES}:${no_ssl}"
  deploy_st_run "${DEPLOY_ST_ARGS[@]}"
  deploy_st_expect_run "a PATH without openssl" 2 'DID NOT RUN' empty
  DEPLOY_ST_PATH="$saved_path"

  echo
  echo "-- case 9: an unreachable host (ssh exits 255) answers DID NOT RUN, status 2 --"
  deploy_st_mode unreachable
  deploy_st_run dev-host.example --user deploy --key "$DEPLOY_ST_KEY" --bundle "$DEPLOY_ST_BUNDLE"
  deploy_st_expect_run "an unreachable host" 2 'DID NOT RUN' nonempty
  deploy_st_mode ok
  deploy_st_pass "exit 255 from the first ssh call gave DID NOT RUN (the inverse control is case 2: the same host answering)"
}

# deploy_local_selftest: builds the fixtures (throwaway keys, a planted signed bundle, fake ssh/scp, a
# temporary HOME), runs the cases (1 to 9 here, 10 to 18 in deploy-local-selftest-cases.sh), and prints the verdict. The temporary directory is removed on
# every exit path. Any failed assertion ends the run through deploy_failed (status 1); a machine
# without openssl or tar is DID NOT RUN (status 2).
deploy_local_selftest() {
  local tool
  for tool in openssl tar; do
    command -v "$tool" >/dev/null 2>&1 || {
      deploy_did_not_run "${tool} is not on PATH (the selftest makes its keys and bundles with it). source scripts/dev first, and see: maran check"
    }
  done
  DEPLOY_ST_WORK="$(mktemp -d)"
  trap 'rm -rf -- "$DEPLOY_ST_WORK"' RETURN EXIT
  DEPLOY_ST_HOME="${DEPLOY_ST_WORK}/home"
  DEPLOY_ST_FAKES="${DEPLOY_ST_WORK}/fakes"
  DEPLOY_ST_LOG="${DEPLOY_ST_WORK}/fake.log"
  DEPLOY_ST_MODE_FILE="${DEPLOY_ST_WORK}/fake.mode"
  DEPLOY_ST_CAPTURE="${DEPLOY_ST_WORK}/capture"
  DEPLOY_ST_OUT="${DEPLOY_ST_WORK}/run.out"
  DEPLOY_ST_ERR="${DEPLOY_ST_WORK}/run.err"
  DEPLOY_ST_PATH="${DEPLOY_ST_FAKES}:${PATH}"
  DEPLOY_ST_ENVKEY=""
  DEPLOY_ST_STATUS=0
  install -d -m 0700 "$DEPLOY_ST_HOME" "${DEPLOY_ST_WORK}/keys"
  deploy_st_write_fakes "$DEPLOY_ST_FAKES"

  DEPLOY_ST_KEY="${DEPLOY_ST_WORK}/keys/test.key"
  DEPLOY_ST_PUB="${DEPLOY_ST_WORK}/keys/test.pub"
  DEPLOY_ST_OTHER_KEY="${DEPLOY_ST_WORK}/keys/other.key"
  DEPLOY_ST_OTHER_PUB="${DEPLOY_ST_WORK}/keys/other.pub"
  for tool in test other; do
    openssl ecparam -name secp384r1 -genkey -noout -out "${DEPLOY_ST_WORK}/keys/${tool}.key" >/dev/null 2>&1
    openssl pkey -in "${DEPLOY_ST_WORK}/keys/${tool}.key" -pubout -out "${DEPLOY_ST_WORK}/keys/${tool}.pub" >/dev/null 2>&1
  done
  DEPLOY_ST_IDENTITY="${DEPLOY_ST_WORK}/keys/id_dev"
  echo "not a real identity - selftest placeholder" > "$DEPLOY_ST_IDENTITY"
  chmod 0600 "$DEPLOY_ST_IDENTITY"
  DEPLOY_ST_BUNDLE="${DEPLOY_ST_WORK}/bundle"
  plant_signed_bundle "$DEPLOY_ST_BUNDLE" "$DEPLOY_ST_KEY" "x86_64"
  DEPLOY_ST_BUNDLE2="${DEPLOY_ST_WORK}/bundle2"
  plant_signed_bundle "$DEPLOY_ST_BUNDLE2" "$DEPLOY_ST_KEY" "x86_64 aarch64"
  DEPLOY_ST_LIMIT=120
  DEPLOY_ST_TMPDIR=""
  deploy_st_mode ok
  DEPLOY_ST_ARGS=(dev-host.example --user deploy --port 2222 --identity "$DEPLOY_ST_IDENTITY"
    --key "$DEPLOY_ST_KEY" --arch x86_64 --bundle "$DEPLOY_ST_BUNDLE")

  deploy_st_case_dry_run
  deploy_st_case_full_run
  deploy_st_case_production_key
  deploy_st_case_key_paths
  deploy_st_case_config
  deploy_st_case_injection
  deploy_st_case_failures
  deploy_st_case_stage_path
  deploy_st_case_verb_keys
  deploy_st_case_config_missing
  deploy_st_case_guard_fail_closed
  deploy_st_case_symlinked_root
  deploy_st_case_sudo_modes
  deploy_st_case_arch
  deploy_st_case_log_shape
  deploy_st_case_workstation_temp

  echo
  echo "UNOBSERVED HERE: a real ssh, a real sudo and a real install on a host; the remote script is checked for its text and the argv it is given, not run as root. cmd_build never ran: every case passes --bundle."
  deploy_ok "deploy-local selftest: a dry run executed nothing; a full run against fake ssh/scp shipped a bundle" \
    "and installer copy carrying only the test key, left the repository key untouched and cleaned the stage;" \
    "the production key, keys under the production directory or the repository, option-shaped hosts, a" \
    "password or unknown config key and an in-repository config were all refused with an inverse control each;" \
    "a failing install was FAILED, and a missing ssh or openssl and an unreachable host were DID NOT RUN;" \
    "a hostile stage path, a sudo that wants a password, an unsupported architecture, a changed repository key," \
    "an encrypted or production key and the leftovers on the workstation were each checked against a control."
}
