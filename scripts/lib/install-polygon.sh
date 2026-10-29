#!/usr/bin/env bash
# install-polygon.sh: run the DOCUMENTED install path end to end against a real systemd, on a
# container that stands in for a fresh server, and return a verdict about what actually came up.
#
# Why this exists, when `maran polygon` already scores polygon runs.
#
# The three older polygon images answer questions about the installer's SHELL. Two of them run no
# init at all and therefore had to replace `systemctl` with docker/polygon/stand-ins/systemctl-stand-in.sh,
# whose own header says it "starts or stops NOTHING"; the third boots real systemd but only ever
# starts stock nginx. So every defect living in the gap between "the installer's shell ran" and
# "systemd actually started the unit" was invisible to CI, and four of them were found by hand on a
# real server instead: a framework-dependent publish (#39), MemoryDenyWriteExecute against a JIT
# (#40), a mandatory ReadWritePaths= target that did not exist yet (#41), and a step that started
# the root daemon without ever checking it (#42). A fake systemctl can fail none of those, because
# the thing under test in all four IS systemd.
#
# This harness closes that gap: real systemd as PID 1, a fresh image with nothing Maran needs
# pre-installed, the real install path, and then a verdict built from what systemd and the panel
# say about themselves rather than from the installer's own exit code alone — the installer exited
# 0 on the very install where the root daemon was dead (#42).
#
# What it cannot settle is stated in the Dockerfiles and in docker/README.md: the kernel is this
# machine's, so quota, SELinux, firewall and enablement-at-boot remain claims only a real host
# settles. It settles the part that was missing — units load, namespaces build, binaries execute,
# sockets bind, the panel answers.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Every version in installer/lib/10-preflight.sh's supported matrix has a folder under
# docker/polygon/images/, and the folder's base.txt is the digest under test. Both families are
# exercised because the family-specific packaging splits are exactly where this project has been
# bitten: `passwd` separate from `shadow-utils` on RHEL made every suspension fail there.
os="ubuntu24"
mode="local"
keep="no"
bundle=""
publish=""
browser="no"
arch="x86_64"

usage() {
  cat >&2 <<'USAGE'
usage: maran installer [--os <version>] [--mode local|get|offline] [--bundle PATH] [--arch <a>]
                       [--publish PORT] [--browser] [--keep]

  --os <version>  any folder under docker/polygon/images (default: ubuntu24). An unknown value
                  prints the list rather than guessing at a family.
  --mode local    install from THIS working tree, over the real released artifacts (default).
                  Use while fixing: it needs no release and so cannot tempt a release per fix.
  --mode get      install with the documented one-liner through get.maran.innovayse.com, which
                  tests the PUBLISHED installer. Only meaningful after a release.
  --mode offline  install from a LOCAL signed bundle given with --bundle, so artifacts built in
                  this tree can be proved on a real systemd BEFORE anything is published. This is
                  the mode that keeps a fix from needing a release to be tested.
  --bundle PATH   the bundle for --mode offline, as produced by:
                    maran release build && maran release sign && maran release package --out PATH
  --arch <a>      x86_64 (default) or aarch64. aarch64 runs the image under qemu on an x86_64 host,
                  which proves the packaging and the units — not the performance. It needs the
                  binfmt handlers registered:
                    docker run --privileged --rm tonistiigi/binfmt --install arm64
  --publish PORT  map the panel's 8443 onto PORT on this machine, so a real BROWSER can open it.
                  curl can reach the panel without this; a browser is the only thing that can find
                  a TLS defect curl is happy with (issue #67).
  --browser       after the install checks pass, open /setup in a real Chromium (Playwright,
                  frontend/playwright.install.config.ts), create the first administrator through
                  the UI, sign in through /login and require the dashboard. Implies --publish
                  (18443 unless --publish names a port). Needs frontend/node_modules and a
                  Playwright chromium on THIS machine; their absence fails the verdict.
  --keep          leave the container running afterwards for inspection.

The verdict is built from systemd and the panel, never from the installer's exit code alone:
an install that exits 0 with a dead root daemon has happened (issue #42).
USAGE
  exit 1
}

while [ $# -gt 0 ]; do
  case "$1" in
    --os)   os="${2:?--os needs a value}"; shift 2 ;;
    --mode) mode="${2:?--mode needs a value}"; shift 2 ;;
    --bundle) bundle="${2:?--bundle needs a path}"; shift 2 ;;
    --arch) arch="${2:?--arch needs a value}"; shift 2 ;;
    --publish) publish="${2:?--publish needs a port}"; shift 2 ;;
    --browser) browser="yes"; shift ;;
    --keep) keep="yes"; shift ;;
    -h|--help) usage ;;
    *) echo "install-polygon.sh: unknown argument '$1'" >&2; usage ;;
  esac
done

