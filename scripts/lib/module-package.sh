#!/usr/bin/env bash
# Builds, signs and verifies a module package (`.ipm`): `scripts/maran ipm <package|sign|verify|selftest>`.
#
# The format is specified in docs/marketplace/ipm-format.md and READ by the open verification library
# (backend/src/Maran.Packaging). This file only WRITES it, and it never re-implements a check: `verify`
# asks the library, through `Maran.Host --cli module-verify`, so the thing that decides whether a package
# is acceptable exists once. The manifest is not written by hand either: `Maran.Host --cli
# module-manifest` prints it from the module's own `Manifest`, and this script adds only what the
# manifest cannot know before the package exists (the hash of every other entry and the SPA stub).
#
# The signature is the release scheme (P-384 over SHA-384, scripts/lib/signing.sh), created with the
# very function the release bundle uses, but with a THIRD key: the module-signing key. The private key
# NEVER lives in this repository; it is read from --key or $MARAN_MODULE_SIGNING_KEY and this script
# never prints, copies or stores it. The matching public key (installer/keys/module-signing.pub) is
# added to the repository by the owner when the real pair exists; until then `verify` requires --pubkey.
#
# Usage:
#   scripts/maran ipm package  --project <csproj> --out <dir> [--spa <dir>] [--source-date-epoch <n>]
#   scripts/maran ipm sign     --package <file.ipm> [--key <pem>]
#   scripts/maran ipm verify   --package <file.ipm> --pubkey <pem> [--panel-version <semver>]
#   scripts/maran ipm selftest
#
# SOURCE_DATE_EPOCH (digits) makes `package` byte-reproducible; without it the last commit's time is
# used, and without a repository the build refuses rather than stamp the clock into the archive.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/lib/signing.sh"

VERDICT_PREFIX="IPM VERDICT"

# The earliest time a ZIP entry can carry (1980-01-01T00:00:00Z); an earlier epoch is clamped by the
# format and would make two builds that differ only in the clock look identical.
readonly IPM_EPOCH_FLOOR=315532800

# The entry names the format reserves. Neither is ever listed in `files`: the manifest cannot hash
# itself and the signature cannot cover itself.
readonly IPM_MANIFEST_ENTRY="ipm.json"
readonly IPM_SIGNATURE_ENTRY="ipm.sig"

# The panel version the selftest judges packages against. The selftest is about signatures and hashes, and
# must not turn red because a dev build reports a version below the sample's minimum; the panel-too-old
# refusal has its own tests in the library and in the verb.
readonly IPM_SELFTEST_PANEL_VERSION="99.0.0"

# The repository's sample module: the one packageable fixture every suite shares.
readonly IPM_SAMPLE_PROJECT="backend/samples/hello-module/Maran.Samples.Hello.csproj"
readonly IPM_SAMPLE_SPA="backend/samples/hello-module/spa"

ipm_ok() {
  echo "${VERDICT_PREFIX}: OK — $*"
}

ipm_failed() {
  echo "${VERDICT_PREFIX}: FAILED — $*" >&2
  exit 1
}

# The three-answer contract the other harnesses keep (rules/testing.md): a machine that cannot run
# the check says DID NOT RUN on stdout and returns 2; it never invents a verdict about source it did not read.
ipm_did_not_run() {
  echo "${VERDICT_PREFIX}: DID NOT RUN — $*"
  exit 2
}

usage() {
  cat >&2 <<'USAGE'
usage: maran ipm <package|sign|verify|selftest> [arguments]

  package   --project <csproj> --out <dir> [--spa <dir>] [--source-date-epoch <n>]
            publish the module, take its manifest from the module itself, hash every entry and write
            <dir>/<id>-<version>.ipm, UNSIGNED. The archive is deterministic: sorted entries, fixed times and
            modes, so two builds of one source are the same bytes. Only the module assembly and its
            satellite resource assemblies are packaged (docs/marketplace/ipm-format.md).
  sign      --package <file.ipm> [--key <pem>]
            add ipm.sig, the P-384/SHA-384 signature of ipm.json. The key is --key or
            $MARAN_MODULE_SIGNING_KEY; the script refuses when neither is set and never prints it.
  verify    --package <file.ipm> --pubkey <pem> [--panel-version <semver>]
            ask the open verification library (Maran.Host --cli module-verify) whether the package is
            acceptable; this script re-implements none of its checks.
  selftest  package the sample module twice (byte-equal), sign with a throwaway key, verify it, then
            prove that a flipped byte, a stripped signature, another key, a tampered manifest and an
            unlisted entry are each REFUSED. Needs the .NET SDK, openssl, zip and jq; no docker, no root.
USAGE
  exit 1
}

