#!/usr/bin/env bash
# Component builders of `maran release build`: build_api, build_agent, build_frontend and tar_component.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

# build_component: publishes/builds one component and tars its OUTPUT DIRECTORY's
# contents at the tarball root (never the directory itself as a top-level entry) — the
# nesting cli_layout_install_release (installer/cli/lib/release-layout.sh) assumes.
#
# A component this session cannot build here (no toolchain, no network, tree lock held by
# the other lane's quota work) is a NOT DONE, stated loudly, never a placeholder file: an
# empty or fake component archive would pass this script's OWN checksum check and still
# be exactly the kind of decoration rules/testing.md rejects, because the check would see
# nothing different whether the real binary is broken or simply never built.
#
# The release version is stamped into the assembly (-p:Version). Without it the build reports
# the SDK default 1.0.0 whatever tag it was cut from, so the panel's "Running version" dropped the
# pre-release part (1.0.0-beta.5.1 read as 1.0.0) while the release directory, and so the previous
# version, kept it (issue #506).
build_api() {
  local staging="$1" arch="${2:-$(arch_name)}" version="${3:?build_api needs the release version}"
  # 1 for a published release: the build then carries MaranShippingBuild, which makes the Licensing project
  # refuse a missing pinned key (issue #544). Any other build (a local or deploy-local one) is not shipping.
  local shipping="${4:-0}"
  local -a shipping_flag=()
  [ "$shipping" -ne 1 ] || shipping_flag=(-p:MaranShippingBuild=true)
  require_tool dotnet "publishes the backend"
  # Staged under an arch-suffixed name so a two-architecture build does not have its first half
  # overwritten by its second (issue #53).
  local publish_dir="${staging}/api-${arch}"
  rm -rf -- "$publish_dir"
  local rid
  rid="$(dotnet_rid "$arch")"
  [ "$rid" != "unsupported" ] || release_failed "dotnet publish: ${arch} is not an architecture this release builds for"
  # Cross-publishing needs no emulation: `dotnet publish -r linux-arm64` on an x86_64 host produces
  # an aarch64 apphost, measured by RUNNING it in an aarch64 container (it reached Wolverine's
  # startup on glibc 2.28), which is the only evidence that counts here.

  # SELF-CONTAINED, and this is the whole point of the flag rather than a preference. Published
  # framework-dependent, the artifact needs a .NET 9 runtime already on the host; no fresh Debian or
  # Ubuntu carries one and `20-dependencies.sh` installs none. Measured on Ubuntu 24.04: the panel
  # never started — "You must install .NET to run this application ... Failed to resolve
  # libhostfxr.so" — and the installer refused at step 70 because the socket was never bound. There
  # was no host on which either published beta could start.
  #
  # The alternative was to teach the installer to add Microsoft's package repository as root, per
  # family. That couples every install to a third-party feed's availability and spelling, for a
  # runtime this project already ships, signs and checksums. Size is the cost of not doing that.
  dotnet publish "$root/backend/src/Maran.Host/Maran.Host.csproj" \
    -c Release -o "$publish_dir" \
    --self-contained true -r "$rid" \
    -p:PublishTrimmed=false \
    -p:Version="$version" \
    ${shipping_flag[@]+"${shipping_flag[@]}"} \
    >"${staging}/.api-publish.log" 2>&1 \
    || { cat "${staging}/.api-publish.log" >&2; release_failed "dotnet publish failed for Maran.Host (log above)"; }

  # The pinned keys' fingerprints travel inside the api archive, so the code-integrity manifest covers
  # them and `maran status` can print which keys this release pins (name and hash, never contents).
  if [ "$shipping" -eq 1 ]; then
    pinned_keys_lines "${root}/installer/keys" >"${publish_dir}/pinned-keys.txt"
  fi

  # The apphost is what systemd execs, and a publish that produced no runtime beside it is the
  # defect this function exists to prevent — caught here rather than sixty seconds into an install.
  [ -f "${publish_dir}/Maran.Host" ] \
    || release_failed "dotnet publish produced no Maran.Host apphost in ${publish_dir}"
  [ -f "${publish_dir}/libhostfxr.so" ] \
    || release_failed "dotnet publish produced no libhostfxr.so in ${publish_dir}: the artifact is framework-dependent and will not start on a host without a .NET runtime (issue #39)"
}

# The oldest glibc in installer/lib/10-preflight.sh's supported matrix: AlmaLinux 8 and Rocky 8
# carry 2.28, and every other accepted distribution carries more. The agent is built against THIS
# floor because a dynamically linked glibc binary is forward compatible and never backward
# compatible, so the floor is the only build target that runs on all eight (issue #48).
readonly MARAN_AGENT_GLIBC_FLOOR="2.28"