# The family decides the template and the web server's group; the version decides only the base
# image. Keeping that split here is what lets a new version be one folder and one line.
case "$os" in
  ubuntu22|ubuntu24|ubuntu26|debian12|debian13) family="debian"; web_group="www-data" ;;
  alma8|alma9|alma10|rocky8|rocky9|oracle8|oracle9|oracle10) family="rhel"; web_group="nginx" ;;
  *)
    echo "install-polygon.sh: unknown --os '$os'. Known versions are the folders in docker/polygon/images:" >&2
    ls -d "${root}/docker/polygon/images"/*/ 2>/dev/null | sed 's#.*/images/##; s#/$##' | sed 's/^/  /' >&2
    exit 1
    ;;
esac
dockerfile="install-${family}.Dockerfile"

# The browser stage reaches the panel from THIS machine, so it needs the port published. Implied
# rather than demanded: a --browser run with nothing published could only ever fail on reachability.
if [ "$browser" = "yes" ] && [ -z "$publish" ]; then
  publish="18443"
fi

# The architecture under test. It changes the docker platform and nothing else: the same per-version
# base.txt serves both, because every pinned digest is a manifest LIST covering both architectures.
case "$arch" in
  x86_64)  platform="linux/amd64" ;;
  aarch64) platform="linux/arm64" ;;
  *) echo "install-polygon.sh: unknown --arch '$arch' (expected x86_64 or aarch64)" >&2; exit 1 ;;
esac

# The base image is read from the version's own folder rather than spelled in this script, so the
# pinned digest lives beside the other facts about that version and a new version needs no edit here.
base_file="${root}/docker/polygon/images/${os}/base.txt"
[ -r "$base_file" ] \
  || { echo "install-polygon.sh: no pinned base image at ${base_file#"$root"/}" >&2; exit 1; }
base_image="$(tr -d '[:space:]' < "$base_file")"
[ -n "$base_image" ] \
  || { echo "install-polygon.sh: ${base_file#"$root"/} is empty, so this run would test an unpinned image (rules/testing.md)" >&2; exit 1; }
case "$mode" in
  local|get) ;;
  offline)
    # Refused rather than defaulted: a bundle this harness built silently would be a bundle nobody
    # chose, and the whole point of this mode is proving a SPECIFIC set of artifacts.
    [ -n "$bundle" ] \
      || { echo "install-polygon.sh: --mode offline needs --bundle <path>; produce one with: maran release build && maran release sign && maran release package --out <path>" >&2; exit 1; }
    [ -f "$bundle" ] || { echo "install-polygon.sh: no bundle at '$bundle'" >&2; exit 1; }
    ;;
  *) echo "install-polygon.sh: unknown --mode '$mode'" >&2; exit 1 ;;
esac

# The image and container carry the architecture in their names, so an aarch64 run can never reuse
# an x86_64 image that happens to be cached under the same tag — which would silently test the wrong
# machine and report a pass.
image="maran-install-polygon:${os}-${arch}"
container="maran-install-polygon-${os}-${arch}"
log_dir="${root}/.polygon-logs"
mkdir -p "$log_dir"
log="${log_dir}/install-${os}-${arch}-${mode}.log"

# A FRESH container per run, unconditionally. Re-running into a half-installed container tests the
# resume/repair path, which is a different question wearing the same name: the defects this harness
# is for are the ones a first install on a clean host hits.
echo "==> polygon: ${os} ${arch} (${family} family, ${base_image%%@*}), mode ${mode}"
docker rm -f "$container" >/dev/null 2>&1 || true
docker build -q --platform "$platform" --build-arg "BASE_IMAGE=${base_image}" \
  -f "${root}/docker/polygon/images/${dockerfile}" -t "$image" "${root}/docker/polygon" >/dev/null

# --privileged plus the host's cgroup tree is what lets systemd be systemd here: it manages real
# cgroups, builds real mount namespaces, and applies the unit hardening the panel and agent units
# declare. Without it every directive this harness exists to exercise is silently inert.
# The published port is optional and off by default: a mapped port is a port this machine now
# listens on, and most runs have no browser to point at it.
publish_args=()
[ -n "$publish" ] && publish_args=(-p "127.0.0.1:${publish}:8443")

docker run -d --name "$container" --platform "$platform" --privileged --cgroupns=host \
  "${publish_args[@]}" \
  -v /sys/fs/cgroup:/sys/fs/cgroup:rw "$image" >/dev/null

# Wait for the boot to settle before installing into it. `degraded` is accepted and `running` is
# not required: a masked unit or a container-only failure can degrade the target without bearing on
# anything Maran does, and the per-unit checks below are what actually decide the verdict.
booted="no"
for _ in $(seq 1 45); do
  state="$(docker exec "$container" systemctl is-system-running 2>&1 | tr -d '\r' || true)"
  case "$state" in running|degraded) booted="yes"; break ;; esac
  sleep 2
done
if [ "$booted" != "yes" ]; then
  echo "install-polygon.sh: systemd never finished booting in the container (last state: ${state:-none})." >&2
  echo "Without a real init this harness proves nothing, so it refuses rather than reporting on a stand-in." >&2
  docker logs "$container" 2>&1 | tail -30 >&2
  exit 1
fi
echo "    systemd: ${state}"

