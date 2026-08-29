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
readonly MARAN_ARTIFACT_TMP="/var/lib/maran/artifact-staging"

# MARAN_RELEASE_PUBLIC_KEY_PEM: the Ed25519 public key that signs release manifests,
# baked into the installer itself (never fetched at install time — an attacker who can
# serve the installer AND a fake manifest would otherwise control both halves of the
# trust check). This is a placeholder key ID; the real key ships from Innovayse's
# release signing infrastructure and is substituted into this file at release-build time.
readonly MARAN_RELEASE_PUBLIC_KEY_PEM="/usr/local/maran/keys/release-signing.pub"

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
    echo "  blindly; verify you are downloading from https://get.maran.com and" >&2
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
  mkdir -p "$MARAN_ARTIFACT_TMP"
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
  mkdir -p "$MARAN_ARTIFACT_TMP"
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
unpack_artifacts() {
  local component
  for component in api agent frontend; do
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
