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
arch="x86_64"

usage() {
  cat >&2 <<'USAGE'
usage: maran installer [--os <version>] [--mode local|get] [--keep]

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
docker run -d --name "$container" --platform "$platform" --privileged --cgroupns=host \
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