# The install itself. `local` copies THIS tree, so a fix can be tested without cutting a release;
# `get` runs the documented one-liner verbatim, so what it tests is what a reader would actually get.
installer_status=0
if [ "$mode" = "local" ]; then
  docker exec "$container" rm -rf /root/installer
  docker cp "${root}/installer" "${container}:/root/installer" >/dev/null
  docker exec "$container" bash -c 'cd /root/installer && bash install.sh --channel beta' \
    >"$log" 2>&1 || installer_status=$?
elif [ "$mode" = "offline" ]; then
  # The installer's own --offline-tarball path, given a bundle built from this tree. It verifies the
  # bundle's signature exactly as an online install verifies the manifest's, so this mode tests the
  # artifacts without weakening anything about how they are trusted.
  docker exec "$container" rm -rf /root/installer
  docker cp "${root}/installer" "${container}:/root/installer" >/dev/null
  docker cp "$bundle" "${container}:/root/maran-bundle.tar.gz" >/dev/null
  docker exec "$container" bash -c \
    'cd /root/installer && bash install.sh --offline-tarball /root/maran-bundle.tar.gz' \
    >"$log" 2>&1 || installer_status=$?
else
  # The documented command verbatim — `| sudo bash` — because that is what a reader runs.
  #
  # `sudo` can be unusable INSIDE the container for a reason that has nothing to do with Maran: the
  # HOST's AppArmor profile for `unix-chkpwd` denies it dac_read_search and dac_override, so PAM
  # cannot read the shadow file and sudo refuses with "Authentication service cannot retrieve
  # authentication info". Measured on an Ubuntu host against the EL8 image; the EL8 image is fine on
  # a real server, which runs no such profile.
  #
  # That is reported and then worked around, never hidden: the container's shell is already root, so
  # running the same pipeline without sudo exercises everything about Maran that the documented
  # command would — the published get.sh, its checksum guard, the signature, the install — while the
  # line below makes it plain that the sudo half was not what got tested here.
  if docker exec "$container" sudo -n true >/dev/null 2>&1; then
    docker exec "$container" bash -c \
      'curl -sSL https://get.maran.innovayse.com | sudo bash' >"$log" 2>&1 || installer_status=$?
  else
    echo "    note: sudo is unusable in this container (the host's AppArmor denies unix_chkpwd), so the"
    echo "          documented pipeline ran without it. Everything after the pipe is unchanged."
    docker exec "$container" bash -c \
      'curl -sSL https://get.maran.innovayse.com | bash' >"$log" 2>&1 || installer_status=$?
  fi
fi
echo "    installer exit: ${installer_status} (log: ${log#"$root"/})"

# The verdict. Every line below asks systemd or the panel, never the installer: the installer's own
# exit code was 0 on the install whose root daemon was dead.
failures=0
note() { echo "    FAIL: $*" >&2; failures=$((failures + 1)); }

[ "$installer_status" -eq 0 ] || note "the installer exited ${installer_status}; its last lines are in ${log#"$root"/}"

for unit in maran-agent maran-api; do
  active="$(docker exec "$container" systemctl is-active "$unit" 2>&1 | tr -d '\r' || true)"
  [ "$active" = "active" ] || note "${unit} is '${active}', not active"
done

# Socket ownership is the boundary, not a detail: a wrong group on the panel's socket is 502 on
# every API call, and a wrong group on the agent's is a panel with no privileged operations at all.
check_socket() {
  local path="$1" expected="$2" observed
  observed="$(docker exec "$container" stat -c '%a %U %G' "$path" 2>&1 | tr -d '\r' || true)"
  [ "$observed" = "$expected" ] || note "${path} is '${observed}', must be '${expected}'"
}
check_socket /run/maran/agent.sock "660 root maran"
check_socket /run/maran-api/api.sock "660 maran ${web_group}"

# The agent's view of the HOST's shared temporary directories (issue #139). The unit keeps
# PrivateTmp=true, so its own /tmp is a private one; account deletion sweeps the host's /tmp through
# the bind mount BindPaths= puts at /run/maran-host/tmp. Nothing short of looking from inside the
# agent's mount namespace can say whether that bind survived PrivateTmp (and, for dev-shm,
# PrivateDevices), so this plants a marker in the HOST's /tmp and joins the namespace of the running
# agent (nsenter -t <pid> -m) to require it visible at the bind. The marker is removed afterwards.
agent_pid="$(docker exec "$container" systemctl show -p MainPID --value maran-agent 2>&1 | tr -d '\r' || true)"
host_marker="maran-host-bind-probe-$$"
case "$agent_pid" in
  ''|0|*[!0-9]*) note "the agent has no main pid ('${agent_pid}'), so its mount namespace cannot be joined to look at /run/maran-host" ;;
  *)
    docker exec "$container" touch "/tmp/${host_marker}" "/var/tmp/${host_marker}" "/dev/shm/${host_marker}" \
      >/dev/null 2>&1 || note "could not plant the probe markers in the host's /tmp, /var/tmp and /dev/shm"
    for pair in tmp:/tmp var-tmp:/var/tmp dev-shm:/dev/shm; do
      bind="/run/maran-host/${pair%%:*}"
      # The positive control: the same marker must NOT be in the agent's own private directory.
      # PrivateTmp is what makes the bind necessary, and a probe that found the marker there would
      # be looking at the host's directory through a PrivateTmp that is not private.
      if [ "${pair#*:}" != /dev/shm ] \
        && docker exec "$container" nsenter -t "$agent_pid" -m test -e "${pair#*:}/${host_marker}" 2>/dev/null; then
        note "the agent sees the host's marker at its own ${pair#*:}, so PrivateTmp=true is not isolating it and the probe at ${bind} would prove nothing"
      fi
      if ! docker exec "$container" nsenter -t "$agent_pid" -m test -e "${bind}/${host_marker}" 2>/dev/null; then
        note "the host's ${pair#*:}/${host_marker} is NOT visible at ${bind} from inside the agent's mount namespace, so account deletion cannot sweep the host's ${pair#*:}"
      fi
    done
    docker exec "$container" rm -f "/tmp/${host_marker}" "/var/tmp/${host_marker}" "/dev/shm/${host_marker}" >/dev/null 2>&1 || true
    echo "    host temp binds: markers planted in the host's /tmp, /var/tmp and /dev/shm, looked for at /run/maran-host/* from agent pid ${agent_pid}"
    echo "    UNOBSERVED HERE: the sweep itself (a real delete in this unit's sandbox); /var/tmp may share /tmp's filesystem on this image."
    ;;
