#!/usr/bin/env bash
# update-selftest-world.sh: the temporary world every case of `maran update-selftest` runs in.
#
# Sourced by update-selftest.sh after its helpers (selftest_pass, expect_*, run_capture) exist; never
# executed on its own and it sets no shell option. It builds, in order: the temporary root, the
# program shims in front of PATH, COPIES of the CLI libraries whose absolute path constants are
# rewritten into that root (and the proof that nothing was missed), the throwaway signing keys, and
# the signed bundles and helper functions the later parts use.
#
# The shims, and what each one makes observable:
#   curl     serves local files by URL and records its argv (the outbound request is audited);
#   chown    records every call, and is a no-op unless really root;
#   install  records its argv (a single copy of the offline tarball is countable) and drops -o/-g
#            when unprivileged;
#   uname    answers -m from MARAN_SELFTEST_UNAME (the architecture is chosen by it);
#   stat     reports the files this user owns as root's (uid AND gid), which is what a root-run CLI
#            sees of its own files, and passes every other question through;
#   tar      records its argv and, with MARAN_SELFTEST_TAR_PLANT_SETID=1, re-adds set-id and sticky
#            bits after an extraction, so the CLI's own stripping is what the check observes and not
#            the extractor's habit of dropping them.

work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
umask 022
# mroot is spliced into sed expressions and a grep pattern: plain characters only, and never a
# constant the rewrite itself looks for.
mroot="${work}/root"
[[ "$mroot" =~ ^/[A-Za-z0-9._/-]+$ ]] || selftest_failed "the temporary directory ${work} has characters this harness will not splice into sed"
case "$mroot" in
  */usr/local/maran*|*/etc/maran*|*/var/backups/maran-update*|*/var/lib/maran-cli-staging*)
    selftest_failed "the temporary directory ${work} contains a path the rewrite would mangle" ;;
esac
mar="${mroot}/usr/local/maran"
etc="${mroot}/etc/maran"
backup_dir="${mroot}/var/backups/maran-update"
staging_base="${mroot}/var/lib/maran-cli-staging"
install -d "${mroot}/usr/local" "${mroot}/etc" "${mroot}/var/backups"
install -d -m 0700 "$staging_base"
shims="${work}/shims"
install -d "$shims"
export MARAN_SELFTEST_CURL_LOG="${work}/curl.log"
export MARAN_SELFTEST_CHOWN_LOG="${work}/chown.log"
export MARAN_SELFTEST_INSTALL_LOG="${work}/install.log"
export MARAN_SELFTEST_TAR_LOG="${work}/tar.log"
: > "$MARAN_SELFTEST_CURL_LOG"
: > "$MARAN_SELFTEST_CHOWN_LOG"
: > "$MARAN_SELFTEST_INSTALL_LOG"
: > "$MARAN_SELFTEST_TAR_LOG"

# make_shim: <program name>. Writes the shim from standard input; @REAL@ in it becomes the path of
# the real program, resolved before the shims are put on PATH.
make_shim() {
  local real
  real="$(command -v "$1")"
  sed "s|@REAL@|${real}|g" > "${shims}/${1}"
  chmod 0755 "${shims}/${1}"
}

# curl shim: serves $MARAN_SELFTEST_SERVE/<channel>/<file> for https://<host>/<channel>/<file>, 404
# (status 22, as `curl --fail` answers) for anything else, refuses plain http, and records its argv.
cat > "${shims}/curl" <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "${MARAN_SELFTEST_CURL_LOG:?}"
args=("$@")
out=""
url=""
i=0
while [ "$i" -lt "${#args[@]}" ]; do
  a="${args[$i]}"
  case "$a" in
    -o|--output) i=$((i + 1)); out="${args[$i]}" ;;
    --output=*) out="${a#--output=}" ;;
    -*) ;;
    http://*|https://*) url="$a" ;;
  esac
  i=$((i + 1))