# require_tool: <name> <why>. Refuses with DID NOT RUN when the tool is missing, naming what needed it.
require_tool() {
  command -v "$1" >/dev/null 2>&1 || ipm_did_not_run "'$1' is not on PATH; it is needed to $2."
}

# ipm_epoch: prints the time every archive entry carries. SOURCE_DATE_EPOCH, else the last commit's time.
ipm_epoch() {
  local epoch="${SOURCE_DATE_EPOCH:-}"
  if [ -z "$epoch" ]; then
    epoch="$(git -C "$root" log -1 --format=%ct 2>/dev/null || true)"
  fi
  [[ "$epoch" =~ ^[0-9]+$ ]] \
    || ipm_failed "no build time: set SOURCE_DATE_EPOCH (digits) or build inside a git checkout; the archive never carries the clock."
  [ "$epoch" -ge "$IPM_EPOCH_FLOOR" ] \
    || ipm_failed "SOURCE_DATE_EPOCH ${epoch} is before 1980-01-01, which a ZIP entry cannot carry."
  echo "$epoch"
}

# ipm_refuse_root: the Host's CLI refuses to run as root, so a build or verify run as root would only
# fail later with a worse message.
ipm_refuse_root() {
  [ "$(id -u)" -ne 0 ] \
    || ipm_did_not_run "this is running as root and the panel's command line refuses to; run it as an unprivileged user."
}

# The built Host assembly, set by ipm_build_host.
IPM_HOST_DLL=""

# ipm_build_host: builds the Host in Release once per run and remembers its assembly. The built assembly is
# run directly, as `maran openapi` does, so the verbs see exactly the libraries the shipped binary has.
ipm_build_host() {
  [ -z "$IPM_HOST_DLL" ] || return 0
  local project="$root/backend/src/Maran.Host/Maran.Host.csproj"
  local dll="$root/backend/src/Maran.Host/bin/Release/net9.0/Maran.Host.dll"
  dotnet build "$project" -c Release -nologo -v:minimal >&2 \
    || ipm_failed "the Release build of Maran.Host failed (output above)."
  [ -f "$dll" ] || ipm_failed "the Release build produced no ${dll}."
  IPM_HOST_DLL="$dll"
}

# ipm_host: <verb> [arguments]. Runs one `--cli` verb of the built Host. The `Testing` environment is what
# the command-line mode is composed for; it is not a production setting.
ipm_host() {
  ipm_build_host
  ASPNETCORE_ENVIRONMENT=Testing dotnet "$IPM_HOST_DLL" --cli "$@"
}

# ipm_sha384_hex: <file>. The lowercase hex SHA-384 of a file.
ipm_sha384_hex() {
  sha384sum "$1" | cut -d' ' -f1
}

# ipm_sri: <file>. The Subresource Integrity string of a file, `sha384-<base64>`.
ipm_sri() {
  printf 'sha384-%s' "$(openssl dgst -sha384 -binary "$1" | base64 -w0)"
}

# ipm_normalise_tree: <dir> <epoch>. Fixes every mtime and mode under <dir>, the only two things of a
# file that a ZIP records and that the same source can differ in between two machines.
ipm_normalise_tree() {
  find "$1" -type d -exec chmod 0755 {} +
  find "$1" -type f -exec chmod 0644 {} +
  find "$1" -exec touch -h -d "@$2" {} +
}

# ipm_files_json: <stage dir>. Prints the manifest's `files` array: every file under the stage except the two
# reserved entries, sorted bytewise by path, each as {path, sha384, size}.
ipm_files_json() {
  local stage="$1" relative
  (
    cd "$stage"
    find . -type f ! -name "$IPM_MANIFEST_ENTRY" ! -name "$IPM_SIGNATURE_ENTRY" -print0 \
      | LC_ALL=C sort -z \
      | while IFS= read -r -d '' relative; do
          relative="${relative#./}"
          printf '%s\t%s\t%s\n' "$relative" "$(ipm_sha384_hex "$relative")" "$(stat -c %s "$relative")"
        done
  ) | jq -R -s -c 'split("\n") | map(select(length > 0) | split("\t")) | map({path: .[0], sha384: .[1], size: (.[2] | tonumber)})'
}

