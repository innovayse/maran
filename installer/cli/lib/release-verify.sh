#!/usr/bin/env bash
# release-verify.sh: how `maran update` obtains a release and decides to trust it. Fetch the signed
# manifest, verify its ECDSA P-384 / SHA-384 signature, read fields ONLY out of the verified file,
# fetch the artifacts and check each sha256 against that file. The online path unpacks nothing. The
# offline path (cli_release_verify_offline) copies the operator's bundle into the root work directory
# once, vets that copy with release-unpack.sh and extracts only the copy; it still unpacks nothing
# into a release tree, only the bundle's own manifest, signature and component archives.
#
# Version syntax and ordering live in release-version.sh (cli_version_*); this file defines no
# version pattern. Sourced by the `maran` wrapper after common.sh, release-version.sh and
# release-unpack.sh; never executed on its own. The trust chain and its limits are argued in
# docs/superpowers/notes/2026-11-04-panel-self-update-threat-note.md.

# The public key that signs release manifests, installed by installer/lib/66-cli.sh (root 0644 in a
# root 0755 directory). A constant default: no environment variable replaces it. The only way to
# name another key is the optional last KEYFILE argument of cli_release_verify_manifest,
# cli_release_fetch_manifest and cli_release_verify_offline, which the installer uses to pass the key
# inside its own package (a first install runs before step 66 has installed the CLI key); the
# `maran` verbs never pass one.
readonly MARAN_RELEASE_PUBLIC_KEY="/usr/local/maran/cli/keys/release-signing.pub"
# The release host. Also a constant, and https only; the channel is the first path segment.
readonly MARAN_RELEASE_BASE_URL="https://releases.maran.innovayse.com"
# The components a release carries for one architecture.
readonly MARAN_RELEASE_COMPONENTS="api agent frontend"
# Deadlines and size ceilings: a stalled or endless answer fails instead of holding the lock. The
# three artifacts together may not exceed MARAN_RELEASE_TOTAL_MAX_BYTES, which equals the 2 GiB
# free-space floor update.sh requires on the staging filesystem before any download (the manifest
# carries no sizes), so a download can never be what fills the disk. A single file is further
# capped at MARAN_RELEASE_ARTIFACT_MAX_BYTES. The offline bundle holds all three, so it shares the
# total.
readonly MARAN_RELEASE_MANIFEST_MAX_SECONDS=60
readonly MARAN_RELEASE_MANIFEST_MAX_BYTES=1048576
readonly MARAN_RELEASE_ARTIFACT_MAX_SECONDS=1800
readonly MARAN_RELEASE_ARTIFACT_MAX_BYTES=1073741824
readonly MARAN_RELEASE_TOTAL_MAX_BYTES=$((2 * 1024 * 1024 * 1024))
# Where the offline bundle's private copy lives inside the work directory.
readonly MARAN_RELEASE_OFFLINE_COPY_NAME="offline-bundle.tar.gz"

# cli_release_arch: prints the artifact architecture name of this machine (x86_64 or aarch64), or
# refuses on any other.
cli_release_arch() {
  case "$(uname -m)" in
    x86_64) printf 'x86_64\n' ;;
    aarch64 | arm64) printf 'aarch64\n' ;;
    *) cli_fail "unsupported architecture: $(uname -m)" ;;
  esac
}

# cli_release_manifest_field MANIFEST KEY: prints one value read from a manifest, or nothing when it
# is absent (the caller decides whether absence is an error). KEY is `version`, `channel`, or
# `<component>-<arch>.url` / `<component>-<arch>.sha256`. No jq: the manifest is small, machine
# written and pretty printed, and brace depth keeps a top-level key apart from one inside an
# artifact. Call it only on a manifest cli_release_verify_manifest has accepted.
cli_release_manifest_field() {
  local manifest="$1" key="$2" name field
  [[ "$key" =~ ^(version|channel|[a-z]+-[a-z0-9_]+\.(url|sha256))$ ]] \
    || cli_fail "internal error: bad manifest key '${key}'"
  name="${key%.*}"
  field="${key##*.}"
  awk -v name="\"${name}\"" -v field="\"${field}\"" -v nested="$([[ "$key" == *.* ]] && echo 1 || echo 0)" '
    function emit(   v) {
      if (match($0, /:[[:space:]]*"[^"]*"/)) {
        v = substr($0, RSTART, RLENGTH); sub(/^:[[:space:]]*"/, "", v); sub(/"$/, "", v); print v
      }
      exit
    }
    nested == 0 && depth == 1 && $0 ~ ("^[[:space:]]*" name "[[:space:]]*:") { emit() }
    nested == 1 && depth == 2 { in_block = ($0 ~ ("^[[:space:]]*" name "[[:space:]]*:")) }
    nested == 1 && depth == 3 && in_block && $0 ~ ("^[[:space:]]*" field "[[:space:]]*:") { emit() }
    { opens = gsub(/{/, "{"); closes = gsub(/}/, "}"); depth += opens - closes }
  ' "$manifest"
}

