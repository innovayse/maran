#!/usr/bin/env bash
# step 50 and step 66: the release layout a fresh install leaves, and the uninstaller taking it back
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# `maran update` and `maran rollback` (issue #128) move a panel between releases by swapping ONE
# symbolic link, so the properties worth a gate are the ones that make the swap safe: what the three
# static paths and `current` point at, who owns and who can write what they point at, what
# /etc/maran/release.state says, and which public key a root process verifies a release against.
# The first assertion RUNS step 50 on a synthetic signed bundle (the step resolves its key against
# SCRIPT_DIR, so a package directory carrying a throwaway key is the step's own interface, not a
# bypass of it) and step 66, then looks at the disk. The second runs the uninstaller's real
# remove_binaries over a planted layout.
#
# Why these are not in directory-layout.sh or artifacts-and-packages.sh: both sit at the size where
# a reviewer stops reading (rules/architecture.md, "Files stay small"), and this is one subject.

# The version the synthetic bundle carries. A pre-release on purpose: it is a SemVer string the
# step's own validation must accept, with a hyphen, so a regex that only knew x.y.z fails here.
readonly RELEASE_LAYOUT_FIXTURE_VERSION="9.9.9-polygon.1"
readonly RELEASE_LAYOUT_ROOT="/usr/local/maran"
readonly RELEASE_LAYOUT_STATE_FILE="/etc/maran/release.state"
readonly RELEASE_LAYOUT_KEY_DIR="/usr/local/maran/cli/keys"
readonly RELEASE_LAYOUT_DUMP_DIR="/var/backups/maran-update"

# release_layout_link_problem: prints why the symbolic link at $1 is not a link whose target text is
# exactly $2 (and whose resolution is an existing path), and prints nothing when it is.
#   $1 = the path, $2 = the expected target text.
release_layout_link_problem() {
  local path="$1" expected="$2" actual
  if [ ! -L "$path" ]; then
    echo "${path} is not a symbolic link"
    return 0
  fi
  actual="$(readlink -- "$path")"
  if [ "$actual" != "$expected" ]; then
    echo "${path} points at '${actual}', not '${expected}'"
    return 0
  fi
  [ -e "$path" ] || echo "${path} points at '${actual}', which does not exist"
  return 0
}

# release_layout_tree_problem: prints the first way the tree at $1 can be changed by somebody other
# than root, and prints nothing when it cannot: an entry not owned by uid 0 or gid 0, or one (links
# aside, whose own mode is never consulted) that group or others can write.
release_layout_tree_problem() {
  local found
  found="$(find "$1" ! -type l \( ! -user root -o ! -group root -o -perm /022 \) -print -quit 2>/dev/null)"
  [ -z "$found" ] || echo "${found} is $(stat -c '%U:%G mode %a' -- "$found"): root alone must be able to write a release tree"
}

# release_layout_service_can_write: success when the service account can write the path $1. Asked of
# the kernel with the account's own credentials, so a mode that LOOKS closed but is not (a group
# the account belongs to, an ACL) cannot hide behind a stat.
release_layout_service_can_write() {
  runuser -u "$POLYGON_SERVICE_USER" -- test -w "$1"
}

# release_layout_arch: this machine's architecture spelled the way install.sh exports it.
release_layout_arch() {
  case "$(uname -m)" in
    x86_64|amd64) echo "x86_64" ;;
    aarch64|arm64) echo "aarch64" ;;
    *) fail "this image's architecture '$(uname -m)' is neither x86_64 nor aarch64, so step 50's MARAN_ARCH cannot be set for the synthetic bundle" ;;
  esac
}