# ipm_spa_json: <stage dir>. Prints the manifest's `spa` object for the stage's spa/ folder, or `null` when
# the package carries none. The layout is a stub until the SPA delivery decision is made
# (docs/marketplace/ipm-format.md "The spa folder"): an entry script, its integrity string, and the locales.
ipm_spa_json() {
  local stage="$1" locale locales=()
  if [ ! -d "$stage/spa" ]; then
    echo null
    return 0
  fi
  [ -f "$stage/spa/entry.js" ] || ipm_failed "the spa folder has no entry.js; a package with an interface stub names its entry script."
  for locale in "$stage"/spa/locales/*.json; do
    [ -e "$locale" ] || continue
    locales+=("$(basename "$locale" .json)")
  done
  printf '%s\n' ${locales[@]+"${locales[@]}"} | LC_ALL=C sort \
    | jq -R -s -c --arg integrity "$(ipm_sri "$stage/spa/entry.js")" \
        '{entry: "spa/entry.js", integrity: $integrity, locales: (split("\n") | map(select(length > 0)))}'
}

# cmd_package: see usage.
cmd_package() {
  local project="" out="" spa="" epoch=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --project) project="${2:?--project requires a value}"; shift 2 ;;
      --out) out="${2:?--out requires a value}"; shift 2 ;;
      --spa) spa="${2:?--spa requires a value}"; shift 2 ;;
      --source-date-epoch) epoch="${2:?--source-date-epoch requires a value}"; shift 2 ;;
      *) echo "ipm package: unknown argument: $1" >&2; usage ;;
    esac
  done
  [ -n "$project" ] && [ -f "$project" ] || ipm_failed "--project must name an existing .csproj."
  [ -n "$out" ] || ipm_failed "--out is required."
  [ -z "$spa" ] || [ -d "$spa" ] || ipm_failed "--spa must name an existing folder."
  [ -z "$epoch" ] || SOURCE_DATE_EPOCH="$epoch"
  epoch="$(ipm_epoch)"

  require_tool dotnet "publish and read the module"
  require_tool zip "write the archive"
  require_tool jq "compose the manifest"
  require_tool openssl "hash the interface stub"
  ipm_refuse_root

  project="$(cd "$(dirname "$project")" && pwd)/$(basename "$project")"
  out="$(mkdir -p "$out" && cd "$out" && pwd)"
  local work stage entry_name
  work="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf -- '$work'" RETURN
  stage="$work/stage"
  entry_name="$(basename "$project" .csproj)"

  # PathMap and ContinuousIntegrationBuild remove the checkout's location from the assembly, and
  # Deterministic makes the compiler's output a function of the source alone: without all three two
  # machines build different bytes from one commit.
  dotnet publish "$project" -c Release -o "$work/publish" -nologo -v:minimal \
    -p:Deterministic=true -p:ContinuousIntegrationBuild=true \
    -p:PathMap="$(dirname "$project")=/src" >&2 \
    || ipm_failed "dotnet publish failed for ${project} (output above)."
  [ -f "$work/publish/${entry_name}.dll" ] \
    || ipm_failed "the publish produced no ${entry_name}.dll; the assembly must be named for its project so the package can name its entry."

  # Only the module assembly and its satellite resource assemblies travel: every other assembly a publish
  # copies (the Sdk, the SharedKernel, the framework) is the panel's own, and a package that shipped one
  # would be refused by the loader. A private dependency is therefore a build failure here, not a surprise there.
  install -d "$stage/lib"
  cp "$work/publish/${entry_name}.dll" "$stage/lib/"
  local satellite culture
  for satellite in "$work"/publish/*/"${entry_name}.resources.dll"; do
    [ -e "$satellite" ] || continue
    culture="$(basename "$(dirname "$satellite")")"
    install -d "$stage/lib/$culture"
    cp "$satellite" "$stage/lib/$culture/"
  done
  [ -z "$spa" ] || { install -d "$stage/spa"; cp -r "$spa"/. "$stage/spa/"; }
  ipm_normalise_tree "$stage" "$epoch"

  ipm_host module-manifest "$stage/lib/${entry_name}.dll" >"$work/base.json" \
    || ipm_failed "the module's manifest could not be read from ${entry_name}.dll (output above)."
  jq -e . "$work/base.json" >/dev/null || ipm_failed "module-manifest printed something that is not JSON."

  # Fixed key order (`jq -S`) and a trailing newline: the signed bytes are the bytes of this file as stored,
  # so there is no canonicalisation step for a signer and a verifier to disagree about.
  # Each is computed on its own line: a failure inside a substitution that is merely an argument of another
  # command is not seen by `set -e`, and would write a manifest with a hole in it.
  local files_json spa_json
  files_json="$(ipm_files_json "$stage")"
  spa_json="$(ipm_spa_json "$stage")"
  jq -S --argjson files "$files_json" --argjson spa "$spa_json" \
    '. + {files: $files} + (if $spa == null then {} else {spa: $spa} end)' "$work/base.json" >"$stage/$IPM_MANIFEST_ENTRY"
  touch -d "@$epoch" "$stage/$IPM_MANIFEST_ENTRY"
  chmod 0644 "$stage/$IPM_MANIFEST_ENTRY"

  local id version package
  id="$(jq -r .id "$stage/$IPM_MANIFEST_ENTRY")"
  version="$(jq -r .version "$stage/$IPM_MANIFEST_ENTRY")"
  package="$out/${id}-${version}.ipm"
  rm -f -- "$package"

  # ipm.json first, then the rest bytewise; -X drops the platform's extra fields (uid, gid, access time),
  # -D writes no directory entries, and TZ=UTC fixes how the DOS time field is read.
  (
    cd "$stage"
    { echo "$IPM_MANIFEST_ENTRY"; find . -type f ! -name "$IPM_MANIFEST_ENTRY" -print | sed 's#^\./##' | LC_ALL=C sort; } \
      | TZ=UTC zip -X -D -q -9 "$package" -@
  )
  echo "$package"
}