esac

# The panel's own account of itself, through nginx on the port the installer published. Health is
# read for CONTENT and not for a 200: the endpoint answered 200 with "agent":"unavailable" on the
# install that had no root daemon, so the string is the check.
health="$(docker exec "$container" curl -sk --max-time 20 https://127.0.0.1:8443/health 2>&1 | tr -d '\r' || true)"
echo "    health: ${health}"
case "$health" in
  *'"agent":"connected"'*) ;;
  *) note "health does not report a connected agent: ${health}" ;;
esac
case "$health" in
  *'"database":"reachable"'*) ;;
  *) note "health does not report a reachable database: ${health}" ;;
esac

setup_code="$(docker exec "$container" curl -sk -o /dev/null -w '%{http_code}' --max-time 20 \
  https://127.0.0.1:8443/setup 2>&1 | tr -d '\r' || true)"
echo "    /setup: ${setup_code}"
[ "$setup_code" = "200" ] || note "/setup answered ${setup_code}, not 200"

# Units that failed for reasons of their own. Reported rather than scored: on this family a
# container can fail a unit the installer never touched, so the operator reads the list.
failed_units="$(docker exec "$container" systemctl --failed --no-legend --no-pager 2>&1 | tr -d '\r' || true)"
if [ -n "${failed_units//[[:space:]]/}" ]; then
  echo "    failed units present:"
  echo "$failed_units" | sed 's/^/      /'
fi

# ---------------------------------------------------------------------------------------------
# The break-glass `maran` command (issue #130), against a real systemd and a real panel.
#
# The suite-level assertions (docker/polygon/asserts/parts/cli-recovery.sh) show the wrapper's
# modes, its refusals and its fixed argv; none of them can show that a locked-out administrator
# actually gets back in, because that needs the panel, PostgreSQL and the service account running.
# These two stages are that proof, and they run only on an install that already passed every check
# above: a recovery stage scored on a half-installed panel would report on the install.
# ---------------------------------------------------------------------------------------------
recovery_checks=0
recovery_log="${log_dir}/recovery-${os}-${arch}-${mode}.log"
: > "$recovery_log"

# panel_post: the HTTP status of one JSON POST to the panel through nginx, or 000. Bodies carry
# throwaway credentials made up by this harness; the status is all that is read back.
panel_post() {
  docker exec "$container" curl -sk -o /dev/null -w '%{http_code}' --max-time 20 \
    -X POST -H 'Content-Type: application/json' --data "$2" "https://127.0.0.1:8443$1" 2>/dev/null \
    | tr -d '\r' || true
}

# wait_for_panel: 0 once /health reports a reachable database, 1 after ~90 seconds.
wait_for_panel() {
  local answer
  for _ in $(seq 1 45); do
    answer="$(docker exec "$container" curl -sk --max-time 5 https://127.0.0.1:8443/health 2>/dev/null \
      | tr -d '\r' || true)"
    case "$answer" in *'"database":"reachable"'*) return 0 ;; esac
    sleep 2
  done
  return 1
}

# restart_panel_api: a restart of maran-api, which empties the in-memory per-address login rate
# limiter (5 attempts per 300 s). The ACCOUNT lockout lives in the database and survives it, and
# that separation is the point: the limiter would otherwise answer 429 long before the account's
# own failed-attempt counter reached its limit, and the stage would be scoring the limiter.
restart_panel_api() {
  docker exec "$container" systemctl restart maran-api >/dev/null 2>&1 || return 1
  wait_for_panel
}

# recovery_expect: one scored check. $1 = what it asserts, $2 = observed, $3 = expected.
recovery_expect() {
  recovery_checks=$((recovery_checks + 1))
  [ "$2" = "$3" ] || note "recovery: $1: got '$2', expected '$3'"
}