# The release signature (P-384 over SHA-384, `openssl dgst`): sign_release_file, verify_release_file and
# MARAN_SIGNATURE_DIGEST live in scripts/lib/signing.sh, sourced above, so the module package builder
# signs with the very same invocation. The reasoning for the scheme is kept there.

# build_agent: <staging> [arch]. Builds the agent inside the architecture's glibc-floor builder image
# with docker, refuses a binary above the floor, and stages it at <staging>/agent-<arch>/maran-agent.
# <staging> must be absolute (docker -v rejects a relative mount); cmd_build guarantees it. In
# deterministic mode (SOURCE_DATE_EPOCH set) the docker run also gets a fixed clock and path remaps.
build_agent() {
  local staging="$1" arch="${2:-$(arch_name)}"
  # docker rather than cargo, and the substitution is the fix for issue #48: a plain
  # `cargo build --release` on the release host produced an agent requiring GLIBC_2.38 and 2.39,
  # which could not execute on Ubuntu 22.04, Debian 12, AlmaLinux 9 or Rocky 9 — four of the eight
  # distributions preflight accepts. On those hosts the panel installed, nginx served, and every
  # privileged operation failed because the root daemon never ran a single instruction.
  #
  # docker/release/agent-builder-alma8.Dockerfile carries the reasoning, including why a static musl
  # build is the wrong escape (the agent calls `getpwnam_r`, and glibc's NSS cannot be linked
  # statically, so musl would break account lookups wherever SSSD or LDAP answers them).
  require_tool docker "builds the agent against the oldest supported glibc (issue #48)"
  # One builder image per architecture, each a native build INSIDE that architecture rather than a
  # cross-compile: the Rust agent links glibc and calls `getpwnam_r`, so building it in an aarch64
  # AlmaLinux 8 container is what makes "requires at most GLIBC_2.28 on ARM too" a measurement
  # rather than a hope. On an x86_64 host the aarch64 build runs under qemu — slow, and honest.
  local platform image
  platform="$(docker_platform "$arch")"
  [ "$platform" != "unsupported" ] || release_failed "no docker platform for architecture ${arch}"
  # A foreign architecture needs the host's binfmt handlers registered, and they do NOT survive a
  # reboot. Without them the build fails deep inside cargo with `exec /usr/local/bin/cargo: exec
  # format error`, which names neither the cause nor the fix — measured, by rebooting. Checked here
  # with the cheapest possible container, so a release refuses in a second with the command to run.
  if [ "$arch" != "$(arch_name)" ]; then
    docker run --rm --platform "$platform" "${base_image_for_check:-almalinux:8}" true >/dev/null 2>&1 \
      || release_failed "this host cannot execute ${arch} containers, so the ${arch} agent cannot be built. Register the emulation handlers once and retry:
    docker run --privileged --rm tonistiigi/binfmt --install arm64
  They are not persistent: a reboot clears them."
  fi

  image="maran-agent-builder:alma8-${arch}"
  docker build -q --platform "$platform" -f "$root/docker/release/agent-builder-alma8.Dockerfile" \
    -t "$image" "$root/docker/release" >"${staging}/.agent-builder-${arch}.log" 2>&1 \
    || { cat "${staging}/.agent-builder-${arch}.log" >&2; release_failed "could not build the ${arch} agent builder image (log above)"; }

  # The source is mounted READ-ONLY and the target directory is separate, so a release can never be
  # a build that quietly rewrote the tree it was built from. `--locked` is the other half of that:
  # a release that would have had to update Cargo.lock fails instead of shipping dependencies
  # nobody reviewed.
  #
  # --user with the caller's uid keeps the build from leaving root-owned files in the staging
  # directory, which a non-root release would then be unable to remove.
  local work="${staging}/.agent-build" repro_env=()
  release_reproducible_docker_env repro_env
  rm -rf -- "$work"
  install -d "$work"
  # The REPOSITORY ROOT is mounted, not agent/ alone: agent/crates/agent/build.rs compiles the shared
  # contract from `../../../proto/`, so a mount that stopped at agent/ fails with a protoc error
  # about a path outside it (rules/proto.md — the generated code is never committed, so every build
  # needs proto/ present). HOME is set because --user gives the process no passwd entry, and cargo
  # writing to a home it cannot create fails in a way that names neither cargo nor the mount.
  docker run --rm --user "$(id -u):$(id -g)" \
    -v "$root":/src:ro \
    -v "$work":/work \
    -e HOME=/work \
    -e CARGO_HOME=/work/.cargo \
    -e CARGO_TARGET_DIR=/work/target \
    ${repro_env[@]+"${repro_env[@]}"} \
    -w /src/agent "$image" \
    cargo build --release --locked -p maran-agent \
    >"${staging}/.agent-build-${arch}.log" 2>&1 \
    || { cat "${staging}/.agent-build-${arch}.log" >&2; release_failed "cargo build --release -p maran-agent failed in the ${arch} glibc ${MARAN_AGENT_GLIBC_FLOOR} builder (log above)"; }

  local built="${work}/target/release/maran-agent"
  [ -f "$built" ] \
    || release_failed "the agent builder produced no binary at ${built}"

  # The guard, and it reads the ARTIFACT rather than trusting the image that produced it: a builder
  # image that silently moved to a newer base, or a dependency that pulled in a newer symbol, would
  # otherwise ship exactly the defect this function was written to end. The check runs inside the
  # builder because that is where binutils lives, and it asks for the highest glibc version the
  # binary REQUIRES, not the one it was compiled on.
  local highest
  highest="$(docker run --rm --platform "$platform" --user "$(id -u):$(id -g)" -v "$work":/work:ro "$image" \
    sh -c "readelf -V /work/target/release/maran-agent 2>/dev/null \
             | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -uV | tail -1")"
  highest="$(printf '%s' "$highest" | tr -d '[:space:]')"
  [ -n "$highest" ] \
    || release_failed "could not read the agent's required glibc versions, so this release cannot claim it runs on ${MARAN_AGENT_GLIBC_FLOOR} (rules/testing.md)"
  if [ "$(printf '%s\n%s\n' "$MARAN_AGENT_GLIBC_FLOOR" "$highest" | sort -V | tail -1)" != "$MARAN_AGENT_GLIBC_FLOOR" ]; then
    release_failed "the agent requires GLIBC_${highest}, above the ${MARAN_AGENT_GLIBC_FLOOR} floor: it would not start on AlmaLinux 8, Rocky 8, AlmaLinux 9, Debian 12 or Ubuntu 22.04 (issue #48)"
  fi
  echo "   agent (${arch}) requires at most GLIBC_${highest} (floor ${MARAN_AGENT_GLIBC_FLOOR})"

  local out_dir="${staging}/agent-${arch}"
  rm -rf -- "$out_dir"
  install -d "$out_dir"
  install -m 0755 "$built" "$out_dir/maran-agent"
}