done
[ -n "$url" ] || { echo "curl shim: no URL in: $*" >&2; exit 2; }
case "$url" in https://*) ;; *) echo "curl shim: refusing a non-https URL: ${url}" >&2; exit 1 ;; esac
path="${url#https://}"
path="${path#*/}"
file="$(basename -- "$path")"
channel="$(basename -- "$(dirname -- "$path")")"
src="${MARAN_SELFTEST_SERVE:?}/${channel}/${file}"
[ -f "$src" ] || { echo "curl shim: 404 ${url}" >&2; exit 22; }
if [ -n "$out" ]; then cp -- "$src" "$out"; else cat -- "$src"; fi
SHIM
chmod 0755 "${shims}/curl"

make_shim chown <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "${MARAN_SELFTEST_CHOWN_LOG:?}"
if [ "$(id -u)" -eq 0 ]; then exec @REAL@ "$@"; fi
exit 0
SHIM

make_shim install <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "${MARAN_SELFTEST_INSTALL_LOG:?}"
if [ "$(id -u)" -eq 0 ]; then exec @REAL@ "$@"; fi
args=()
skip=0
for a in "$@"; do
  if [ "$skip" -eq 1 ]; then skip=0; continue; fi
  case "$a" in
    -o|-g|--owner|--group) skip=1 ;;
    --owner=*|--group=*|-o?*|-g?*) ;;
    *) args+=("$a") ;;
  esac
done
exec @REAL@ "${args[@]}"
SHIM

make_shim uname <<'SHIM'
#!/usr/bin/env bash
if [ -n "${MARAN_SELFTEST_UNAME:-}" ] && [ "${1:-}" = "-m" ]; then echo "$MARAN_SELFTEST_UNAME"; exit 0; fi
exec @REAL@ "$@"
SHIM

# stat shim: when not root, a file this user owns is reported with owner 0 (%u) and, when its group
# is this user's primary group, group 0 (%g), per file; every other question passes through.
make_shim stat <<'SHIM'
#!/usr/bin/env bash
real=@REAL@
if [ "$(id -u)" -eq 0 ]; then exec "$real" "$@"; fi
orig=("$@")
fmt=""
files=()
while [ "$#" -gt 0 ]; do
  case "$1" in
    -c) fmt="${2-}"; shift; shift || true ;;
    --) shift; files+=("$@"); break ;;
    *) files+=("$1"); shift ;;
  esac
done
case "$fmt" in *%u* | *%g*) ;; *) exec "$real" "${orig[@]}" ;; esac
me="$(id -u)"
mygid="$(id -g)"
for f in "${files[@]}"; do
  uid="$("$real" -c %u -- "$f" 2>/dev/null)" && gid="$("$real" -c %g -- "$f" 2>/dev/null)" \
    || exec "$real" "${orig[@]}"
  out="$fmt"
  [ "$uid" != "$me" ] || out="${out//%u/0}"
  [ "$gid" != "$mygid" ] || out="${out//%g/0}"
  "$real" -c "$out" -- "$f" || exit $?
done
SHIM

make_shim tar <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "${MARAN_SELFTEST_TAR_LOG:?}"
@REAL@ "$@"
status=$?
if [ "${MARAN_SELFTEST_TAR_PLANT_SETID:-}" = "1" ] && [ "$status" -eq 0 ]; then
  dest=""
  extracting=0
  prev=""
  for a in "$@"; do
    [ "$prev" != "-C" ] || dest="$a"
    case "$a" in -x*) extracting=1 ;; esac
    prev="$a"
  done
  if [ "$extracting" -eq 1 ] && [ -n "$dest" ]; then
    chmod 6755 "${dest}/setid-file" 2>/dev/null || true
    chmod 1777 "${dest}/world-dir" 2>/dev/null || true
  fi
fi
exit "$status"
SHIM

export PATH="${shims}:${PATH}"
# Nothing below may reach a real host: the curl on PATH must be the shim.
[ "$(command -v curl 2>/dev/null || echo "${shims}/curl")" = "${shims}/curl" ] \
  || selftest_failed "curl resolves to $(command -v curl), not the shim: a download would leave this machine."