# release_layout_build_bundle: writes, under $1, an installer package directory carrying the shipped
# CLI libraries (cli/), a throwaway release key (keys/release-signing.pub) and an offline bundle
# (bundle.tar.gz) holding a manifest in the shape `maran release build` writes, its ECDSA P-384 /
# SHA-384 signature, and the three component archives. The api archive carries one group- and
# world-writable member on purpose: a signed archive is still data a root process unpacks, and the
# tree it leaves must not be writable by
# anybody else however the member was packed.
#
# The outer tarball lists its five members by name, as scripts/lib/release-package.sh does, so it has
# no `./` member: a fixture packed with `-C bundle .` carries one, the vetting of real bundles never
# sees it, and the step would be scored on an input no release produces. The three component archives
# are packed from their directories (`-C payload/<component> .`), so their members do start with
# `./`: the release-bundle builder is not run here, and whether it writes the prefix is not observed.
# The caller's work directory comes from mktemp -d: owned by the running uid (root, in the polygon)
# with mode 0700, which is what the offline path demands of the directory holding a tarball (root
# owned, not group or world writable).
#   $1 = an empty directory, $2 = the architecture, $3 = the channel.
release_layout_build_bundle() {
  local dir="$1" arch="$2" channel="$3" component sum members first=1
  install -d -m 0755 "${dir}/pkg/keys" "${dir}/payload/api/lib" "${dir}/payload/agent" "${dir}/payload/frontend" "${dir}/bundle"
  # Step 50 sources the CLI libraries from the package it runs out of (${SCRIPT_DIR}/cli/lib), so the
  # synthetic package carries the REAL ones: the code under test is the shipped code, and only the
  # key and the bundle are synthetic.
  cp -a -- "${INSTALLER_ROOT}/cli" "${dir}/pkg/cli"
  openssl ecparam -name secp384r1 -genkey -noout -out "${dir}/throwaway.key" 2>/dev/null
  openssl ec -in "${dir}/throwaway.key" -pubout -out "${dir}/pkg/keys/release-signing.pub" 2>/dev/null
  printf '#!/bin/sh\nexit 0\n' > "${dir}/payload/api/Maran.Host"
  printf 'a library\n' > "${dir}/payload/api/lib/loose.dll"
  printf '#!/bin/sh\nexit 0\n' > "${dir}/payload/agent/maran-agent"
  printf '<html></html>\n' > "${dir}/payload/frontend/index.html"
  chmod 0755 "${dir}/payload/api/Maran.Host" "${dir}/payload/agent/maran-agent"
  chmod 0666 "${dir}/payload/api/lib/loose.dll"
  for component in api agent frontend; do
    tar -czf "${dir}/bundle/${component}.tar.gz" -C "${dir}/payload/${component}" .
  done
  {
    echo "{"
    echo "  \"version\": \"${RELEASE_LAYOUT_FIXTURE_VERSION}\","
    echo "  \"channel\": \"${channel}\","
    echo "  \"artifacts\": {"
    for component in api agent frontend; do
      sum="$(sha256sum "${dir}/bundle/${component}.tar.gz" | awk '{print $1}')"
      [ "$first" -eq 1 ] || echo ","
      first=0
      printf '    "%s-%s": {\n      "url": "https://releases.maran.innovayse.com/%s/%s-%s.tar.gz",\n      "sha256": "%s"\n    }' \
        "$component" "$arch" "$channel" "$component" "$arch" "$sum"
    done
    echo
    echo "  }"
    echo "}"
  } > "${dir}/bundle/manifest.json"
  openssl dgst -sha384 -sign "${dir}/throwaway.key" -out "${dir}/bundle/manifest.json.sig" "${dir}/bundle/manifest.json"
  tar -czf "${dir}/bundle.tar.gz" -C "${dir}/bundle" manifest.json manifest.json.sig api.tar.gz agent.tar.gz frontend.tar.gz
  # The fixture must have the shape the comment above promises: five members, none spelled `./...`.
  members="$(tar -tzf "${dir}/bundle.tar.gz")"
  [ "$(grep -c . <<<"$members")" -eq 5 ] && [ "$(grep -c '^\./' <<<"$members" || true)" -eq 0 ] \
    || fail "the synthetic offline bundle does not list exactly its five members by name: ${members//$'\n'/ }"
}