build_frontend() {
  local staging="$1" arch="${2:-$(arch_name)}"
  require_tool npm "builds the SPA"
  # The product SPA is never the demo flavour: vite reads VITE_* from the process environment, so an exported
  # VITE_MARAN_DEMO=1 would make this build a demo.
  [ "${VITE_MARAN_DEMO:-}" != "1" ] \
    || release_failed "VITE_MARAN_DEMO=1 is set; the product release must not be built with the demo flavour"
  ( cd "$root/frontend" && npm run build ) \
    >"${staging}/.frontend-build.log" 2>&1 \
    || { cat "${staging}/.frontend-build.log" >&2; release_failed "npm run build failed in frontend/ (log above)"; }
  # The SPA is architecture-independent — it is JavaScript and CSS — but it is staged and published
  # per architecture anyway, so that `<component>-<arch>` is the shape of every manifest entry and
  # installer/lib/50-artifacts.sh's reader needs no special case for one of the three. The cost is
  # one duplicated archive per release; the alternative is a manifest where two components are keyed
  # one way and the third another, which is the kind of exception that outlives its reason.
  # And it holds no trace of the demo: the status request exists only in the demo flavour.
  if grep -rqF -- 'demo/status' "$root/frontend/dist"; then
    release_failed "frontend/dist contains the demo status request; the product SPA must not be the demo flavour"
  fi
  local out_dir="${staging}/frontend-${arch}"
  rm -rf -- "$out_dir"
  cp -r "$root/frontend/dist" "$out_dir"
}

# tar_component: tars a staged output directory's CONTENTS at the archive root, matching
# what cli_layout_install_release (installer/cli/lib/release-layout.sh), which unpacks each
# component into /usr/local/maran/releases/<version>/<component>, expects to find.
tar_component() {
  local src_dir="$1" dest_tar="$2"
  # Deterministic mode is opt-in (release-reproducible.sh); unset, the archive is what it always was.
  if [ "${SOURCE_DATE_EPOCH+x}" = "x" ]; then release_tar_deterministic "$src_dir" "$dest_tar"; return; fi
  ( cd "$src_dir" && tar -czf "$dest_tar" . )
}