# ---------------------------------------------------------------------------------------------
echo "-- libraries: the copies under test and the proof their constants were rewritten --"
# ---------------------------------------------------------------------------------------------
libdir="${work}/lib"
install -d "$libdir"
copied_libs="common.sh release-version.sh release-verify.sh release-unpack.sh release-layout.sh update-dumps.sh update-unattended.sh update.sh"
constants='(^|[^A-Za-z0-9_./-])(/usr/local/maran|/etc/maran|/var/backups/maran-update|/var/lib/maran-cli-staging)'
for lib in $copied_libs; do
  [ -f "${CLI_LIB_DIR}/${lib}" ] || selftest_failed "installer/cli/lib/${lib} does not exist - the library the design names is missing, so nothing can be checked."
  sed -e "s|/var/lib/maran-cli-staging|${mroot}/var/lib/maran-cli-staging|g" \
      -e "s|/var/backups/maran-update|${mroot}/var/backups/maran-update|g" \
      -e "s|/usr/local/maran|${mroot}/usr/local/maran|g" \
      -e "s|/etc/maran|${mroot}/etc/maran|g" \
      "${CLI_LIB_DIR}/${lib}" > "${libdir}/${lib}"
done
originals=""
copies=""
for lib in $copied_libs; do originals="${originals} ${CLI_LIB_DIR}/${lib}"; copies="${copies} ${libdir}/${lib}"; done
# The positive control: the ORIGINALS hold such constants (the grep can hit), and the COPIES hold
# none (every one was rewritten). A copy that still named /usr/local/maran would touch the real one.
# shellcheck disable=SC2086
grep -qE -- "$constants" $originals \
  || selftest_failed "the original libraries hold no absolute path constant - the rewrite check below could not see one, so it proves nothing."
selftest_pass "the originals DO name the constants the rewrite moves (the search is capable of a hit)"
# shellcheck disable=SC2086
if grep -nE -- "$constants" $copies; then
  selftest_failed "a constant survived the rewrite (lines above): the selftest would touch the real /usr/local/maran, /etc/maran or /var/backups/maran-update."
fi
selftest_pass "no copy names a real /usr/local/maran, /etc/maran, /var/backups/maran-update or staging path"

# The key path is a constant of the library: no `${VAR:-...}` fallback and no flag replaces it
# (threat note, reviewer item 2). Control: the same grep finds the constant.
key_def='^readonly MARAN_RELEASE_PUBLIC_KEY="/usr/local/maran/cli/keys/release-signing\.pub"$'
grep -qE -- "$key_def" "${CLI_LIB_DIR}/release-verify.sh" \
  || selftest_failed "release-verify.sh does not define the baked key path as a readonly literal - the override scan has nothing to look at."
selftest_pass "release-verify.sh defines the baked key path as a readonly literal (positive control for the override scan)"
# scan_code: <file> <pattern>. Hits on non-comment lines only, as "<n>:<line>".
scan_code() {
  grep -vE '^[[:space:]]*#' "$1" | grep -nE -- "$2"
}
assignments="$(scan_code "${CLI_LIB_DIR}/release-verify.sh" 'MARAN_RELEASE_PUBLIC_KEY=' | wc -l)"
expect_equal "the baked key constant is assigned once, and no ':-' form of it exists anywhere in the library" "1" \
  "$(( assignments + $(scan_code "${CLI_LIB_DIR}/release-verify.sh" 'MARAN_RELEASE_PUBLIC_KEY:[-=+?]' | wc -l) ))"

shell_pattern='\beval\b|bash -c|[^a-z]sh -c'
for lib in release-version release-verify release-unpack release-layout update-dumps panel-dump update-health update-unattended update update-schedule rollback; do
  if scan_code "${CLI_LIB_DIR}/${lib}.sh" "$shell_pattern"; then
    selftest_failed "${lib}.sh builds a shell string (lines above): eval, bash -c or sh -c."
  fi