# ipm_key_check: <private key>. Refuses a key that cannot be the module-signing key's scheme (P-384). The release
# key is the same scheme and cannot be told apart here; keeping them apart is the owner's custody (D10).
ipm_key_check() {
  [ -f "$1" ] || ipm_failed "the signing key '${1}' does not exist."
  openssl pkey -in "$1" -noout -text 2>/dev/null | grep -q 'NIST CURVE: P-384' \
    || ipm_failed "the signing key is not an ECDSA P-384 key, the scheme every package is signed with."
}

# cmd_sign: see usage.
cmd_sign() {
  local package="" key="${MARAN_MODULE_SIGNING_KEY:-}"
  while [ $# -gt 0 ]; do
    case "$1" in
      --package) package="${2:?--package requires a value}"; shift 2 ;;
      --key) key="${2:?--key requires a value}"; shift 2 ;;
      *) echo "ipm sign: unknown argument: $1" >&2; usage ;;
    esac
  done
  [ -n "$package" ] && [ -f "$package" ] || ipm_failed "--package must name an existing .ipm."
  [ -n "$key" ] || ipm_failed "no signing key: pass --key <pem> or set MARAN_MODULE_SIGNING_KEY. The private key is never kept in this repository."
  require_tool zip "add the signature entry"
  require_tool unzip "read the manifest"
  require_tool openssl "sign"
  ipm_key_check "$key"
  unzip -Z1 "$package" | grep -x "$IPM_MANIFEST_ENTRY" >/dev/null || ipm_failed "${package} has no ${IPM_MANIFEST_ENTRY}."
  ! unzip -Z1 "$package" | grep -x "$IPM_SIGNATURE_ENTRY" >/dev/null \
    || ipm_failed "${package} already carries ${IPM_SIGNATURE_ENTRY}; sign an unsigned package."

  local epoch work
  epoch="$(ipm_epoch)"
  package="$(cd "$(dirname "$package")" && pwd)/$(basename "$package")"
  work="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf -- '$work'" RETURN
  unzip -p "$package" "$IPM_MANIFEST_ENTRY" >"$work/$IPM_MANIFEST_ENTRY"
  sign_release_file "$key" "$work/$IPM_MANIFEST_ENTRY" "$work/$IPM_SIGNATURE_ENTRY"
  chmod 0644 "$work/$IPM_SIGNATURE_ENTRY"
  touch -d "@$epoch" "$work/$IPM_SIGNATURE_ENTRY"
  (cd "$work" && TZ=UTC zip -X -D -q "$package" "$IPM_SIGNATURE_ENTRY")
  echo "signed ${package}"
}

