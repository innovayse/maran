#!/usr/bin/env bash
# Offline packaging for `maran release package`: one verified tarball per architecture.
#
# SOURCED by scripts/lib/release-bundle.sh, which supplies the helpers used below (release_failed,
# release_ok, require_tool, sha256_of, arch_name, verify_release_file) and the VERDICT_PREFIX. It
# lives beside it rather than inside it because that file passed the 400-line review trigger
# (rules/architecture.md) and packaging is a separate unit with its own selftest.
#
# WHY IT EXISTS (issue #310). A two-architecture build (`maran release build --arch both`) stages
# `<component>-<arch>.tar.gz` for each architecture and signs ONE manifest holding both. The offline
# installer path (`step_artifacts` in installer/lib/50-artifacts.sh) is shaped differently: it
# reads the key `<component>-${MARAN_ARCH}` from the manifest but opens the UNSUFFIXED
# `<component>.tar.gz`, because an offline bundle is carried to one machine. The previous packager
# tarred the unsuffixed names, which a two-architecture build never writes, so it failed and the
# bundles had to be assembled by hand.
#
# WHAT IT DOES NOW. For each requested architecture it copies that architecture's tarballs to the
# unsuffixed names beside the ALREADY SIGNED manifest, tars the result, then extracts what it just
# wrote into a fresh directory and verifies THAT with the installer's own checks. The manifest and
# its signature are not rewritten, so the bundle verifies against the production key without this
# script ever holding the private half (rules/security.md #8): the other architecture's entries stay
# in the manifest, unused by the installer, and are still covered by the signature.

# Where a component's entry file must sit inside its tarball, at the archive ROOT (the installer
# and `maran update` unpack each archive into /usr/local/maran/releases/<version>/<component>
# through cli_layout_install_release, installer/cli/lib/release-layout.sh, after
# installer/cli/lib/release-verify.sh has verified it). Pinned by the systemd units
# and nginx vhost named in release-bundle.sh's header; a tarball missing its entry file checksums
# perfectly and still starts nothing on the server.
package_entry_file() {
  case "$1" in
    api) echo "Maran.Host" ;;
    agent) echo "maran-agent" ;;
    frontend) echo "index.html" ;;
    *) echo "" ;;
  esac
}

# manifest_version: <manifest.json>. The "version" string, restricted to characters that are safe in
# a file name, because it becomes part of one.
manifest_version() {
  local version
  version="$(sed -n 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$1" | head -1)"
  case "$version" in
    ""|*[!A-Za-z0-9._+-]*) return 1 ;;
  esac
  printf '%s' "$version"
}

# manifest_channel: <manifest.json>. The signed top-level "channel" string (stable or beta), or
# nothing when the manifest has none. The pattern is anchored to the start of the line, where the
# builder writes it between "version" and "artifacts"; the installer's awk reader keys on
# `"<component>-<arch>"` blocks and never looks at this line, so the field is additive.
manifest_channel() {
  sed -n 's/^[[:space:]]*"channel"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$1" | head -1
}

# manifest_url_channels: <manifest.json>. The first path segment of every artifact "url", one per
# line (https://<host>/<channel>/<component>-<arch>.tar.gz). The builder writes the channel into
# every URL, so these are the channel the bundle was built FOR, independent of the "channel" field.
manifest_url_channels() {
  sed -n 's#^[[:space:]]*"url"[[:space:]]*:[[:space:]]*"https://[^/"]*/\([^/"]*\)/.*#\1#p' "$1"
}

# check_manifest_channel: <manifest.json>. Returns 1 naming the problem when the manifest has no
# "channel" field (a bundle built before the field existed cannot be told apart from one whose field
# was stripped, so it is refused), when the field is neither stable nor beta, or when any artifact
# url names a different channel than the field. Run only AFTER the signature has verified, so the
# field it reads is the signed one; a tampered field is the signature's refusal, not this one's.
check_manifest_channel() {
  local manifest="$1" channel url_channels
  channel="$(manifest_channel "$manifest")"
  case "$channel" in
    stable|beta) ;;
    "")
      echo "verify: ${manifest##*/} carries no \"channel\" field; rebuild it with: maran release build" >&2
      return 1
      ;;
    *)
      echo "verify: ${manifest##*/} names channel '${channel}', which is neither stable nor beta" >&2
      return 1
      ;;
  esac
  url_channels="$(manifest_url_channels "$manifest" | sort -u)"
  if [ "$url_channels" != "$channel" ]; then
    echo "verify: ${manifest##*/} says channel '${channel}' but its artifact urls name" \
      "'${url_channels//$'\n'/ }'; the bundle was not built for one channel" >&2
    return 1
  fi
  return 0
}

# manifest_arches: <manifest.json>. Each architecture the manifest holds all three components for,
# one per line. A half-present architecture is not listed: it cannot be installed, so it must not be
# packaged as if it could.
manifest_arches() {
  local arch component complete
  for arch in $MARAN_PUBLISH_ARCHES; do
    complete=1
    for component in $MARAN_COMPONENTS; do
      grep -qE "^[[:space:]]*\"${component}-${arch}\"[[:space:]]*:" "$1" || complete=0
    done
    [ "$complete" -eq 1 ] && echo "$arch"
  done
  return 0
}