# stage_lockout_recovery: first-admin setup, a locked account, the recovery command, and a link
# that works exactly once.
stage_lockout_recovery() {
  local admin_user="polygonadmin" admin_email="polygon.admin@example.test"
  local old_password="Polygon-Old-Password-1" new_password="Polygon-New-Password-2"
  local setup_token code batch attempt non_401=0 out status link token journal

  # When the browser stage ran, it already created this exact administrator through the UI and
  # spent the one-time token; posting it again would be refused and scored as a recovery failure.
  # The sign-in below with the old password is the positive control either way.
  if [ "$browser_admin_created" = "yes" ]; then
    echo "    lockout-recovery: the first administrator was created by the browser stage; API setup skipped"
  else
  setup_token="$(docker exec "$container" sed -n 's/^Setup__Token=//p' /etc/maran/panel.env 2>/dev/null | tr -d '\r' || true)"
  # The vacuity guard: an empty token makes the setup refuse and every later line report on a
  # panel that has no administrator, which would read as five unrelated failures.
  if [ "${#setup_token}" -lt 8 ]; then
    note "recovery: /etc/maran/panel.env carries no Setup__Token of at least 8 characters, so the first administrator cannot be created and nothing below was observed"
    return 0
  fi

  code="$(panel_post /api/v1/setup "{\"token\":\"${setup_token}\",\"username\":\"${admin_user}\",\"email\":\"${admin_email}\",\"password\":\"${old_password}\"}")"
  recovery_expect "completing first-admin setup" "$code" "200"
  [ "$code" = "200" ] || return 0
  fi

  # The positive control for everything about locking: this exact credential is ACCEPTED now, so a
  # refusal of it later is attributable to the lock and not to a wrong password.
  code="$(panel_post /api/v1/auth/login "{\"username\":\"${admin_user}\",\"password\":\"${old_password}\"}")"
  recovery_expect "signing in with the new administrator's password before any failure" "$code" "200"
  [ "$code" = "200" ] || return 0

  # Twelve wrong passwords, in batches of four with a restart between them: under the limiter's 5
  # per address and over the account's default limit of 10 failed attempts. Every attempt must be a
  # plain 401; a 429 means the limiter, not the account, answered, and the lock was never reached.
  for batch in 1 2 3; do
    restart_panel_api || { note "recovery: the panel did not come back after a restart (batch ${batch})"; return 0; }
    for attempt in 1 2 3 4; do
      code="$(panel_post /api/v1/auth/login "{\"username\":\"${admin_user}\",\"password\":\"Polygon-Wrong-Password-${batch}${attempt}\"}")"
      [ "$code" = "401" ] || non_401=$((non_401 + 1))
    done
  done
  recovery_expect "twelve wrong passwords all answered 401 (none rate-limited)" "$non_401" "0"

  restart_panel_api || { note "recovery: the panel did not come back after the last restart"; return 0; }
  code="$(panel_post /api/v1/auth/login "{\"username\":\"${admin_user}\",\"password\":\"${old_password}\"}")"
  recovery_expect "the CORRECT password is refused after exceeding the failed-login limit (locked)" "$code" "401"

  # The recovery command. First its refusal to print a credential into a non-terminal: docker exec
  # without -t is exactly that, so this is the unflagged invocation and it must print no link.
  status=0
  out="$(docker exec "$container" /usr/local/sbin/maran admin-reset-password "$admin_user" 2>>"$recovery_log" | tr -d '\r')" || status=$?
  recovery_expect "admin-reset-password without --allow-non-terminal on a non-terminal exits non-zero" "$([ "$status" -ne 0 ] && echo refused || echo accepted)" "refused"
  case "$out" in
    *'reset-password?token='*) note "recovery: a reset link was printed to a non-terminal without --allow-non-terminal" ;;
  esac

  status=0
  out="$(docker exec "$container" /usr/local/sbin/maran admin-reset-password "$admin_user" --allow-non-terminal 2>>"$recovery_log" | tr -d '\r')" || status=$?
  recovery_expect "admin-reset-password --allow-non-terminal exits 0" "$status" "0"
  link="$(printf '%s\n' "$out" | grep -E 'reset-password\?token=' || true)"
  recovery_expect "exactly one reset link on standard output" "$(printf '%s\n' "$link" | grep -c . || true)" "1"
  token="${link##*token=}"
  # Percent-decoded: the CLI escapes the token as a URL component and the endpoint wants it raw.
  token="$(printf '%b' "$(printf '%s' "$token" | sed 's/%\([0-9A-Fa-f][0-9A-Fa-f]\)/\\x\1/g')")"
  if [ "${#token}" -lt 20 ]; then
    note "recovery: no usable token could be read from the printed link, so the redemption below was not observed"
    return 0
  fi

  code="$(panel_post /api/v1/auth/reset-password "{\"token\":\"${token}\",\"newPassword\":\"${new_password}\"}")"
  recovery_expect "redeeming the link with a new password" "$code" "200"

  code="$(panel_post /api/v1/auth/login "{\"username\":\"${admin_user}\",\"password\":\"${new_password}\"}")"
  recovery_expect "signing in with the new password (the lock is cleared)" "$code" "200"
  code="$(panel_post /api/v1/auth/login "{\"username\":\"${admin_user}\",\"password\":\"${old_password}\"}")"
  recovery_expect "the old password no longer works" "$code" "401"

  # Single use. The same request that returned 200 above, replayed, must now be refused; the first
  # redemption is this check's inverse control (the token was acceptable until it was spent).
  code="$(panel_post /api/v1/auth/reset-password "{\"token\":\"${token}\",\"newPassword\":\"Polygon-Replay-Password-3\"}")"
  recovery_expect "replaying the spent link" "$code" "400"
  code="$(panel_post /api/v1/auth/login "{\"username\":\"${admin_user}\",\"password\":\"Polygon-Replay-Password-3\"}")"
  recovery_expect "the replayed link did not change the password" "$code" "401"

  # The authpriv line exists (positive control) and carries no token (the point).
  docker exec "$container" journalctl --sync >/dev/null 2>&1 || true
  journal="$(docker exec "$container" journalctl -t maran-cli --no-pager -o cat 2>/dev/null | tr -d '\r' || true)"
  case "$journal" in
    *'verb=admin-reset-password outcome=issued'*) recovery_checks=$((recovery_checks + 1)) ;;
    *) note "recovery: the journal has no 'verb=admin-reset-password outcome=issued' line from maran-cli, so the no-token check below saw nothing: ${journal}" ;;
  esac
  case "$journal" in
    *"$token"*) note "recovery: the reset token appears in the maran-cli journal lines" ;;
    *) recovery_checks=$((recovery_checks + 1)) ;;
  esac
  echo "    lockout-recovery: locked after 12 failures, link minted by 'maran admin-reset-password', redeemed once, sign-in restored, replay refused (${recovery_checks} checks so far)"
  echo "    UNOBSERVED HERE: the root caller's identity is whatever 'docker exec' reports (root, no SUDO_USER), so the claimed-operator name in the audit row is not exercised; and the limiter was emptied by restarting maran-api between batches, so what is proved is the ACCOUNT lock, not the per-address limit."
}