# cli_release_require_work_dir DIR: refuses unless DIR is an absolute path naming a real directory
# (not a symlink) owned by root with mode 0700. The caller makes it; this function checks it,
# because downloads land here and anything another uid can write could swap bytes between the
# check and the use (the gate installer/lib/50-artifacts.sh applies to its own staging directory).
cli_release_require_work_dir() {
  local dir="$1"
  [[ "$dir" =~ ^/[A-Za-z0-9._/-]*$ ]] && [[ "$dir" != *..* ]] \
    || cli_fail "work directory must be an absolute path of plain characters"
  [ -d "$dir" ] && [ ! -L "$dir" ] || cli_fail "work directory ${dir} is not a real directory"
  [ "$(stat -c '%u %a' -- "$dir")" = "0 700" ] \
    || cli_fail "work directory ${dir} must be owned by root with mode 0700"
}

# cli_release_download URL DEST MAX_SECONDS MAX_BYTES: one HTTPS GET into DEST and nothing else. No
# config file (--disable), https only for the request and every redirect (at most 3), TLS 1.2 or
# newer, certificate checks on, --fail so an error page is never saved, a connect timeout, a total
# deadline and a size ceiling. curl's --max-filesize is only advisory when the server does not state
# a length, so the size actually written is measured afterwards and an over-size file is deleted and
# refused. The request carries no query string and no identifier of this host or panel.
cli_release_download() {
  local url="$1" dest="$2" seconds="$3" bytes="$4" size
  rm -f -- "${dest:?}"
  curl --disable --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
    --tlsv1.2 --max-redirs 3 --connect-timeout 15 --max-time "$seconds" --max-filesize "$bytes" \
    --output "$dest" --url "$url" \
    || { rm -f -- "${dest:?}"; cli_fail "download failed: ${url}"; }
  size="$(stat -c '%s' -- "$dest")" || cli_fail "cannot measure the download of ${url}"
  [ "$size" -le "$bytes" ] || { rm -f -- "${dest:?}"; cli_fail "download of ${url} exceeds ${bytes} bytes; refusing it"; }
}

# cli_release_url_under_channel CHANNEL URL: succeeds when URL is an artifact archive directly under
# https://releases.maran.innovayse.com/<CHANNEL>/ and nothing else.
cli_release_url_under_channel() {
  local prefix="${MARAN_RELEASE_BASE_URL}/${1}/"
  [[ "$2" == "$prefix"* ]] && [[ "${2#"$prefix"}" =~ ^[A-Za-z0-9._-]+\.tar\.gz$ ]]
}

# cli_release_check_sha256 FILE EXPECTED: refuses unless FILE's sha256 equals EXPECTED, a 64 digit
# lowercase hex digest taken from a verified manifest. Also the check run again before unpacking.
cli_release_check_sha256() {
  local file="$1" expected="$2" actual
  [[ "$expected" =~ ^[0-9a-f]{64}$ ]] || cli_fail "the manifest holds no valid sha256 for $(basename "$file")"
  [ -f "$file" ] && [ ! -L "$file" ] || cli_fail "$(basename "$file") is missing or not a regular file"
  actual="$(sha256sum -- "$file" | awk '{print $1}')"
  [ "$actual" = "$expected" ] \
    || cli_fail "checksum mismatch for $(basename "$file"): expected ${expected}, got ${actual}"
}