# assert_a_fresh_install_leaves_the_release_layout_the_design_requires: step 50 installs a signed
# offline bundle and step 66 installs the CLI, and what they leave is the layout `maran update`
# swaps inside:
#   - /usr/local/maran/current is a symbolic link to releases/<version> (a relative target), and
#     /usr/local/maran/{api,agent,frontend} are symbolic links to current/<component>, each
#     resolving to a real file the unit's ExecStart names;
#   - releases/<version> is root-owned and NO entry of it can be written by group or others, even
#     the member the archive packed 0666, and the service account cannot write it (asked of the
#     kernel as that account);
#   - /etc/maran/release.state is root:root 0644 with exactly CHANNEL, VERSION and an empty
#     PREVIOUS;
#   - /usr/local/maran/cli/keys/release-signing.pub is root 0644 in a root 0755 directory, byte for
#     byte installer/keys/release-signing.pub, and a P-384 public key.
#
# Each refusal-shaped check has its inverse control, planted and shown to be seen: the link checker
# is handed a real directory and a wrong target, the tree checker a world-writable file and a
# foreign owner, the service-account probe a path that account CAN write, and the key comparison a
# copy with one byte changed.
#
# UNOBSERVED HERE: this is a FIRST install into an image with no previous release, so adoption of a
# pre-layout host (a real directory at /usr/local/maran/api moved into releases/legacy-<stamp>) and
# a second install over a first are not run. The bundle is synthetic and signed by a throwaway key,
# so the REAL release key's acceptance of a published bundle is not observed (the release selftest
# owns that), and nothing here boots the units: that the api and agent start from these paths is the
# install polygon. SELinux contexts on the releases and the links (AlmaLinux enforces) are not
# exercised by a container build.
assert_a_fresh_install_leaves_the_release_layout_the_design_requires() {
  local work arch version release component path found checked=0 problem state key
  version="$RELEASE_LAYOUT_FIXTURE_VERSION"
  release="${RELEASE_LAYOUT_ROOT}/releases/${version}"

  # The service account and /etc/maran come from step 40, as in a real install.
  run_user_step 'create_service_user >/dev/null
create_directory_layout' \
    || fail "step 40's create_directory_layout failed before the release layout assertion"

  # This assertion writes the paths a real install uses. On a host that already has them it would
  # destroy the real thing, so it refuses rather than assuming it is in a throwaway image.
  for path in "${RELEASE_LAYOUT_ROOT}/releases" "${RELEASE_LAYOUT_ROOT}/current" "${RELEASE_LAYOUT_ROOT}/api" \
    "${RELEASE_LAYOUT_ROOT}/agent" "${RELEASE_LAYOUT_ROOT}/frontend" "$RELEASE_LAYOUT_STATE_FILE"; do
    if [ -e "$path" ] || [ -L "$path" ]; then
      fail "${path} already exists. This assertion installs and then deletes the release layout at its real paths, so it refuses to run rather than destroy a real installation."
    fi
  done

  # The controls come first, so the checkers are shown capable before their silence is believed.
  work="$(mktemp -d)"
  install -d -m 0755 "${work}/plant/real-directory"
  # The checker also demands that a link RESOLVE, so the control that must be accepted needs a real
  # target, spelled the way the installer spells its own: relative, and resolved from the link's own
  # directory (`current` -> releases/<v>, `api` -> current/api). A link to a name nothing holds is
  # planted separately, as a control that must be refused for that reason alone.
  : > "${work}/plant/wrong-target"
  install -d -m 0755 "${work}/plant/releases/x"
  ln -s wrong-target "${work}/plant/wrong-link"
  ln -s releases/x "${work}/plant/current"
  ln -s nothing-is-here "${work}/plant/dangling-link"
  [ -n "$(release_layout_link_problem "${work}/plant/real-directory" "wrong-target")" ] \
    || fail "the link checker reports nothing for a real directory where a symbolic link is required, so its silence over ${RELEASE_LAYOUT_ROOT}/current means nothing"
  [ -n "$(release_layout_link_problem "${work}/plant/wrong-link" "releases/x")" ] \
    || fail "the link checker reports nothing for a link with the wrong target text, so its silence over the real links means nothing"
  [ -n "$(release_layout_link_problem "${work}/plant/dangling-link" "nothing-is-here")" ] \
    || fail "the link checker reports nothing for a link whose target text is right but resolves to nothing, so its silence over the real links does not show they resolve"
  [ -z "$(release_layout_link_problem "${work}/plant/wrong-link" "wrong-target")" ] \
    || fail "the link checker refuses a link whose target text is exactly the expected one and which resolves, so it refuses everything and its refusals below prove nothing: $(release_layout_link_problem "${work}/plant/wrong-link" "wrong-target")"
  [ -z "$(release_layout_link_problem "${work}/plant/current" "releases/x")" ] \
    || fail "the link checker refuses a relative link spelled the way the installer writes 'current' (releases/<v>) and resolving from the link's own directory, so it refuses everything: $(release_layout_link_problem "${work}/plant/current" "releases/x")"
  install -d -m 0755 "${work}/plant/tree"
  : > "${work}/plant/tree/writable"
  chmod 0666 "${work}/plant/tree/writable"
  [ -n "$(release_layout_tree_problem "${work}/plant/tree")" ] \
    || fail "the tree checker reports nothing for a world-writable file, so its silence over the release tree means nothing"
  chmod 0644 "${work}/plant/tree/writable"
  chown nobody "${work}/plant/tree/writable"
  [ -n "$(release_layout_tree_problem "${work}/plant/tree")" ] \
    || fail "the tree checker reports nothing for a file owned by another uid, so its silence over the release tree means nothing"
  chown root "${work}/plant/tree/writable"
  chmod 0755 "${work}/plant" "${work}/plant/tree"
  chmod 0755 "${work}/plant/real-directory"
  [ -z "$(release_layout_tree_problem "${work}/plant/tree")" ] \
    || fail "the tree checker refuses a root-owned 0644 file in a root-owned 0755 directory, so it refuses everything: $(release_layout_tree_problem "${work}/plant/tree")"
  release_layout_service_can_write /var/lib/maran \
    || fail "the service account cannot write /var/lib/maran, which step 40 makes it the owner of, so the probe cannot tell a writable path from a closed one and its refusals below prove nothing"
  checked=$((checked + 1))

  # Step 50 itself, on the synthetic signed bundle, the way install.sh runs it.
  arch="$(release_layout_arch)"
  release_layout_build_bundle "$work" "$arch" beta
  # The package's key is the throwaway one and not the shipped one, so the step's success below
  # shows it verified against the key it was handed (the KEYFILE argument) and not a baked-in copy.
  ! cmp -s "${work}/pkg/keys/release-signing.pub" "${INSTALLER_ROOT}/keys/release-signing.pub" \
    || fail "the synthetic package's key is identical to the shipped release key, so step 50's success below would not show which key it verified against"
  [ -f "${work}/pkg/cli/lib/release-layout.sh" ] && [ -f "${work}/pkg/cli/lib/release-verify.sh" ] \
    || fail "the synthetic package carries no cli/lib/release-layout.sh or release-verify.sh, so step 50 would have nothing to source"
  SCRIPT_DIR="${work}/pkg" MARAN_CHANNEL=beta MARAN_OFFLINE_TARBALL="${work}/bundle.tar.gz" MARAN_ARCH="$arch" \
    bash -c 'set -euo pipefail
. "$1"
. "$2"
step_artifacts' _ "$COMMON_LIB" "$ARTIFACTS_STEP" >"${work}/step.log" 2>&1 \
    || fail "step 50's step_artifacts failed on a correctly signed offline bundle: $(cat "${work}/step.log")"
  checked=$((checked + 1))

  # 1. `current`, and the three static links through it.
  problem="$(release_layout_link_problem "${RELEASE_LAYOUT_ROOT}/current" "releases/${version}")"
  [ -z "$problem" ] || fail "after a fresh install: ${problem}"
  [ -d "${RELEASE_LAYOUT_ROOT}/releases/${version}" ] && [ ! -L "${RELEASE_LAYOUT_ROOT}/releases/${version}" ] \
    || fail "${release} is not a real directory after a fresh install"
  for component in api agent frontend; do
    problem="$(release_layout_link_problem "${RELEASE_LAYOUT_ROOT}/${component}" "current/${component}")"
    [ -z "$problem" ] || fail "after a fresh install: ${problem}"
    [ "$(readlink -f -- "${RELEASE_LAYOUT_ROOT}/${component}")" = "${release}/${component}" ] \
      || fail "${RELEASE_LAYOUT_ROOT}/${component} resolves to $(readlink -f -- "${RELEASE_LAYOUT_ROOT}/${component}"), not ${release}/${component}"
    checked=$((checked + 1))
  done
  # What the units execute resolves through the links to the release's own file. The unit lines are
  # read as text; the files are asked of the disk.
  [ -f /usr/local/maran/api/Maran.Host ] && [ -x /usr/local/maran/api/Maran.Host ] \
    || fail "/usr/local/maran/api/Maran.Host (the api unit's ExecStart) is not an executable file through the links"
  [ -f /usr/local/maran/agent/maran-agent ] && [ -x /usr/local/maran/agent/maran-agent ] \
    || fail "/usr/local/maran/agent/maran-agent (the agent unit's ExecStart) is not an executable file through the links"
  grep -qxF 'ExecStart=/usr/local/maran/api/Maran.Host' "$API_UNIT" \
    && grep -qE '^ExecStart=/usr/local/maran/agent/maran-agent( |$)' "$AGENT_UNIT" \
    || fail "a unit no longer starts its binary from /usr/local/maran/<component>/, the path the static links keep valid across releases"
  checked=$((checked + 1))

  # 2. The release tree is root's alone: owner and mode by stat, then asked as the service account.
  assert_directory_is "${RELEASE_LAYOUT_ROOT}/releases" "0:0:755"
  problem="$(release_layout_tree_problem "$release")"
  [ -z "$problem" ] || fail "after a fresh install, in ${release}: ${problem}"
  # An install unpacks into releases/<version>.partial and renames it; nothing of that may remain,
  # or `maran update` would start by clearing the previous install's leftovers.
  found="$(find "${RELEASE_LAYOUT_ROOT}/releases" -mindepth 1 -maxdepth 1 ! -name "$version" -print -quit)"
  [ -z "$found" ] || fail "a fresh install left ${found} beside releases/${version}"
  for path in "$release" "${release}/api" "${release}/api/Maran.Host" "${release}/api/lib/loose.dll" \
    "${release}/agent/maran-agent" "${release}/frontend/index.html" "${RELEASE_LAYOUT_ROOT}/releases"; do
    if release_layout_service_can_write "$path"; then
      fail "the service account (${POLYGON_SERVICE_USER}) can write ${path}: a compromised api could replace the code a root restart runs"
    fi
  done
  checked=$((checked + 1))

  # 3. release.state: root:root 0644, three lines, the channel the bundle declared.
  [ -f "$RELEASE_LAYOUT_STATE_FILE" ] && [ ! -L "$RELEASE_LAYOUT_STATE_FILE" ] \
    || fail "${RELEASE_LAYOUT_STATE_FILE} is not a regular file after a fresh install"
  [ "$(stat -c '%u:%g:%a' -- "$RELEASE_LAYOUT_STATE_FILE")" = "0:0:644" ] \
    || fail "${RELEASE_LAYOUT_STATE_FILE} is $(stat -c '%u:%g:%a' -- "$RELEASE_LAYOUT_STATE_FILE"), not 0:0:644"
  state="$(cat -- "$RELEASE_LAYOUT_STATE_FILE")"
  [ "$state" = "$(printf 'CHANNEL=beta\nVERSION=%s\nPREVIOUS=' "$version")" ] \
    || fail "${RELEASE_LAYOUT_STATE_FILE} reads '${state//$'\n'/ | }', not CHANNEL=beta, VERSION=${version} and an empty PREVIOUS"
  checked=$((checked + 1))

  # 4. The CLI and its key, from step 66 against the REAL package (INSTALLER_ROOT), not the
  # throwaway one above.
  run_cli_step >/dev/null || fail "step 66's step_cli failed after a fresh install"
  key="${RELEASE_LAYOUT_KEY_DIR}/release-signing.pub"
  for path in /usr/local/maran/cli "$RELEASE_LAYOUT_KEY_DIR"; do
    [ -d "$path" ] && [ ! -L "$path" ] && [ "$(stat -c '%u:%g:%a' -- "$path")" = "0:0:755" ] \
      || fail "${path} is not a real root:root 0755 directory (found $(stat -c '%u:%g:%a' -- "$path" 2>/dev/null || echo absent)): a key directory root does not own is a key anyone can swap"
  done
  assert_file_is "$key" "0:0:644"
  cmp -s "${INSTALLER_ROOT}/keys/release-signing.pub" "$key" \
    || fail "${key} differs from installer/keys/release-signing.pub: the CLI would verify releases against a key the package does not carry"
  cp -- "$key" "${work}/key-one-byte-off"
  printf 'x' >> "${work}/key-one-byte-off"
  cmp -s "${INSTALLER_ROOT}/keys/release-signing.pub" "${work}/key-one-byte-off" \
    && fail "cmp reports a key with one byte appended as identical to the package's, so its agreement over ${key} means nothing"
  openssl ec -pubin -in "$key" -noout -text 2>/dev/null | grep -qE 'ASN1 OID: secp384r1|NIST CURVE: P-384' \
    || fail "${key} is not an ECDSA P-384 public key, the scheme release signing, the installer and the CLI use"
  [ "$(find "$RELEASE_LAYOUT_KEY_DIR" -mindepth 1 | wc -l)" -eq 1 ] \
    || fail "${RELEASE_LAYOUT_KEY_DIR} holds more than the one key: $(ls -A "$RELEASE_LAYOUT_KEY_DIR")"
  checked=$((checked + 1))

  # Put the image back as it was found: the layout, the state, the key, the CLI, the staging
  # directory step 50 removes itself, and the scratch directory.
  rm -f -- "${RELEASE_LAYOUT_ROOT}/current" "${RELEASE_LAYOUT_ROOT}/api" "${RELEASE_LAYOUT_ROOT}/agent" \
    "${RELEASE_LAYOUT_ROOT}/frontend" "$RELEASE_LAYOUT_STATE_FILE"
  rm -rf -- "${RELEASE_LAYOUT_ROOT}/releases" /var/lib/maran-artifact-staging "$work"
  remove_the_installed_break_glass_command
  for path in "${RELEASE_LAYOUT_ROOT}/releases" "${RELEASE_LAYOUT_ROOT}/current" "${RELEASE_LAYOUT_ROOT}/api" \
    "${RELEASE_LAYOUT_ROOT}/agent" "${RELEASE_LAYOUT_ROOT}/frontend" "$RELEASE_LAYOUT_STATE_FILE" "$RELEASE_LAYOUT_KEY_DIR"; do
    if [ -e "$path" ] || [ -L "$path" ]; then
      fail "${path} is still present after this assertion removed it"
    fi
  done

  echo "The release layout after a fresh install (step 50 on a signed offline bundle, then step 66): current -> releases/${version}, api, agent and frontend -> current/<component> and resolving to the files the units execute, the release tree owned by root with no group- or world-writable entry (the archive's 0666 member included) and unwritable by the service account as asked of the kernel, release.state root:root 0644 reading CHANNEL=beta, VERSION=${version} and an empty PREVIOUS, and the CLI's key root:root 0644 in a root 0755 directory, identical to installer/keys/release-signing.pub and a P-384 key; each checker shown to see a planted defect. ${checked} checks made."
  echo "UNOBSERVED HERE: a first install only. Adoption of a pre-layout host (a real /usr/local/maran/api moved into releases/legacy-<stamp>) and a second install over a first are not run. The bundle is synthetic and signed by a throwaway key, so a published release against the real key is not observed here (the release selftest owns it), and no unit is started from these paths (the install polygon owns that). The writability probe is one kernel answer for one account: an ACL or a capability this image does not carry is not exercised. SELinux contexts on the releases and links are not exercised by a container build."
}

