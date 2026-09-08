#!/usr/bin/env bash
# Step 50: fetch the release artifacts (maran-api, maran-agent, frontend
# bundle) over HTTPS and verify their Ed25519 signature against a manifest BEFORE
# unpacking anything. An artifact that cannot be verified aborts the install — this is
# the installer's only trust boundary against a compromised download mirror or a
# man-in-the-middle on the download path, so it is never bypassed, never made a warning.
# Also supports a fully offline install from a pre-verified local tarball.
set -euo pipefail

readonly MARAN_RELEASE_BASE_URL="https://releases.maran.com"
readonly MARAN_INSTALL_ROOT="/usr/local/maran"
# MARAN_ARTIFACT_TMP: staging for downloaded archives between verification and extraction.
# It is deliberately NOT under /var/lib/maran: that directory is created panel:panel 0750 by
# 40-user.sh and is in maran-api.service's ReadWritePaths=, so the unprivileged uid the api
# runs as owns it and can place any entry inside it — a symlink or a directory of its own at
# the staging name. Root would then download into a directory the api controls, and because
# every archive is checksummed before ANY of them is extracted, the api could swap
# agent.tar.gz in that window; the extracted file becomes /usr/local/maran/agent/maran-agent,
# which maran-agent.service runs as root. Staging lives directly under /var/lib instead
# (root:root 0755), where no unprivileged uid can create an entry at all.
readonly MARAN_ARTIFACT_TMP="/var/lib/maran-artifact-staging"

# MARAN_RELEASE_PUBLIC_KEY_PEM: the Ed25519 public key that signs release manifests,
# baked into the installer itself (never fetched at install time — an attacker who can
# serve the installer AND a fake manifest would otherwise control both halves of the
# trust check). This is a placeholder key ID; the real key ships from Innovayse's
# release signing infrastructure and is substituted into this file at release-build time.
# Resolved against the installer package (SCRIPT_DIR, exported by install.sh), NOT against
# the install root: /usr/local/maran is created and populated by THIS step, so a key looked
# up there could never exist on a first install and every install would abort on a missing
# key. The key ships next to the installer, which is also what makes it "baked in".
readonly MARAN_RELEASE_PUBLIC_KEY_PEM="${SCRIPT_DIR}/keys/release-signing.pub"

# manifest_url / artifact_url: versioned by channel (stable|beta), set via
# MARAN_CHANNEL exported by install.sh.
manifest_url() { echo "${MARAN_RELEASE_BASE_URL}/${MARAN_CHANNEL}/manifest.json"; }
manifest_sig_url() { echo "${MARAN_RELEASE_BASE_URL}/${MARAN_CHANNEL}/manifest.json.sig"; }

# fetch: downloads to a fixed staging path, failing loudly on any HTTP or TLS error.
# --fail turns a 4xx/5xx into a non-zero exit instead of silently saving an error page.
fetch() {
  local url="$1" dest="$2"
  curl --fail --silent --show-error --location --output "$dest" "$url"
}

# prepare_staging_dir: creates the staging directory as a fresh, root-owned, root-only
# directory, and refuses to continue if it is anything else. `mkdir -p` was not enough on its
# own: it exits 0 on an existing path, following a symlink and checking neither owner nor
# mode, so it silently accepts a directory an attacker put there first. The leading `rm -rf`
# removes a stale directory or a planted symlink (removing a symlink unlinks the link, never
# its target), `install -d` then creates a real directory, and the gate below is what makes
# this a refusal rather than a hope: a staging path that is a symlink, is not a directory, is
# not owned by uid 0, or is group/other-accessible aborts the install instead of being used.
prepare_staging_dir() {
  local owner mode
  rm -rf -- "$MARAN_ARTIFACT_TMP"
  install -d -o root -g root -m 0700 "$MARAN_ARTIFACT_TMP"
  if [ -L "$MARAN_ARTIFACT_TMP" ] || [ ! -d "$MARAN_ARTIFACT_TMP" ]; then
    echo "50-artifacts.sh: staging path ${MARAN_ARTIFACT_TMP} is not a real directory. Aborting." >&2
    exit 1
  fi
  owner="$(stat -c '%u' "$MARAN_ARTIFACT_TMP")"
  mode="$(stat -c '%a' "$MARAN_ARTIFACT_TMP")"
  if [ "$owner" -ne 0 ] || [ "$mode" != "700" ]; then
    echo "50-artifacts.sh: staging directory ${MARAN_ARTIFACT_TMP} must be owned by root with mode 0700" >&2
    echo "  (found owner uid ${owner}, mode ${mode}). Refusing to stage release artifacts in a" >&2
    echo "  directory another uid can write to. Aborting." >&2
    exit 1
  fi
}