# cli_release_verify_manifest DIR [KEYFILE]: verifies DIR/manifest.json against DIR/manifest.json.sig
# with KEYFILE (default: the baked MARAN_RELEASE_PUBLIC_KEY; `openssl dgst -sha384 -verify`, as the
# installer does) BEFORE any field is read, then requires a strict SemVer version
# (cli_version_is_semver), and, when the signed channel is present, that it is stable or beta and
# that every artifact url sits under it. A missing channel is accepted here (an old offline bundle)
# and refused by cli_release_fetch_manifest, which is the network path.
cli_release_verify_manifest() {
  local dir="$1" keyfile="${2:-$MARAN_RELEASE_PUBLIC_KEY}" manifest sig version channel url
  manifest="${dir}/manifest.json"; sig="${dir}/manifest.json.sig"
  cli_release_require_work_dir "$dir"
  [ -f "$keyfile" ] && [ ! -L "$keyfile" ] \
    || cli_fail "release signing key ${keyfile} is missing; is the CLI installed?"
  [ -f "$manifest" ] && [ -f "$sig" ] || cli_fail "manifest.json or manifest.json.sig is missing"
  openssl dgst -sha384 -verify "$keyfile" -signature "$sig" "$manifest" >/dev/null 2>&1 \
    || cli_fail "manifest signature verification FAILED; the release is not trusted"
  version="$(cli_release_manifest_field "$manifest" version)"
  cli_version_is_semver "$version" || cli_fail "the manifest carries no valid version"
  channel="$(cli_release_manifest_field "$manifest" channel)"
  [ -n "$channel" ] || return 0
  [[ "$channel" == "stable" || "$channel" == "beta" ]] || cli_fail "the manifest names an unknown channel"
  while IFS= read -r url; do
    cli_release_url_under_channel "$channel" "$url" \
      || cli_fail "the manifest lists an artifact outside the ${channel} channel: ${url}"
  done < <(awk -F'"' '/^[[:space:]]*"url"[[:space:]]*:/ { print $4 }' "$manifest")
}

# cli_release_fetch_manifest CHANNEL DIR [KEYFILE]: downloads manifest.json and manifest.json.sig for
# CHANNEL (stable or beta) into DIR, verifies them with KEYFILE (default: the baked
# MARAN_RELEASE_PUBLIC_KEY), and refuses a manifest with no signed channel or one that differs from
# CHANNEL, so a beta manifest replayed to a stable request is not accepted.
cli_release_fetch_manifest() {
  local channel="$1" dir="$2" keyfile="${3:-$MARAN_RELEASE_PUBLIC_KEY}" signed
  [[ "$channel" == "stable" || "$channel" == "beta" ]] || cli_fail "channel must be stable or beta"
  cli_release_require_work_dir "$dir"
  rm -f -- "${dir}/manifest.json" "${dir}/manifest.json.sig"
  cli_release_download "${MARAN_RELEASE_BASE_URL}/${channel}/manifest.json" "${dir}/manifest.json" \
    "$MARAN_RELEASE_MANIFEST_MAX_SECONDS" "$MARAN_RELEASE_MANIFEST_MAX_BYTES"
  cli_release_download "${MARAN_RELEASE_BASE_URL}/${channel}/manifest.json.sig" "${dir}/manifest.json.sig" \
    "$MARAN_RELEASE_MANIFEST_MAX_SECONDS" "$MARAN_RELEASE_MANIFEST_MAX_BYTES"
  cli_release_verify_manifest "$dir" "$keyfile"
  signed="$(cli_release_manifest_field "${dir}/manifest.json" channel)"
  [ -n "$signed" ] || cli_fail "the manifest has no channel field; it predates self-update and is not accepted from the network"
  [ "$signed" = "$channel" ] || cli_fail "the manifest is signed for channel ${signed}, not the requested ${channel}"
}

# cli_release_fetch_artifacts MANIFEST DIR: downloads api, agent and frontend for this machine's
# architecture into DIR as <component>.tar.gz and checks each sha256 against the VERIFIED manifest.
# MANIFEST must have passed cli_release_verify_manifest; each url is re-checked against its channel.
# The cap is on the SUM: each download may use only what is left of MARAN_RELEASE_TOTAL_MAX_BYTES
# after the earlier ones (and at most MARAN_RELEASE_ARTIFACT_MAX_BYTES), so the three together can
# never outgrow the free-space floor.
cli_release_fetch_artifacts() {
  local manifest="$1" dir="$2" arch channel component url used=0 remaining cap
  cli_release_require_work_dir "$dir"
  arch="$(cli_release_arch)"
  channel="$(cli_release_manifest_field "$manifest" channel)"
  [ -n "$channel" ] || cli_fail "the manifest has no channel field"
  for component in $MARAN_RELEASE_COMPONENTS; do
    url="$(cli_release_manifest_field "$manifest" "${component}-${arch}.url")"
    [ -n "$url" ] || cli_fail "this release has no ${component} artifact for ${arch}"
    cli_release_url_under_channel "$channel" "$url" || cli_fail "refusing the artifact url ${url}"
    remaining=$((MARAN_RELEASE_TOTAL_MAX_BYTES - used))
    [ "$remaining" -gt 0 ] || cli_fail "the release artifacts together exceed ${MARAN_RELEASE_TOTAL_MAX_BYTES} bytes"
    cap=$((remaining < MARAN_RELEASE_ARTIFACT_MAX_BYTES ? remaining : MARAN_RELEASE_ARTIFACT_MAX_BYTES))
    cli_release_download "$url" "${dir}/${component}.tar.gz" "$MARAN_RELEASE_ARTIFACT_MAX_SECONDS" "$cap"
    used=$((used + $(stat -c '%s' -- "${dir}/${component}.tar.gz")))
    cli_release_check_sha256 "${dir}/${component}.tar.gz" \
      "$(cli_release_manifest_field "$manifest" "${component}-${arch}.sha256")"
  done
}