# assert_the_uninstaller_removes_the_release_layout_and_the_update_state: `remove_binaries` takes
# releases/, current, the three static links and the CLI's key directory with /usr/local/maran, and
# takes /etc/maran/release.state and /var/backups/maran-update by name, and this assertion can tell
# each from a survivor.
#
# The update dumps are a copy of the panel database and belong to the panel that is being removed,
# which is why they go while /var/backups/maran (a customer's only copy of their data) and
# /var/backups/maran-panel (the operator's own panel archives) stay.
#
# Proved in both directions, because a promise about a REMOVAL is satisfied by an uninstaller that
# never had the lines and an assertion that never looked. Two inverse controls, each a copy of the
# uninstaller with exactly one removal deleted (verified to have landed with a count and `cmp`,
# since a mutation that silently failed to apply is how mutations here were scored blind): without
# `rm -rf /usr/local/maran` the tree survives, without the two named lines the state file and the
# dump directory survive. A symlink planted under releases/ at a victim directory outside the tree
# shows the removal unlinks the link and never reaches its target.
#
# UNOBSERVED HERE: the uninstaller's shell function against a layout this assertion planted by hand,
# not an uninstall of a real install, and not that main() reaches remove_binaries. /etc/maran
# itself is removed or kept by remove_maran_config_directory, which this does not run, so
# release.state is shown to be removed by remove_binaries alone, the case a kept /etc/maran needs.
assert_the_uninstaller_removes_the_release_layout_and_the_update_state() {
  local work victim mutant output path component planted=""
  work="$(mktemp -d)"
  victim="${work}/victim"
  install -d -m 0755 "$victim"
  : > "${victim}/precious"

  # A planted layout, by hand, at the real paths (refusing to run over a real one).
  for path in "${RELEASE_LAYOUT_ROOT}/releases" "${RELEASE_LAYOUT_ROOT}/current" "${RELEASE_LAYOUT_ROOT}/api" \
    "${RELEASE_LAYOUT_ROOT}/agent" "${RELEASE_LAYOUT_ROOT}/frontend" "$RELEASE_LAYOUT_STATE_FILE" "$RELEASE_LAYOUT_DUMP_DIR"; do
    if [ -e "$path" ] || [ -L "$path" ]; then
      fail "${path} already exists. This assertion plants and then removes the release layout at its real paths, so it refuses to run rather than destroy a real installation."
    fi
  done
  plant_release_layout() {
    for component in api agent frontend; do
      install -d -m 0755 "${RELEASE_LAYOUT_ROOT}/releases/1.0.0/${component}"
    done
    ln -s "$victim" "${RELEASE_LAYOUT_ROOT}/releases/link-to-victim"
    ln -s releases/1.0.0 "${RELEASE_LAYOUT_ROOT}/current"
    for component in api agent frontend; do
      ln -s "current/${component}" "${RELEASE_LAYOUT_ROOT}/${component}"
    done
    install -d -m 0755 "$RELEASE_LAYOUT_KEY_DIR"
    : > "${RELEASE_LAYOUT_KEY_DIR}/release-signing.pub"
    install -d -m 0750 -o root -g "$POLYGON_SERVICE_GROUP" /etc/maran
    printf 'CHANNEL=stable\nVERSION=1.0.0\nPREVIOUS=\n' > "$RELEASE_LAYOUT_STATE_FILE"
    install -d -m 0700 -o root -g root "$RELEASE_LAYOUT_DUMP_DIR"
    printf 'a dump\n' > "${RELEASE_LAYOUT_DUMP_DIR}/pre-0.9.0-to-1.0.0-20260101T000000Z.dump"
    chmod 0600 "${RELEASE_LAYOUT_DUMP_DIR}/pre-0.9.0-to-1.0.0-20260101T000000Z.dump"
  }
  planted_paths() {
    printf '%s\n' "${RELEASE_LAYOUT_ROOT}/releases" "${RELEASE_LAYOUT_ROOT}/current" "${RELEASE_LAYOUT_ROOT}/api" \
      "${RELEASE_LAYOUT_ROOT}/agent" "${RELEASE_LAYOUT_ROOT}/frontend" "$RELEASE_LAYOUT_KEY_DIR" \
      "$RELEASE_LAYOUT_STATE_FILE" "$RELEASE_LAYOUT_DUMP_DIR"
  }

  # The lines the controls delete must exist exactly once, in the form they are mutated in.
  [ "$(grep -c '^  rm -rf /usr/local/maran$' "$UNINSTALLER")" -eq 1 ] \
    && [ "$(grep -c '^  rm -f /etc/maran/release.state$' "$UNINSTALLER")" -eq 1 ] \
    && [ "$(grep -c '^  rm -rf /var/backups/maran-update$' "$UNINSTALLER")" -eq 1 ] \
    || fail "uninstall.sh no longer carries exactly one '  rm -rf /usr/local/maran', one '  rm -f /etc/maran/release.state' and one '  rm -rf /var/backups/maran-update' line in the form this assertion mutates; it would be mutating nothing"

  # Control 1: without the removal of /usr/local/maran, releases/, current, the links and the key
  # directory SURVIVE (and the two named lines still do their work).
  plant_release_layout
  mutant="$(mktemp)"
  sed -e '\|^  rm -rf /usr/local/maran$|d' "$UNINSTALLER" > "$mutant"
  cmp -s "$UNINSTALLER" "$mutant" && fail "the first inverse control's copy of the uninstaller is identical to the original, so the survival below would be about nothing"
  [ "$(grep -c '^  rm -rf /usr/local/maran$' "$mutant")" -eq 0 ] \
    || fail "the first inverse control's copy still removes /usr/local/maran"
  output="$(bash -c 'set -euo pipefail
. "$1"
remove_binaries' _ "$mutant" 2>&1)" || fail "the mutated uninstaller's remove_binaries failed: ${output}"
  for path in "${RELEASE_LAYOUT_ROOT}/releases" "${RELEASE_LAYOUT_ROOT}/api" "$RELEASE_LAYOUT_KEY_DIR"; do
    [ -e "$path" ] || [ -L "$path" ] \
      || fail "an uninstaller WITHOUT the removal of /usr/local/maran still removed ${path}, so something else removes it and this assertion cannot attribute the removal below to that line"
  done
  [ -L "${RELEASE_LAYOUT_ROOT}/current" ] || fail "the control's current link is gone"
  rm -f -- "$mutant"
  # Reset for the second control: put the two lines' victims back, leave the tree.
  install -d -m 0750 -o root -g "$POLYGON_SERVICE_GROUP" /etc/maran
  printf 'CHANNEL=stable\nVERSION=1.0.0\nPREVIOUS=\n' > "$RELEASE_LAYOUT_STATE_FILE"
  install -d -m 0700 -o root -g root "$RELEASE_LAYOUT_DUMP_DIR"
  : > "${RELEASE_LAYOUT_DUMP_DIR}/pre-0.9.0-to-1.0.0-20260101T000000Z.dump"

  # Control 2: without the two named lines the state file and the dump directory SURVIVE.
  mutant="$(mktemp)"
  sed -e '\|^  rm -f /etc/maran/release.state$|d' -e '\|^  rm -rf /var/backups/maran-update$|d' "$UNINSTALLER" > "$mutant"
  cmp -s "$UNINSTALLER" "$mutant" && fail "the second inverse control's copy of the uninstaller is identical to the original, so the survival below would be about nothing"
  [ "$(grep -cE 'release\.state$|maran-update$' "$mutant")" -eq 0 ] \
    || fail "the second inverse control's copy still removes the state file or the dump directory"
  # The tree is already in place from control 1; remove_binaries on the mutant removes it, so the
  # survival asked about is the state file's and the dump directory's alone.
  output="$(bash -c 'set -euo pipefail
. "$1"
remove_binaries' _ "$mutant" 2>&1)" || fail "the second mutated uninstaller's remove_binaries failed: ${output}"
  rm -f -- "$mutant"
  [ -f "$RELEASE_LAYOUT_STATE_FILE" ] && [ -d "$RELEASE_LAYOUT_DUMP_DIR" ] \
    || fail "an uninstaller WITHOUT the two removal lines still removed the state file or the dump directory, so something else removes them and this assertion cannot attribute the removal below to those lines"

  # The real thing, over a freshly planted full layout.
  rm -rf -- "${RELEASE_LAYOUT_ROOT}/releases" "${RELEASE_LAYOUT_ROOT}/current" "${RELEASE_LAYOUT_ROOT}/api" \
    "${RELEASE_LAYOUT_ROOT}/agent" "${RELEASE_LAYOUT_ROOT}/frontend" "$RELEASE_LAYOUT_STATE_FILE" "$RELEASE_LAYOUT_DUMP_DIR" "$RELEASE_LAYOUT_KEY_DIR"
  plant_release_layout
  for path in $(planted_paths); do
    [ -e "$path" ] || [ -L "$path" ] || fail "${path} was not planted, so its absence after the uninstaller would prove nothing"
  done
  output="$(run_uninstaller 'remove_binaries' 2>&1)" \
    || fail "the real uninstaller's remove_binaries failed: ${output}"
  for path in $(planted_paths); do
    if [ -e "$path" ] || [ -L "$path" ]; then
      planted="${planted} ${path}"
    fi
  done
  [ -z "$planted" ] || fail "the uninstaller left behind:${planted}"
  [ -f "${victim}/precious" ] \
    || fail "the uninstaller followed a symbolic link under releases/ and deleted what it pointed at"

  # remove_binaries took /usr/local/maran as a tree, as it does on a real host; put the layout back
  # through the installer's own step so nothing that follows depends on it being absent.
  run_user_step 'create_directory_layout' >/dev/null \
    || fail "step 40's create_directory_layout could not restore the layout this assertion's deleter removed"
  [ -d /usr/local/maran ] || fail "/usr/local/maran is missing after this assertion restored the layout"
  rm -rf -- "$work"
  unset -f plant_release_layout planted_paths

  echo "The uninstaller removes releases/, current, the three static links and the CLI key directory (with /usr/local/maran), /etc/maran/release.state and /var/backups/maran-update, never follows a link under releases/ to its target, and a copy of it without the /usr/local/maran line leaves the tree, a copy without the two named lines leaves the state file and the dump directory, so each check can see a survivor."
  echo "UNOBSERVED HERE: this is the uninstaller's remove_binaries against a layout this assertion planted by hand, not an uninstall of a real install; main() reaching remove_binaries and /etc/maran being removed whole (remove_maran_config_directory) are not run here."
}