# verify_manifest_signature: checks the manifest's Ed25519 signature against the baked-in
# public key using openssl's raw pkeyutl verifier. Aborts the entire install on any
# failure — a bad signature is treated identically to a network error: install stops.
verify_manifest_signature() {
  local manifest="$1" sig="$2"
  if [ ! -f "$MARAN_RELEASE_PUBLIC_KEY_PEM" ]; then
    echo "50-artifacts.sh: release signing public key not found at ${MARAN_RELEASE_PUBLIC_KEY_PEM}" >&2
    echo "  This key ships embedded with the installer package; a bare copy of install.sh" >&2
    echo "  without its accompanying keys/ directory cannot verify releases and MUST NOT be used." >&2
    exit 1
  fi
  if ! openssl pkeyutl -verify -rawin \
      -pubin -inkey "$MARAN_RELEASE_PUBLIC_KEY_PEM" \
      -in "$manifest" -sigfile "$sig" >/dev/null 2>&1; then
    echo "50-artifacts.sh: manifest signature verification FAILED. Aborting install." >&2
    echo "  The release manifest's Ed25519 signature does not match the trusted key." >&2
    echo "  This can mean a corrupted download or a compromised mirror. Do not retry" >&2
    echo "  blindly; verify you are downloading from ${MARAN_RELEASE_BASE_URL} and" >&2
    echo "  contact Innovayse support if the problem persists." >&2
    exit 1
  fi
}

# manifest_field: extracts one field for the current OS arch/component from the JSON
# manifest without requiring jq (not guaranteed present at this point in the install).
# Manifest shape: {"artifacts": {"<component>-<arch>": {"url": "...", "sha256": "..."}}}
# A minimal grep/sed reader is sufficient because the manifest is a small, machine-
# generated, flat structure — not arbitrary attacker-shaped JSON.
manifest_field() {
  local manifest="$1" component="$2" field="$3"
  local key="${component}-${MARAN_ARCH}"
  awk -v key="\"${key}\"" -v field="\"${field}\"" '
    $0 ~ key { in_block=1 }
    in_block && $0 ~ field {
      match($0, /"[^"]*"[[:space:]]*$/)
      val = substr($0, RSTART, RLENGTH)
      gsub(/"/, "", val)
      print val
      exit
    }
    in_block && /}/ { in_block=0 }
  ' "$manifest"
}

# verify_artifact_checksum: the manifest itself is authenticated (Ed25519); each listed
# artifact's sha256 inside that trusted manifest is the second, cheap check that the
# downloaded bytes match exactly what the manifest promised.
verify_artifact_checksum() {
  local file="$1" expected="$2" actual
  actual="$(sha256sum "$file" | awk '{print $1}')"
  if [ "$actual" != "$expected" ]; then
    echo "50-artifacts.sh: checksum mismatch for $(basename "$file"): expected ${expected}, got ${actual}. Aborting." >&2
    exit 1
  fi
}

# download_and_verify_online: the default path — fetch manifest + signature, verify,
# then fetch and checksum-verify each component before any of it is unpacked.
download_and_verify_online() {
  prepare_staging_dir
  local manifest="${MARAN_ARTIFACT_TMP}/manifest.json"
  local sig="${MARAN_ARTIFACT_TMP}/manifest.json.sig"

  fetch "$(manifest_url)" "$manifest"
  fetch "$(manifest_sig_url)" "$sig"
  verify_manifest_signature "$manifest" "$sig"
  echo "Release manifest signature verified."

  local component url checksum dest
  for component in api agent frontend; do
    url="$(manifest_field "$manifest" "$component" "url")"
    checksum="$(manifest_field "$manifest" "$component" "sha256")"
    if [ -z "$url" ] || [ -z "$checksum" ]; then
      echo "50-artifacts.sh: manifest has no entry for ${component}-${MARAN_ARCH}" >&2
      exit 1
    fi
    dest="${MARAN_ARTIFACT_TMP}/${component}.tar.gz"
    fetch "$url" "$dest"
    verify_artifact_checksum "$dest" "$checksum"
    echo "Verified artifact: ${component} (${MARAN_ARCH})"
  done
}