# stage_diagnostics: a bundle collected from the running host, with canaries planted where the
# redactor must find them, extracted and searched.
stage_diagnostics() {
  local setup_token b64 pem_body pem canary_name="Polygon__CanarySecret" canary_value
  local env_backup="/root/panel.env.polygon-orig" canary_log="/var/log/maran/polygon-canary.log"
  local out status bundle extract control_dir hits kept needle

  setup_token="$(docker exec "$container" sed -n 's/^Setup__Token=//p' /etc/maran/panel.env 2>/dev/null | tr -d '\r' || true)"
  b64="$(head -c 36 /dev/urandom | base64 -w0)"
  pem_body="$(head -c 48 /dev/urandom | base64 -w0)"
  pem="$(printf -- '-----BEGIN PRIVATE KEY-----\n%s\n-----END PRIVATE KEY-----' "$pem_body")"
  canary_value="$(head -c 18 /dev/urandom | base64 | tr '+/=' 'xyz')"
  if [ "${#setup_token}" -lt 8 ]; then
    note "diagnostics: panel.env carries no known secret value (Setup__Token) to plant, so redaction of a KNOWN value was not observed"
    return 0
  fi

  # 1. The canaries: a known configuration value, a PEM block and a base64 secret in a top-level
  # product log the bundle collects. The sentinel word proves the file was collected (not blank).
  printf 'polygon-canary-line: handler started\nunexpected value %s in handler\n%s\nrequest failed with payload %s\n' \
    "$setup_token" "$pem" "$b64" | docker exec -i "$container" tee "$canary_log" >/dev/null
  docker exec "$container" cp -p /etc/maran/panel.env "$env_backup"

  # The positive control, on the SOURCE: before redaction every canary is in the log the bundle
  # reads. Without this, "zero hits in the bundle" could mean the canaries never existed.
  for needle in "$setup_token" "$pem_body" "$b64" '-----BEGIN PRIVATE KEY-----'; do
    docker exec "$container" grep -qF -- "$needle" "$canary_log" \
      || note "diagnostics: a canary is not in ${canary_log} before collection, so its absence from the bundle proves nothing"
  done
  recovery_checks=$((recovery_checks + 1))

  # 2. Collect, extract, search.
  status=0
  out="$(docker exec "$container" /usr/local/sbin/maran collect-diagnostics --out /root 2>>"$recovery_log" | tr -d '\r')" || status=$?
  recovery_expect "collect-diagnostics with planted canaries exits 0" "$status" "0"
  bundle="$(printf '%s\n' "$out" | sed -n 's/^Diagnostics bundle: //p')"
  if [ "$status" -eq 0 ] && [ -n "$bundle" ]; then
    recovery_expect "the bundle is 0600" "$(docker exec "$container" stat -c '%a %U' "$bundle" | tr -d '\r')" "600 root"
    extract="/root/polygon-diag-extract"
    control_dir="/root/polygon-diag-control"
    docker exec "$container" rm -rf "$extract" "$control_dir"
    docker exec "$container" mkdir -p "$extract" "$control_dir"
    docker exec "$container" tar -xzf "$bundle" -C "$extract"

    # Vacuity guards on the collection axis: the planted log is in the bundle, with its sentinel,
    # beside the other allow-listed texts, and the env NAMES file names the secret but not its value.
    recovery_expect "the bundle contains the planted log with its sentinel" \
      "$(docker exec "$container" grep -c 'polygon-canary-line' "${extract}/log-polygon-canary.log" 2>/dev/null | tr -d '\r' || true)" "1"
    recovery_expect "the bundle holds at least 8 collected texts" \
      "$([ "$(docker exec "$container" find "$extract" -type f | wc -l)" -ge 8 ] && echo yes || echo no)" "yes"
    recovery_expect "the env names file lists Setup__Token" \
      "$(docker exec "$container" grep -c '^Setup__Token$' "${extract}/panel-env-names.txt" 2>/dev/null | tr -d '\r' || true)" "1"

    # The search, with its own positive control: the same grep over a directory holding the raw
    # canaries must find all four, or its zero over the bundle is the probe being blind.
    printf '%s\n%s\n%s\n' "$setup_token" "$pem" "$b64" | docker exec -i "$container" tee "${control_dir}/raw.txt" >/dev/null
    for needle in "$setup_token" "$pem_body" "$b64" '-----BEGIN'; do
      docker exec "$container" grep -rqF -- "$needle" "$control_dir" \
        || note "diagnostics: the extraction search does not find a planted canary in its own control directory, so its zero over the bundle is meaningless"
    done
    recovery_checks=$((recovery_checks + 1))
    hits=0
    for needle in "$setup_token" "$pem_body" "$b64" '-----BEGIN'; do
      docker exec "$container" grep -rqF -- "$needle" "$extract" && hits=$((hits + 1))
    done
    recovery_expect "canaries found in the extracted bundle (known value, PEM body, base64 secret, PEM marker)" "$hits" "0"
    docker exec "$container" rm -rf "$extract" "$control_dir" "$bundle"
  else
    note "diagnostics: no bundle path in the command's output: ${out}"
  fi

  # 3. Fail closed. A value in panel.env that the redactor has no way to know, planted bare in the
  # log: the extract-and-scan barrier must see it by NAME-matched value and refuse, leaving no
  # archive. This is the scanner's inverse control: the first run above shows it ACCEPTS a clean
  # bundle, this one shows it REFUSES a dirty one.
  printf '\n%s=%s\n' "$canary_name" "$canary_value" | docker exec -i "$container" tee -a /etc/maran/panel.env >/dev/null
  printf 'unknown secret in a message: %s\n' "$canary_value" | docker exec -i "$container" tee -a "$canary_log" >/dev/null
  status=0
  out="$(docker exec "$container" /usr/local/sbin/maran collect-diagnostics --out /root 2>&1 | tr -d '\r')" || status=$?
  recovery_expect "a bundle that still holds an unredacted configuration value is refused (non-zero exit)" "$([ "$status" -ne 0 ] && echo refused || echo accepted)" "refused"
  case "$out" in
    *"${canary_name} survived redaction"*) recovery_checks=$((recovery_checks + 1)) ;;
    *) note "diagnostics: the refusal did not name ${canary_name}: ${out}" ;;
  esac
  case "$out" in
    *"$canary_value"*) note "diagnostics: the refusal message printed the secret's VALUE" ;;
    *) recovery_checks=$((recovery_checks + 1)) ;;
  esac
  kept="$(docker exec "$container" bash -c 'ls /root/maran-diagnostics-*.tar.gz 2>/dev/null | wc -l' | tr -d '\r' || true)"
  recovery_expect "no archive is left behind after a refused bundle" "$kept" "0"

  # 4. Put the host back, and prove it.
  docker exec "$container" bash -c "cat '$env_backup' > /etc/maran/panel.env && rm -f '$env_backup' '$canary_log'"
  recovery_expect "panel.env no longer holds the planted canary setting" \
    "$(docker exec "$container" grep -c "^${canary_name}=" /etc/maran/panel.env 2>/dev/null | tr -d '\r' || true)" "0"
  docker exec "$container" bash -c 'rm -rf /var/lib/maran-cli-staging/*' || true
  echo "    diagnostics: canaries planted (known value, PEM block, base64 secret), bundle extracted and searched, fail-closed refusal shown (${recovery_checks} checks so far)"
  echo "    UNOBSERVED HERE: secrets with no known literal and no matching pattern. Redaction is literal- and pattern-based; a free-text password in a log line, or a token in a format nobody listed, passes it. The second-look scan compares only the values of settings whose NAMES say they are secrets."
  echo "    UNOBSERVED HERE: whether the known value was removed by the live-configuration LITERAL stage or by a pattern (a long base64-shaped token matches both); the zero hits do not attribute the removal."
  echo "    UNOBSERVED HERE: customer-identifying data (domain names, IP addresses) is not removed by design, and a real operator's journal is far larger than this fresh install's."
}