# cli_release_require_trusted_parent DIR: refuses unless DIR is owned by root and neither group nor
# world writable. The offline tarball is read from there; if another uid could write the directory it
# could swap or replace the file between the operator's look and the copy.
cli_release_require_trusted_parent() {
  local dir="$1" owner mode
  read -r owner mode < <(stat -c '%u %a' -- "$dir") || cli_fail "cannot inspect ${dir}"
  [ "$owner" = "0" ] || cli_fail "the directory ${dir} holding the offline tarball must be owned by root"
  (( (8#$mode & 8#022) == 0 )) \
    || cli_fail "the directory ${dir} holding the offline tarball must not be group or world writable"
}

# cli_release_verify_offline TARBALL DIR [KEYFILE]: the call-free path. TARBALL's directory must be root-owned
# and not group/world writable and the file no larger than MARAN_RELEASE_TOTAL_MAX_BYTES. The file is
# copied ONCE into the root-0700 work directory DIR (a single open, mode 0600); from then on only the
# copy is read: cli_unpack_vet_members (release-unpack.sh) refuses any unsafe member, the copy is
# extracted into DIR, then the manifest signature and each <component>.tar.gz against the manifest
# entry `<component>-<arch>` are verified exactly as the online path does, with KEYFILE (default: the
# baked MARAN_RELEASE_PUBLIC_KEY). The copy is deleted.
cli_release_verify_offline() {
  local tarball="$1" dir="$2" keyfile="${3:-$MARAN_RELEASE_PUBLIC_KEY}" parent copy arch component size
  cli_release_require_work_dir "$dir"
  [ -f "$tarball" ] && [ ! -L "$tarball" ] || cli_fail "offline tarball not found: ${tarball}"
  parent="$(cd -P -- "$(dirname -- "$tarball")" && pwd -P)" || cli_fail "cannot resolve the offline tarball directory"
  cli_release_require_trusted_parent "$parent"
  size="$(stat -c '%s' -- "$tarball")" || cli_fail "cannot inspect the offline tarball"
  [ "$size" -le "$MARAN_RELEASE_TOTAL_MAX_BYTES" ] \
    || cli_fail "the offline tarball is larger than ${MARAN_RELEASE_TOTAL_MAX_BYTES} bytes; refusing it"
  copy="${dir}/${MARAN_RELEASE_OFFLINE_COPY_NAME}"
  rm -f -- "${copy:?}" "${dir:?}/manifest.json" "${dir:?}/manifest.json.sig"
  for component in $MARAN_RELEASE_COMPONENTS; do rm -f -- "${dir:?}/${component:?}.tar.gz"; done
  install -m 0600 -o root -g root -- "$tarball" "$copy" || cli_fail "cannot copy the offline tarball"
  # Tested explicitly: both return non-zero (message on stderr) instead of calling cli_fail, and a
  # caller that runs this inside `if` or `||` has errexit off, where a bare call would be ignored.
  cli_unpack_vet_members "$copy" || cli_fail "the offline tarball was refused"
  cli_unpack_into "$copy" "$dir" || cli_fail "the offline tarball could not be unpacked"
  # A tarball built with `tar -czf x -C dir .` carries a leading `./` member, and GNU tar applies that
  # member's mode to the directory it extracts into, which would loosen the 0700 work directory.
  chmod 0700 -- "$dir" || cli_fail "cannot restore the mode of ${dir}"
  rm -f -- "${copy:?}"
  cli_release_verify_manifest "$dir" "$keyfile"
  arch="$(cli_release_arch)"
  for component in $MARAN_RELEASE_COMPONENTS; do
    cli_release_check_sha256 "${dir}/${component}.tar.gz" \
      "$(cli_release_manifest_field "${dir}/manifest.json" "${component}-${arch}.sha256")"
  done
}
