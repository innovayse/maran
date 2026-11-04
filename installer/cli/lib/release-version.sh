#!/usr/bin/env bash
# release-version.sh: the ONE home of release-name validation and ordering for the `maran` CLI and
# the installer. A release name becomes a directory name under /usr/local/maran/releases and a line
# in /etc/maran/release.state, so a name is validated here before any path or comparison is built
# from it; no other library defines a version pattern, they call the functions below.
#
# Two kinds of release name exist. A SemVer 2.0 version is what a published release carries. A
# `legacy-<UTC timestamp>` name is what a host installed before the release layout existed is given
# when the first `maran update` adopts it; it ranks below every SemVer version, so any published
# release is an upgrade from it.
#
# Sourced after common.sh by the `maran` wrapper and by installer/lib/50-artifacts.sh; never
# executed on its own. Every function either answers by status (is_*, gt) or refuses a malformed
# argument with cli_fail (gt, an internal-error guard only; callers validate first).

# SemVer 2.0 without build metadata: three numeric identifiers with no leading zero, then an
# optional pre-release of dot-separated identifiers, each either a numeric one with no leading zero
# or an alphanumeric one holding at least one letter or hyphen.
readonly MARAN_VERSION_NUMERIC_PATTERN='(0|[1-9][0-9]*)'
readonly MARAN_VERSION_PRERELEASE_PATTERN='(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
readonly MARAN_VERSION_SEMVER_RE="^${MARAN_VERSION_NUMERIC_PATTERN}\.${MARAN_VERSION_NUMERIC_PATTERN}\.${MARAN_VERSION_NUMERIC_PATTERN}(-${MARAN_VERSION_PRERELEASE_PATTERN}(\.${MARAN_VERSION_PRERELEASE_PATTERN})*)?\$"
# The name given to an adopted pre-layout host: legacy-YYYYMMDDTHHMMSSZ (UTC).
readonly MARAN_VERSION_LEGACY_RE='^legacy-[0-9]{8}T[0-9]{6}Z$'
# A name longer than this is refused: it ends up in file names and in the dump name contract.
readonly MARAN_VERSION_MAX_BYTES=64

# cli_version_is_semver V: succeeds when V is a strict SemVer 2.0 version (no build metadata, no
# leading zeros in numeric identifiers) of at most 64 bytes. Fails otherwise; never calls cli_fail.
cli_version_is_semver() {
  local LC_ALL=C v="${1-}"
  [ "${#v}" -ge 5 ] && [ "${#v}" -le "$MARAN_VERSION_MAX_BYTES" ] || return 1
  [[ "$v" =~ $MARAN_VERSION_SEMVER_RE ]]
}

# cli_version_is_legacy V: succeeds when V is an adopted-host name, legacy-YYYYMMDDTHHMMSSZ. Fails
# otherwise; never calls cli_fail.
cli_version_is_legacy() {
  local LC_ALL=C v="${1-}"
  [[ "$v" =~ $MARAN_VERSION_LEGACY_RE ]]
}

# cli_version_is_release_name V: succeeds when V is a SemVer version or a legacy name, i.e. when it
# may be a directory under releases/ and a value in release.state. Never calls cli_fail.
cli_version_is_release_name() {
  cli_version_is_semver "${1-}" || cli_version_is_legacy "${1-}"
}

# cli_version_compare_identifiers A B: prints 1, 0 or -1 for one dot-separated pre-release or core
# identifier. Two all-digit identifiers compare by value (leading zeros dropped, then length, then
# text, so no arithmetic can overflow); an all-digit identifier is below any other; text compares as
# ASCII.
cli_version_compare_identifiers() {
  local LC_ALL=C a="$1" b="$2"
  if [[ "$a" =~ ^[0-9]+$ && "$b" =~ ^[0-9]+$ ]]; then
    a="${a#"${a%%[!0]*}"}"; b="${b#"${b%%[!0]*}"}"
    if [ "${#a}" -ne "${#b}" ]; then
      if [ "${#a}" -gt "${#b}" ]; then echo 1; else echo -1; fi
      return 0
    fi
  elif [[ "$a" =~ ^[0-9]+$ ]]; then echo -1; return 0
  elif [[ "$b" =~ ^[0-9]+$ ]]; then echo 1; return 0
  fi
  if [ "$a" = "$b" ]; then echo 0; elif [[ "$a" > "$b" ]]; then echo 1; else echo -1; fi
}

# cli_version_gt A B: succeeds when release name A ranks strictly above B, fails when it is equal or
# lower. SemVer 2.0 precedence: a release outranks its own pre-release; pre-release identifiers
# compare left to right, numeric below alphanumeric, and the shorter list is lower when all else is
# equal. A legacy-* name ranks below every SemVer version and two legacy names rank equal. Refuses
# (cli_fail) an argument that is not a release name, so a malformed value can never compare as
# "not greater" and be mistaken for an answer.
cli_version_gt() {
  local LC_ALL=C a="$1" b="$2" a_core b_core a_pre="" b_pre="" i
  local -a a_parts b_parts
  cli_version_is_release_name "$a" && cli_version_is_release_name "$b" \
    || cli_fail "not a valid release name: '${a}' or '${b}'"
  if cli_version_is_legacy "$a"; then return 1; fi
  if cli_version_is_legacy "$b"; then return 0; fi
  a_core="${a%%-*}"; b_core="${b%%-*}"
  [ "$a" = "$a_core" ] || a_pre="${a#*-}"
  [ "$b" = "$b_core" ] || b_pre="${b#*-}"
  IFS=. read -r -a a_parts <<< "$a_core"
  IFS=. read -r -a b_parts <<< "$b_core"
  for i in 0 1 2; do
    case "$(cli_version_compare_identifiers "${a_parts[i]}" "${b_parts[i]}")" in
      1) return 0 ;;
      -1) return 1 ;;
    esac
  done
  [ "$a_pre" = "$b_pre" ] && return 1
  [ -z "$b_pre" ] && return 1
  [ -z "$a_pre" ] && return 0
  IFS=. read -r -a a_parts <<< "$a_pre"
  IFS=. read -r -a b_parts <<< "$b_pre"
  for ((i = 0; i < ${#a_parts[@]} && i < ${#b_parts[@]}; i++)); do
    case "$(cli_version_compare_identifiers "${a_parts[i]}" "${b_parts[i]}")" in
      1) return 0 ;;
      -1) return 1 ;;
    esac
  done
  [ "${#a_parts[@]}" -gt "${#b_parts[@]}" ]
}