# ---------------------------------------------------------------------------------------------
# The real-browser stage (issue #68), opt-in with --browser.
#
# Every check above talks to the panel with `curl -k`, and two defects shipped straight past it: an
# Ed25519 self-signed certificate that Chromium refuses with ERR_SSL_VERSION_OR_CIPHER_MISMATCH
# (#67) — curl negotiates it without complaint — and a panel whose database had no schema (#66),
# which no curl here asked a question deep enough to notice. This stage opens the panel the way an
# operator does: Chromium, through nginx and its certificate, on the published loopback port. It
# creates the first administrator through /setup, signs in through /login and requires the
# dashboard. ignoreHTTPSErrors forgives the self-signed TRUST failure, which is by design; it does
# not forgive a handshake Chromium refuses, which is the #67 class.
#
# It runs BEFORE the recovery stage because both need the one-time setup token, and it creates the
# same administrator the recovery stage then locks out, so the two compose instead of colliding.
#
# How it was shown to fail (rules/testing.md, "a check must be able to observe what it reports on"):
# the spec run against a URL with no server fails on ERR_CONNECTION_REFUSED, and run against an
# `openssl s_server` presenting an Ed25519 certificate it fails on ERR_SSL_VERSION_OR_CIPHER_MISMATCH
# at the first navigation; both reproducible with the hand-run command in playwright.install.config.ts.
# ---------------------------------------------------------------------------------------------
browser_admin_created="no"
browser_log="${log_dir}/browser-${os}-${arch}-${mode}.log"