# cmd_verify: see usage. The library decides; this only passes the question on and reports its answer.
cmd_verify() {
  local package="" pubkey="" panel_version=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --package) package="${2:?--package requires a value}"; shift 2 ;;
      --pubkey) pubkey="${2:?--pubkey requires a value}"; shift 2 ;;
      --panel-version) panel_version="${2:?--panel-version requires a value}"; shift 2 ;;
      *) echo "ipm verify: unknown argument: $1" >&2; usage ;;
    esac
  done
  [ -n "$package" ] && [ -f "$package" ] || ipm_failed "--package must name an existing .ipm."
  [ -n "$pubkey" ] && [ -f "$pubkey" ] || ipm_failed "--pubkey must name the module-signing public key; installer/keys/module-signing.pub is added when the owner generates the real pair."
  require_tool dotnet "run the verification library"
  ipm_refuse_root
  local args=(module-verify "$package" --pubkey "$pubkey")
  [ -z "$panel_version" ] || args+=(--panel-version "$panel_version")
  ipm_host "${args[@]}"
}

# ipm_refused: <description> <pattern> <package> <pubkey>. Verifies <package> and requires a REFUSAL whose output
# matches <pattern>: a package refused for the wrong reason has proven nothing about the right one.
ipm_refused() {
  local description="$1" pattern="$2" package="$3" pubkey="$4" output
  if output="$(ipm_host module-verify "$package" --pubkey "$pubkey" --panel-version "$IPM_SELFTEST_PANEL_VERSION" 2>&1)"; then
    ipm_failed "selftest: verify ACCEPTED ${description}."
  fi
  if ! printf '%s' "$output" | grep -qE "$pattern"; then
    ipm_failed "selftest: ${description} was refused for another reason than expected (wanted /${pattern}/): ${output}"
  fi
  echo "${VERDICT_PREFIX}: OK, ${description} REFUSED (expected)."
}