# manifest_sha256: <manifest.json> <component> <arch>. A small awk reader for the one field. It is
# NOT a copy of the installer's reader (cli_release_manifest_field in
# installer/cli/lib/release-verify.sh, which tracks brace depth), so it is a reader of a different
# shape that could agree with this script and disagree with the installer; the selftest's accepted
# bundle is what shows the two agree on a manifest this build writes
# (rules/testing.md: observe what the system actually uses).
manifest_sha256() {
  awk -v key="\"${2}-${3}\"" -v field="\"sha256\"" '
    $0 ~ key { in_block=1 }
    in_block && $0 ~ field {
      match($0, /"[^"]*"[[:space:]]*,?[[:space:]]*$/)
      val = substr($0, RSTART, RLENGTH)
      gsub(/[",[:space:]]/, "", val)
      print val
      exit
    }
    in_block && /}/ { in_block=0 }
  ' "$1"
}

# verify_offline_tarball: <tarball> <arch> <public key>. Extracts the tarball into a fresh directory
# and repeats what cli_release_verify_offline (installer/cli/lib/release-verify.sh) and
# cli_layout_install_release (installer/cli/lib/release-layout.sh) do for MARAN_ARCH=<arch>:
# signature over manifest.json, the signed channel field (present, stable or beta, and the one
# every artifact url names), signature over integrity-manifest.json (the code-integrity hash
# list ships in the same bundle, so an unsigned or altered one must be refused here and not
# discovered on a server),
# then for api, agent and frontend the sha256 of the UNSUFFIXED archive against the
# `<component>-<arch>` manifest entry, then the entry file at each archive's root. Returns 1 naming
# the first failure. Reads the artefact that will be shipped, not the staging directory it came from.
verify_offline_tarball() {
  local tarball="$1" arch="$2" pubkey="$3" dir component expected actual entry rc=0
  [ -f "$pubkey" ] || { echo "verify: public key not found at ${pubkey}" >&2; return 1; }
  dir="$(mktemp -d)"
  tar -xzf "$tarball" -C "$dir" || { echo "verify: ${tarball} is not a readable tar.gz" >&2; rm -rf -- "$dir"; return 1; }
  if [ ! -f "${dir}/manifest.json" ] || [ ! -f "${dir}/manifest.json.sig" ]; then
    echo "verify: ${tarball##*/} is missing manifest.json or manifest.json.sig" >&2; rc=1
  elif ! verify_release_file "$pubkey" "${dir}/manifest.json" "${dir}/manifest.json.sig"; then
    echo "verify: manifest signature in ${tarball##*/} does NOT verify against ${pubkey}" >&2; rc=1
  elif ! check_manifest_channel "${dir}/manifest.json"; then
    echo "verify: manifest channel check failed for ${tarball##*/}" >&2; rc=1
  elif [ ! -f "${dir}/integrity-manifest.json" ] || [ ! -f "${dir}/integrity-manifest.json.sig" ]; then
    echo "verify: ${tarball##*/} is missing integrity-manifest.json or integrity-manifest.json.sig" >&2; rc=1
  elif ! verify_release_file "$pubkey" "${dir}/integrity-manifest.json" "${dir}/integrity-manifest.json.sig"; then
    echo "verify: integrity manifest signature in ${tarball##*/} does NOT verify against ${pubkey}" >&2; rc=1
  else
    for component in $MARAN_COMPONENTS; do
      if [ ! -f "${dir}/${component}.tar.gz" ]; then
        echo "verify: ${tarball##*/} is missing ${component}.tar.gz" >&2; rc=1; break
      fi
      expected="$(manifest_sha256 "${dir}/manifest.json" "$component" "$arch")"
      actual="$(sha256_of "${dir}/${component}.tar.gz")"
      if [ -z "$expected" ] || [ "$actual" != "$expected" ]; then
        echo "verify: ${component}.tar.gz in ${tarball##*/} does not match manifest entry ${component}-${arch} (expected '${expected}', got ${actual})" >&2; rc=1; break
      fi
      entry="$(package_entry_file "$component")"
      if ! tar -tzf "${dir}/${component}.tar.gz" | sed 's#^\./##' | grep -qxF -- "$entry"; then
        echo "verify: ${component}.tar.gz holds no ${entry} at its root, so the installer would unpack nothing runnable" >&2; rc=1; break
      fi
    done
  fi
  rm -rf -- "$dir"
  return "$rc"
}

# package_one_arch: <bundle dir> <arch> <out file> <public key>. Stages, tars, verifies; on any
# failure removes the output so a refused bundle is never left looking shippable.
package_one_arch() {
  local bundle_dir="$1" arch="$2" out_file="$3" pubkey="$4" stage component src
  stage="$(mktemp -d)"
  cp -- "${bundle_dir}/manifest.json" "${bundle_dir}/manifest.json.sig" \
        "${bundle_dir}/integrity-manifest.json" "${bundle_dir}/integrity-manifest.json.sig" "$stage/"
  for component in $MARAN_COMPONENTS; do
    src="${bundle_dir}/${component}-${arch}.tar.gz"
    [ -f "$src" ] || { rm -rf -- "$stage"; release_failed "${bundle_dir} has no ${component}-${arch}.tar.gz — build it with: maran release build --arch ${arch}"; }
    cp -- "$src" "${stage}/${component}.tar.gz"
  done
  install -d -- "$(dirname "$out_file")"
  ( cd "$stage" && tar -czf "$out_file" \
      manifest.json manifest.json.sig integrity-manifest.json integrity-manifest.json.sig \
      api.tar.gz agent.tar.gz frontend.tar.gz ) \
    || { rm -rf -- "$stage" "$out_file"; release_failed "could not write ${out_file}"; }
  rm -rf -- "$stage"
  if ! verify_offline_tarball "$out_file" "$arch" "$pubkey"; then
    rm -f -- "$out_file"
    release_failed "the ${arch} offline bundle did not verify, so it was removed (reason printed above)."
  fi
  echo "Packaged and verified offline bundle (${arch}): ${out_file}"
}

# cmd_package: see the file header. --arch defaults to every architecture the manifest holds, so
# `build --arch both` then `package` yields both bundles with no further decision.
cmd_package() {
  local bundle_dir="/tmp/maran-release-bundle" out="" arch_list="" pubkey="$root/installer/keys/release-signing.pub"
  while [ $# -gt 0 ]; do
    case "$1" in
      --bundle) bundle_dir="${2:?--bundle requires a value}"; shift 2 ;;
      --out) out="${2:?--out requires a value}"; shift 2 ;;
      --pubkey) pubkey="${2:?--pubkey requires a value}"; shift 2 ;;
      --arch)
        arch_list="${2:?--arch requires a value}"
        [ "$arch_list" != "both" ] || arch_list="x86_64,aarch64"
        shift 2
        ;;
      *) echo "release package: unknown argument: $1" >&2; usage ;;
    esac
  done
  require_tool openssl "verifies each bundle it packages"
  require_tool tar "writes the bundles"
  [ -f "${bundle_dir}/manifest.json.sig" ] || release_failed "${bundle_dir} has no manifest.json.sig — sign it first: maran release sign"
  [ -f "${bundle_dir}/integrity-manifest.json" ] || release_failed "${bundle_dir} has no integrity-manifest.json — run: maran release build (this bundle predates the code-integrity hash list)"
  [ -f "${bundle_dir}/integrity-manifest.json.sig" ] || release_failed "${bundle_dir} has no integrity-manifest.json.sig — sign it first: maran release sign"

  local arches arch available
  available="$(manifest_arches "${bundle_dir}/manifest.json" | tr '\n' ' ')"
  [ -n "${available// /}" ] || release_failed "${bundle_dir}/manifest.json holds no architecture with all of api, agent and frontend"
  if [ -z "$arch_list" ]; then
    arches="$available"
  else
    arches=""
    for arch in ${arch_list//,/ }; do
      arch="$(arch_name "$arch")"
      case " ${available} " in
        *" ${arch} "*) arches="${arches} ${arch}" ;;
        *) release_failed "architecture '${arch}' is not complete in ${bundle_dir}/manifest.json (it holds: ${available})" ;;
      esac
    done
  fi

  local version
  version="$(manifest_version "${bundle_dir}/manifest.json")" \
    || release_failed "${bundle_dir}/manifest.json has no usable \"version\" (letters, digits and . _ + - only)"

  # --out is a directory unless it names a single .tar.gz, which keeps `--out PATH` working for the
  # single-architecture callers (scripts/lib/install-polygon.sh) and refuses the ambiguous case of
  # one file name for two architectures.
  local out_file out_dir count=0 produced=""
  for arch in $arches; do count=$((count + 1)); done
  case "$out" in
    *.tar.gz)
      [ "$count" -eq 1 ] || release_failed "--out ${out} names one file but ${count} architectures were asked for (${arches# }); give --out a directory, or pick one with --arch"
      package_one_arch "$bundle_dir" "${arches# }" "$out" "$pubkey"
      produced="$out"
      ;;
    *)
      out_dir="${out:-${bundle_dir%/}-offline}"
      for arch in $arches; do
        out_file="${out_dir}/maran-offline-${version}-${arch}.tar.gz"
        package_one_arch "$bundle_dir" "$arch" "$out_file" "$pubkey"
        produced="${produced} ${out_file}"
      done
      ;;
  esac
  release_ok "${count} offline bundle(s) verified against ${pubkey}:${produced:+ ${produced# }}"
  echo "Each is the file installer/install.sh --offline-tarball <path> expects on a machine of that architecture."
}