done
scan_code "${root}/scripts/lib/release-build-components.sh" "$shell_pattern" >/dev/null \
  || selftest_failed "the shell-string scan found nothing in scripts/lib/release-build-components.sh, which has an sh -c - the scan cannot hit."
selftest_pass "no eval, bash -c or sh -c in the eleven release and update libraries (the scan hits release-build-components.sh, so it can)"

# shellcheck disable=SC1090
. "${libdir}/common.sh"
# shellcheck disable=SC1090
. "${libdir}/release-version.sh"
# shellcheck disable=SC1090
. "${libdir}/release-verify.sh"
# shellcheck disable=SC1090
. "${libdir}/release-unpack.sh"
# shellcheck disable=SC1090
. "${libdir}/release-layout.sh"
# shellcheck disable=SC1090
. "${libdir}/update-dumps.sh"
for fn in cli_version_is_semver cli_version_is_legacy cli_version_is_release_name cli_version_gt \
          cli_release_arch cli_release_manifest_field cli_release_fetch_manifest cli_release_verify_manifest \
          cli_release_fetch_artifacts cli_release_verify_offline cli_release_check_sha256 cli_release_download \
          cli_release_require_work_dir cli_unpack_vet_members cli_unpack_into \
          cli_layout_current_version cli_layout_adopt_legacy cli_layout_install_release cli_layout_switch \
          cli_layout_previous_version cli_layout_prune cli_layout_state_read cli_layout_state_write \
          cli_layout_check_consistent cli_layout_remove_partials \
          cli_dumps_dir cli_dumps_path cli_dumps_name cli_dumps_name_fits cli_dumps_list cli_dumps_newest_to \
          cli_dumps_prune cli_dumps_require_private \
          cli_release_notes_url_valid cli_release_date_valid cli_release_describe_notes; do
  declare -F "$fn" >/dev/null || selftest_failed "the library function ${fn} this selftest drives is not defined."
done
selftest_pass "every library function this selftest drives is defined by the copies"
release_base="$MARAN_RELEASE_BASE_URL"

# ---------------------------------------------------------------------------------------------
# Fixtures: throwaway keys and signed bundles
# ---------------------------------------------------------------------------------------------
keydir="${work}/keys"
install -d -m 0700 "$keydir"
for name in good attacker; do
  openssl ecparam -name secp384r1 -genkey -noout -out "${keydir}/${name}.key" >/dev/null 2>&1
  openssl pkey -in "${keydir}/${name}.key" -pubout -out "${keydir}/${name}.pub" >/dev/null 2>&1
done

target_arch="$(cli_release_arch)"
case "$target_arch" in
  x86_64) other_arch="aarch64" ;;
  aarch64) other_arch="x86_64" ;;
  *) selftest_failed "cli_release_arch answered '${target_arch}' on this host; the fixtures only exist for x86_64 and aarch64." ;;
esac

# resign: <bundle dir> <key>. Signs the manifest again after a deliberate edit of a SIGNED field.
resign() {
  openssl dgst -sha384 -sign "$2" -out "${1}/manifest.json.sig" "${1}/manifest.json"
}