# cmd_selftest: see usage.
cmd_selftest() {
  require_tool dotnet "build the sample module and run the verification library"
  require_tool openssl "generate throwaway keys and sign"
  require_tool zip "write archives"
  require_tool unzip "take archives apart"
  require_tool jq "compose and read manifests"
  ipm_refuse_root

  # Built here, in this shell, so every verb below runs the same assembly and the build happens once.
  ipm_build_host

  local work keydir
  work="$(mktemp -d)"
  trap 'rm -rf -- "$work"' EXIT
  keydir="$work/keys"
  install -d -m 0700 "$keydir"
  # The SAME curve and digest the real key uses, and a key generated per run and never committed: a selftest that
  # signed with another algorithm would verify an invocation nobody ships (rules/testing.md).
  openssl ecparam -name secp384r1 -genkey -noout -out "$keydir/a.key" >/dev/null 2>&1
  openssl pkey -in "$keydir/a.key" -pubout -out "$keydir/a.pub" >/dev/null 2>&1
  openssl ecparam -name secp384r1 -genkey -noout -out "$keydir/b.key" >/dev/null 2>&1
  openssl pkey -in "$keydir/b.key" -pubout -out "$keydir/b.pub" >/dev/null 2>&1

  # Any fixed time at all: the point is that two builds with the SAME one agree.
  export SOURCE_DATE_EPOCH=1767225600

  echo "-- outcome 1: the build is reproducible --"
  local first second
  first="$(cmd_package --project "$root/$IPM_SAMPLE_PROJECT" --out "$work/one" --spa "$root/$IPM_SAMPLE_SPA")"
  second="$(cmd_package --project "$root/$IPM_SAMPLE_PROJECT" --out "$work/two" --spa "$root/$IPM_SAMPLE_SPA")"
  [ -s "$first" ] && [ -s "$second" ] || ipm_failed "selftest: the sample module produced no package, so nothing below proves anything."
  cmp -s "$first" "$second" \
    || ipm_failed "selftest: two builds of one source differ ($(sha256sum "$first" "$second" | cut -c1-16 | tr '\n' ' ')); the archive is not deterministic."
  echo "${VERDICT_PREFIX}: OK, two builds of the sample are the same bytes."

  echo
  echo "-- outcome 2: a signed package is accepted, by the library and by openssl alike --"
  local package="$work/signed.ipm" tampered
  cp "$first" "$package"
  cmd_sign --package "$package" --key "$keydir/a.key" >/dev/null
  cmd_verify --package "$package" --pubkey "$keydir/a.pub" --panel-version "$IPM_SELFTEST_PANEL_VERSION" >/dev/null \
    || ipm_failed "selftest: the library refused a correctly signed package; the check refuses everything, or the fixture is broken."
  echo "${VERDICT_PREFIX}: OK, a correctly signed package ACCEPTED by the library (inverse control)."
  unzip -p "$package" "$IPM_MANIFEST_ENTRY" >"$work/ipm.json"
  unzip -p "$package" "$IPM_SIGNATURE_ENTRY" >"$work/ipm.sig"
  verify_release_file "$keydir/a.pub" "$work/ipm.json" "$work/ipm.sig" \
    || ipm_failed "selftest: openssl refused the signature the library accepts; signer and verifier have drifted."
  echo "${VERDICT_PREFIX}: OK, openssl accepts the same signature (signer and verifier agree)."

  echo
  echo "-- outcome 3: each defect is refused for its own reason --"
  # A byte of the module assembly, edited after signing: the signature still holds, so only the hash
  # of that entry can refuse it.
  tampered="$work/tamper-assembly"
  rm -rf -- "$tampered" && install -d "$tampered"
  (cd "$tampered" && unzip -q "$package")
  local dll
  dll="$(find "$tampered/lib" -maxdepth 1 -name '*.dll' | head -n 1)"
  [ -n "$dll" ] || ipm_failed "selftest: the sample package holds no assembly under lib/."
  cp "$dll" "$work/assembly.orig"
  local old_byte
  old_byte="$(dd if="$dll" bs=1 skip=100 count=1 status=none | od -An -tu1 | tr -d ' ')"
  # Add one to the byte, wrapping at 256, so the byte provably changes whatever it held.
  printf "$(printf '\\x%02x' $(((old_byte + 1) % 256)))" | dd of="$dll" bs=1 seek=100 conv=notrunc status=none
  if cmp -s "$dll" "$work/assembly.orig"; then
    ipm_failed "selftest: the byte flip changed nothing, so this case proves nothing."
  fi
  rm -f -- "$work/flipped.ipm"
  (cd "$tampered" && TZ=UTC zip -X -D -q -9 "$work/flipped.ipm" -@ < <(find . -type f -print | sed 's#^\./##' | LC_ALL=C sort))
  ipm_refused "a package with one byte of its assembly flipped" 'IpmHashMismatch' "$work/flipped.ipm" "$keydir/a.pub"

  # The same package without its signature.
  cp "$package" "$work/unsigned.ipm"
  zip -q -d "$work/unsigned.ipm" "$IPM_SIGNATURE_ENTRY"
  ipm_refused "an unsigned package" 'IpmSignature' "$work/unsigned.ipm" "$keydir/a.pub"

  # Signed by another key: a perfectly good signature, by someone the panel does not trust.
  ipm_refused "a package signed by another key" 'IpmSignature' "$package" "$keydir/b.pub"

  # One byte of the manifest itself, edited after signing.
  tampered="$work/tamper-manifest"
  rm -rf -- "$tampered" && install -d "$tampered"
  (cd "$tampered" && unzip -q "$package")
  sed -i 's/"id": "hello"/"id": "hellp"/' "$tampered/$IPM_MANIFEST_ENTRY"
  cmp -s "$work/ipm.json" "$tampered/$IPM_MANIFEST_ENTRY" && ipm_failed "selftest: the manifest edit changed nothing, so this case proves nothing."
  rm -f -- "$work/manifest.ipm"
  (cd "$tampered" && TZ=UTC zip -X -D -q -9 "$work/manifest.ipm" -@ < <(find . -type f -print | sed 's#^\./##' | LC_ALL=C sort))
  ipm_refused "a package whose manifest was edited after signing" 'IpmSignature' "$work/manifest.ipm" "$keydir/a.pub"

  # An entry the manifest does not list, signature intact.
  cp "$package" "$work/extra.ipm"
  echo "not listed" >"$work/extra.txt"
  (cd "$work" && TZ=UTC zip -X -D -q "$work/extra.ipm" extra.txt)
  ipm_refused "a package carrying an entry its manifest does not list" 'IpmUnlistedEntry' "$work/extra.ipm" "$keydir/a.pub"

  echo
  echo "UNOBSERVED HERE: hostile archives (traversal, symlinks, ratio bombs, duplicate names) are refused by the"
  echo "library's own tests, not by this script; the real module-signing key and the marketplace are not reached."
  ipm_ok "selftest: the sample packaged twice to the same bytes, was signed and verified (library and openssl agree)," \
    "and a flipped byte, a missing signature, another key, an edited manifest and an unlisted entry were each refused for their own reason."
}

command_name="${1:-}"
[ -n "$command_name" ] || usage
shift || true

case "$command_name" in
  package) cmd_package "$@" ;;
  sign) cmd_sign "$@" ;;
  verify) cmd_verify "$@" ;;
  selftest) cmd_selftest "$@" ;;
  -h|--help|help) usage ;;
  *) echo "unknown ipm subcommand: $command_name" >&2; usage ;;
esac