stage_browser() {
  local setup_token status=0
  : > "$browser_log"

  # The toolchain is this machine's, not the container's. A missing one is a FAILURE of the stage
  # the operator asked for, never a quiet skip: a --browser run that printed OK without a browser
  # having opened anything is the exact false pass this stage exists to remove.
  if [ ! -d "${root}/frontend/node_modules/@playwright/test" ]; then
    note "browser: frontend/node_modules/@playwright/test is absent (run: cd frontend && npm ci), so no browser opened the panel"
    return 0
  fi

  setup_token="$(docker exec "$container" sed -n 's/^Setup__Token=//p' /etc/maran/panel.env 2>/dev/null | tr -d '\r' || true)"
  # The vacuity guard: with no token the setup form is refused and the stage would report a UI
  # defect that is really an installer one. Fail, never skip.
  if [ "${#setup_token}" -lt 8 ]; then
    note "browser: /etc/maran/panel.env carries no Setup__Token of at least 8 characters, so the browser could not create an administrator and nothing was observed"
    return 0
  fi

  # The token travels in the environment, never on a command line another user can read, and the
  # spec never prints it. The run's output goes to its own log beside the install log.
  (
    cd "${root}/frontend" \
      && MARAN_INSTALLED_URL="https://127.0.0.1:${publish}" \
         MARAN_SETUP_TOKEN="$setup_token" \
         MARAN_ADMIN_USERNAME="polygonadmin" \
         MARAN_ADMIN_EMAIL="polygon.admin@example.test" \
         MARAN_ADMIN_PASSWORD="Polygon-Old-Password-1" \
         npx playwright test -c playwright.install.config.ts
  ) >"$browser_log" 2>&1 || status=$?

  # Scored on the printed result as well as the status: a run that collected nothing exits
  # non-zero in Playwright, but "1 passed" is the line that says the one test actually ran.
  if [ "$status" -eq 0 ] && grep -qE '^[[:space:]]*1 passed' "$browser_log"; then
    browser_admin_created="yes"
    recovery_checks=$((recovery_checks + 1))
    echo "    browser: Chromium negotiated TLS with the panel, created the administrator through /setup, signed in through /login and saw the dashboard"
    echo "    UNOBSERVED HERE: other browsers' TLS stacks (only Chromium ran), and a certificate trusted by a real CA — the self-signed trust failure is forgiven by design."
  else
    note "browser: the Playwright stage failed (exit ${status}); see ${browser_log#"$root"/}"
    grep -E 'ERR_[A-Z_]+|Error:|expect\(' "$browser_log" | head -5 | sed 's/^/      /' >&2 || true
  fi
}

if [ "$failures" -eq 0 ]; then
  if [ "$browser" = "yes" ]; then
    stage_browser
  fi
  stage_lockout_recovery
  stage_diagnostics
  [ "$recovery_checks" -ge 20 ] \
    || note "the lockout-recovery and diagnostics stages made only ${recovery_checks} checks, fewer than the 20 they make when nothing stops them early; a stage that returned early observed little (see ${recovery_log#"$root"/})"
else
  echo "    browser (when asked), lockout-recovery and diagnostics stages NOT RUN: the install itself failed above, and a recovery stage scored on a half-installed panel would be reporting on the install."
fi

if [ "$keep" = "yes" ]; then
  echo "    container kept: docker exec -it ${container} bash"
else
  docker rm -f "$container" >/dev/null 2>&1 || true
fi

if [ "$failures" -gt 0 ]; then
  echo
  echo "${failures} failure(s) on ${os} ${arch} (${mode}). The install log is ${log#"$root"/}."
  exit 1
fi

echo "INSTALL-POLYGON-OK (${os}, ${arch}, ${mode})"
