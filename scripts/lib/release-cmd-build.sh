#!/usr/bin/env bash
# `maran release build`: cmd_build, which assembles the bundle from the component builders.
#
# SOURCED by scripts/lib/release-bundle.sh (split out of it, issue #351), which has already set
# `-euo pipefail`. Only definitions here: nothing runs when this file is sourced.

cmd_build() {
  local version="" out_dir="" channel="" arch_list="" publish=0 notes_url="" security=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --version) version="${2:?--version requires a value}"; shift 2 ;;
      --security) security=1; shift ;;
      --channel) channel="${2:?--channel requires a value}"; shift 2 ;;
      --notes-url) notes_url="${2:?--notes-url requires a value}"; shift 2 ;;
      --out) out_dir="${2:?--out requires a value}"; shift 2 ;;
      --publish) publish=1; shift ;;
      # The flag sets the variable the environment would, and wins over it (one value, one source).
      --source-date-epoch) SOURCE_DATE_EPOCH="${2:?--source-date-epoch requires a value}"; export SOURCE_DATE_EPOCH; shift 2 ;;
      # --arch: a comma-separated list, or `both` for the pair a published release needs. Defaults
      # to the host's architecture further down, so an unadorned build is unchanged (issue #53).
      --arch)
        arch_list="${2:?--arch requires a value}"
        [ "$arch_list" != "both" ] || arch_list="x86_64,aarch64"
        shift 2
        ;;
      *) echo "release build: unknown argument: $1" >&2; usage ;;
    esac
  done
  [ "${SOURCE_DATE_EPOCH+x}" != "x" ] || release_check_source_date_epoch "$SOURCE_DATE_EPOCH"
  [ -n "$version" ] || version="0.0.0-dev-$(date -u +%Y%m%dT%H%M%SZ)"
  # The channel is part of every artifact URL the manifest publishes, and the manifest is what the
  # signature covers — so a bundle built for one channel and served from another is a bundle whose
  # own URLs point somewhere it is not. It was hardcoded `stable`, which would have published the
  # first beta under the word an operator reads as "ready".
  [ -n "$channel" ] || channel="stable"
  case "$channel" in
    stable|beta) ;;
    *) echo "release build: --channel must be stable or beta, got: ${channel}" >&2; exit 2 ;;
  esac
  [ -n "$out_dir" ] || out_dir="/tmp/maran-release-bundle"
  # The release notes live on the GitHub release the tagged workflow creates for v<version>.
  [ -n "$notes_url" ] || notes_url="https://github.com/innovayse/maran/releases/tag/v${version}"
  manifest_release_fields "$notes_url" >/dev/null

  # A published build owes both architectures, refused HERE, before the tree lock and before any
  # toolchain runs: the omission costs an ARM64 operator a failed install, and finding it after an
  # hour of builds would be the expensive place to learn it. --channel cannot carry this meaning
  # because it always has a value (stable by default), so every local build would count as published.
  if [ "$publish" -eq 1 ]; then
    local requested_arches
    requested_arches="$(for a in ${arch_list//,/ }; do arch_name "$a"; done | sort -u | tr '\n' ' ')"
    [ "$requested_arches" = "aarch64 x86_64 " ] \
      || release_failed "--publish requires --arch both (x86_64 AND aarch64); got '${arch_list:-the host default}'. A release without both leaves one architecture's operators nothing to install (issue #53)."
  fi

  tree_lock_acquire "$root" 0 "release build" \
    || { suite_did_not_run "$VERDICT_PREFIX" "the tree lock is held (named above) — retry once the other lane finishes."; exit "$SUITE_STATUS_DID_NOT_RUN"; }
  trap tree_lock_release EXIT INT TERM

  rm -rf -- "$out_dir"
  install -d "$out_dir"
  # Absolute, because build_agent mounts "${out_dir}/.agent-build" with docker -v, which refuses a relative path.
  out_dir="$(cd "$out_dir" && pwd)"

  # The architectures this bundle is for. Defaults to the host's, so a plain `maran release build`
  # behaves exactly as it did; `--arch x86_64,aarch64` builds the publishable pair (issue #53).
  #
  # preflight has accepted aarch64 from the beginning and its refusal text promised "x86_64 and
  # aarch64 artifacts only" while the release produced one, so an ARM64 operator passed the gate and
  # then found nothing to install. Building both is what makes that text true.
  local arch_list="${arch_list:-$(arch_name)}"
  local arch
  for arch in ${arch_list//,/ }; do
    [ "$(arch_name "$arch")" != "unsupported" ] \
      || release_failed "'${arch}' is not a MARAN_ARCH this installer recognizes (installer/install.sh:310-312)"
  done

  local component
  for arch in ${arch_list//,/ }; do
    arch="$(arch_name "$arch")"
    echo "== building for ${arch}"
    build_api "$out_dir" "$arch" "$version" "$publish"
    build_agent "$out_dir" "$arch"
    build_frontend "$out_dir" "$arch"
    for component in api agent frontend; do
      tar_component "${out_dir}/${component}-${arch}" "${out_dir}/${component}-${arch}.tar.gz"
    done
  done

  # The unsuffixed names the OFFLINE path reads. installer/lib/50-artifacts.sh's offline branch opens
  # `<component>.tar.gz` with no architecture in the name — an offline bundle is carried to one
  # machine, so it holds one architecture — while the online manifest points at the suffixed URLs.
  # Written only for a single-architecture build, because a two-architecture bundle has no single
  # right answer and silently picking one would produce an offline bundle for the wrong machine.
  local single_arch=""
  case "$arch_list" in
    *,*) : ;;
    *) single_arch="$(arch_name "$arch_list")" ;;
  esac
  if [ -n "$single_arch" ]; then
    for component in api agent frontend; do
      cp "${out_dir}/${component}-${single_arch}.tar.gz" "${out_dir}/${component}.tar.gz"
    done
  fi

  # integrity-manifest.json: a SEPARATE artefact from manifest.json below, written from the
  # same staged directories before they are removed from consideration — see
  # write_integrity_manifest's own comment for why this is not just another field on
  # manifest.json.
  write_integrity_manifest "$out_dir" "$version"

  # manifest.json: hand-built with the exact field names the awk reader
  # cli_release_manifest_field (installer/cli/lib/release-verify.sh) scans for: "url" and
  # "sha256" as quoted JSON string values on their own line inside each "<component>-<arch>"
  # block. jq would be
  # cleaner but the installer never assumes jq is present at verify time, and a manifest
  # this script cannot also read with a bare awk scan would be a shape the installer's
  # own reader cannot be trusted to parse either.
  {
    echo "{"
    echo "  \"version\": \"${version}\","
    echo "  \"channel\": \"${channel}\","
    manifest_security_field "$security"
    manifest_release_fields "$notes_url"
    # A published release lists the five public keys it pins with their SHA-256 (issue #544); a local
    # build pins nothing it must name, so its manifest stays as before.
    [ "$publish" -ne 1 ] || manifest_pinned_keys_field "${root}/installer/keys"
    echo "  \"artifacts\": {"
    local first=1
    for arch in ${arch_list//,/ }; do
      arch="$(arch_name "$arch")"
      for component in api agent frontend; do
        local sha
        sha="$(sha256_of "${out_dir}/${component}-${arch}.tar.gz")"
        [ "$first" -eq 1 ] || echo "    },"
        first=0
        echo "    \"${component}-${arch}\": {"
        echo "      \"url\": \"https://releases.maran.innovayse.com/${channel}/${component}-${arch}.tar.gz\","
        echo "      \"sha256\": \"${sha}\""
      done
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${out_dir}/manifest.json"

  rm -f "${out_dir}"/.*-*.log
  echo "Bundle staged at ${out_dir} (version ${version}, arch ${arch_list})."
  echo "NOT SIGNED YET — run: maran release sign --key <path-to-private-key> --bundle ${out_dir}"
}