# make_bundle: <dir> <version> <channel or - for none> <signing key> [evil kind]. Writes both
# architectures' archives (the other one marked, so picking it is visible), a manifest in the
# release builder's shape with the signed channel, notes_url and released fields, and its signature. Every component carries
# a 0666 file, a 6755 file and a 1777 directory, which a clean install must make safe. The evil kinds
# damage the api archive only: traversal, absolute, symlink-up, symlink-abs, symlink-benign, hardlink,
# fifo, no-entry (no api/Maran.Host) and not-executable. Each fixture proves it carries what it is
# about, so a refusal test cannot pass over an archive that lost its hostile member.
make_bundle() {
  local dir="$1" version="$2" channel="$3" key="$4" evil="${5:-}" arch component stage entry first=1 url_channel out
  url_channel="$channel"
  [ "$channel" != "-" ] || url_channel="stable"
  install -d "$dir"
  for arch in "$target_arch" "$other_arch"; do
    for component in api agent frontend; do
      case "$component" in api) entry="Maran.Host" ;; agent) entry="maran-agent" ;; frontend) entry="index.html" ;; esac
      [ "${component}:${evil}" != "api:no-entry" ] || entry="other.txt"
      stage="${work}/stage-${component}-${arch}-${version}-${evil:-clean}"
      out="${dir}/${component}-${arch}.tar.gz"
      rm -rf -- "$stage"
      install -d "$stage"
      echo "synthetic ${component} ${version} ${arch} selftest only" > "${stage}/${entry}"
      chmod 0755 "${stage}/${entry}"
      echo "$version" > "${stage}/version.txt"
      echo "any user may write me" > "${stage}/world-writable.txt"
      chmod 0666 "${stage}/world-writable.txt"
      echo "set-id" > "${stage}/setid-file"
      chmod 6755 "${stage}/setid-file"
      install -d -m 1777 "${stage}/world-dir"
      if [ "$component" != "api" ] || [ -z "$evil" ] || [ "$evil" = "no-entry" ]; then
        ( cd "$stage" && tar -czf "$out" . )
      else
        case "$evil" in
          traversal)
            echo "evil" > "${work}/selftest-evil-traversal"
            tar -P -czf "$out" -C "$stage" . ../selftest-evil-traversal
            tar -tPzf "$out" | grep -qxF '../selftest-evil-traversal' \
              || selftest_failed "fixture: the traversal archive does not carry its ../ member - the refusal test would be vacuous."
            ;;
          absolute)
            install -d "${work}/abs"
            echo "evil" > "${work}/abs/selftest-evil-absolute"
            tar -P -czf "$out" -C "$stage" . "${work}/abs/selftest-evil-absolute"
            tar -tPzf "$out" | grep -qxF "${work}/abs/selftest-evil-absolute" \
              || selftest_failed "fixture: the absolute-path archive does not carry its absolute member - the refusal test would be vacuous."
            ;;
          symlink-up|symlink-abs|symlink-benign)
            case "$evil" in
              symlink-up) ln -s ../../../../etc "${stage}/escape-link" ;;
              symlink-abs) ln -s /etc/passwd "${stage}/escape-link" ;;
              *) ln -s "$entry" "${stage}/escape-link" ;;
            esac
            ( cd "$stage" && tar -czf "$out" . )
            tar -tvzf "$out" | grep -qE -- 'escape-link -> ' \
              || selftest_failed "fixture: the ${evil} archive does not carry its symlink - the refusal test would be vacuous."
            ;;
          hardlink)
            ln "${stage}/${entry}" "${stage}/hardlink-alias"
            ( cd "$stage" && tar -czf "$out" . )
            tar -tvzf "$out" | grep -qE -- '^h' \
              || selftest_failed "fixture: the hardlink archive does not carry a hard-link member - the refusal test would be vacuous."
            ;;
          fifo)
            mkfifo "${stage}/pipe"
            ( cd "$stage" && tar -czf "$out" . )
            tar -tvzf "$out" | grep -qE -- '^p' \
              || selftest_failed "fixture: the fifo archive does not carry a FIFO member - the refusal test would be vacuous."
            ;;
          not-executable)
            chmod 0644 "${stage}/${entry}"
            ( cd "$stage" && tar -czf "$out" . )
            tar -tvzf "$out" | grep -qE -- '^-rw-r--r-- .* \./Maran\.Host$' \
              || selftest_failed "fixture: the not-executable archive still has an executable api entry - the refusal test would be vacuous."
            ;;
          *) selftest_failed "fixture: unknown evil kind ${evil}" ;;
        esac
      fi
    done
  done
  if [ "$evil" = "no-entry" ]; then
    ! tar -tzf "${dir}/api-${target_arch}.tar.gz" | grep -q 'Maran.Host' \
      || selftest_failed "fixture: the no-entry archive still carries Maran.Host - the refusal test would be vacuous."
  fi
  {
    echo "{"
    echo "  \"version\": \"${version}\","
    [ "$channel" = "-" ] || echo "  \"channel\": \"${channel}\","
    echo "  \"notes_url\": \"https://github.com/innovayse/maran/releases/tag/v${version}\","
    echo "  \"released\": \"2026-11-07\","
    echo "  \"artifacts\": {"
    for arch in "$target_arch" "$other_arch"; do
      for component in api agent frontend; do
        [ "$first" -eq 1 ] || echo "    },"
        first=0
        echo "    \"${component}-${arch}\": {"
        echo "      \"url\": \"${release_base}/${url_channel}/${component}-${arch}.tar.gz\","
        echo "      \"sha256\": \"$(sha_of "${dir}/${component}-${arch}.tar.gz")\""
      done
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${dir}/manifest.json"
  openssl dgst -sha384 -sign "$key" -out "${dir}/manifest.json.sig" "${dir}/manifest.json"
}