# use_offline_tarball: enterprise path. The operator supplies one tarball that already
# bundles manifest.json + manifest.json.sig + every component archive; it is verified
# with the exact same signature/checksum logic as the online path, never a lesser check.
use_offline_tarball() {
  local bundle="$1"
  if [ ! -f "$bundle" ]; then
    echo "50-artifacts.sh: offline tarball not found: ${bundle}" >&2
    exit 1
  fi
  prepare_staging_dir
  tar -xzf "$bundle" -C "$MARAN_ARTIFACT_TMP"

  local manifest="${MARAN_ARTIFACT_TMP}/manifest.json"
  local sig="${MARAN_ARTIFACT_TMP}/manifest.json.sig"
  if [ ! -f "$manifest" ] || [ ! -f "$sig" ]; then
    echo "50-artifacts.sh: offline bundle is missing manifest.json or manifest.json.sig" >&2
    exit 1
  fi
  verify_manifest_signature "$manifest" "$sig"
  echo "Offline bundle manifest signature verified."

  local component checksum dest
  for component in api agent frontend; do
    checksum="$(manifest_field "$manifest" "$component" "sha256")"
    dest="${MARAN_ARTIFACT_TMP}/${component}.tar.gz"
    if [ ! -f "$dest" ]; then
      echo "50-artifacts.sh: offline bundle is missing ${component}.tar.gz" >&2
      exit 1
    fi
    verify_artifact_checksum "$dest" "$checksum"
    echo "Verified artifact (offline): ${component} (${MARAN_ARCH})"
  done
}

# unpack_artifacts: only reached once every archive has passed signature+checksum
# verification. Unpacks each component into its own subdirectory under the install
# root; nothing here executes downloaded content, it only extracts files.
#
# Each archive's sha256 is checked AGAIN here, immediately before its own extraction, against
# the same Ed25519-signed manifest. The earlier check verifies all three archives in one loop
# and extraction happens afterwards, so the first check authenticates the bytes at check time
# and says nothing about the bytes tar actually reads. The staging directory is root-only, so
# nothing should be able to change them in between — this second check is what turns "should"
# into an observation, and it is the difference between the install ABORTING and root
# extracting a swapped maran-agent that maran-agent.service then runs as root.
unpack_artifacts() {
  local component checksum
  local manifest="${MARAN_ARTIFACT_TMP}/manifest.json"
  for component in api agent frontend; do
    checksum="$(manifest_field "$manifest" "$component" "sha256")"
    if [ -z "$checksum" ]; then
      echo "50-artifacts.sh: no sha256 for ${component}-${MARAN_ARCH} at extraction time. Aborting." >&2
      exit 1
    fi
    verify_artifact_checksum "${MARAN_ARTIFACT_TMP}/${component}.tar.gz" "$checksum"
    install -d -m 0755 "${MARAN_INSTALL_ROOT}/${component}"
    tar -xzf "${MARAN_ARTIFACT_TMP}/${component}.tar.gz" -C "${MARAN_INSTALL_ROOT}/${component}"
  done
  chown -R root:root "$MARAN_INSTALL_ROOT"
  # Only the api binary needs to be runnable by the panel user's systemd unit; the
  # unit itself runs the binary as `panel` while the file stays root-owned (0755 =
  # world-readable+executable, not writable by panel), so a compromised api process
  # cannot modify its own binary on disk.
}

step_artifacts() {
  echo "Fetching and verifying release artifacts (channel: ${MARAN_CHANNEL})..."
  if [ -n "${MARAN_OFFLINE_TARBALL}" ]; then
    use_offline_tarball "$MARAN_OFFLINE_TARBALL"
  else
    download_and_verify_online
  fi
  unpack_artifacts
  rm -rf "$MARAN_ARTIFACT_TMP"
  echo "Artifacts installed under ${MARAN_INSTALL_ROOT}."
}