serve0="${work}/serve0"
make_bundle "${serve0}/stable" "1.0.0" "stable" "${keydir}/good.key"
make_bundle "${serve0}/beta" "1.1.0-beta.1" "beta" "${keydir}/good.key"

# new_case_dir: a fresh 0700 directory under the staging base; prints its path. Unique by mktemp,
# not by a counter: callers run this in a command substitution, where a counter would not survive.
new_case_dir() {
  mktemp -d "${staging_base}/case.XXXXXX"
}

# new_serve: copies the pristine serve tree; prints the copy, ready to be damaged.
new_serve() {
  local copy
  copy="$(mktemp -d "${work}/serve.XXXXXX")"
  cp -r "${serve0}/." "$copy/"
  printf '%s' "$copy"
}

# fresh_layout: an empty release layout as a new install leaves it (static symlinks, no release,
# no state), with the baked public key in place, and an empty dump directory slot.
fresh_layout() {
  rm -rf -- "$mar" "$etc" "$backup_dir"
  install -d "${mar}/releases" "${mar}/cli/keys" "$etc"
  install -m 0644 "${keydir}/good.pub" "${mar}/cli/keys/release-signing.pub"
  ln -s current/api "${mar}/api"
  ln -s current/agent "${mar}/agent"
  ln -s current/frontend "${mar}/frontend"
}

# fetch_all: <channel> <dir>. What `maran update` does before it unpacks.
fetch_all() {
  cli_release_fetch_manifest "$1" "$2"
  cli_release_fetch_artifacts "${2}/manifest.json" "$2"
}

# fetch_and_install: <channel> <version> <dir> [replace].
fetch_and_install() {
  fetch_all "$1" "$3"
  cli_layout_install_release "$2" "$3" ${4:+"$4"}
}

# make_offline: <bundle dir> <out tarball> [skip-sig]. The shape `maran release package` writes: the
# manifest, its signature and the three unsuffixed archives at the root, members named one by one.
make_offline() {
  local bundle="$1" out="$2" tmp component
  tmp="$(mktemp -d "${work}/offline.XXXXXX")"
  cp "${bundle}/manifest.json" "$tmp/"
  [ "${3:-}" = "skip-sig" ] || cp "${bundle}/manifest.json.sig" "$tmp/"
  for component in api agent frontend; do
    cp "${bundle}/${component}-${target_arch}.tar.gz" "${tmp}/${component}.tar.gz"
  done
  if [ "${3:-}" = "skip-sig" ]; then
    ( cd "$tmp" && tar -czf "$out" manifest.json api.tar.gz agent.tar.gz frontend.tar.gz )
  else
    ( cd "$tmp" && tar -czf "$out" manifest.json manifest.json.sig api.tar.gz agent.tar.gz frontend.tar.gz )
  fi
}

fresh_layout
