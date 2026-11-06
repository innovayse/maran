#!/usr/bin/env bash
# Input and key guards for `maran deploy-local` (issue #410).
#
# SOURCED by scripts/lib/deploy-local.sh and its selftest, after scripts/lib/release-bundle.sh has
# set `root` (the repository root). Every function prints its reasons to stderr and returns 0 for
# "accepted" or 1 for "refused"; none of them exits, so the caller decides what a refusal costs.
#
# WHY IT EXISTS. The verb handles a private signing key and builds ssh/scp command lines out of
# values that come from arguments or a local file. Both are attack surfaces (the threat note
# docs/superpowers/notes/2026-11-06-deploy-local-threat-note.md, points 1, 3 and 4): the production
# key must be refused before it is opened, a host must never be able to look like an ssh option, and a
# config file must never be able to carry a password or run code.

# deploy_resolve: <path>. The absolute path with symlinks and `..` resolved, even when the file does
# not exist (`realpath -m`). Prints nothing and returns 1 when realpath is unavailable or fails.
deploy_resolve() {
  realpath -m -- "$1" 2>/dev/null
}

# deploy_path_under: <path> <directory>. Returns 0 when the first is the second or sits below it.
# Compares whole components, so /a/bc is not under /a/b.
deploy_path_under() {
  local path="$1" dir="${2%/}"
  [[ -n "$dir" && ( "$path" == "$dir" || "$path" == "$dir"/* ) ]]
}

# deploy_path_within: <path> <directory>. Returns 0 when the path, as written or resolved, is the
# directory or sits below it, the directory taken as written or resolved. All four pairings are tried
# so a symlinked checkout, a symlinked key and a `..` spelling each fail to bypass the comparison.
deploy_path_within() {
  local path="$1" dir="$2" rpath rdir
  rpath="$(deploy_resolve "$path")" || rpath=""
  rdir="$(deploy_resolve "$dir")" || rdir=""
  deploy_path_under "$path" "$dir" && return 0
  [[ -n "$rpath" ]] && deploy_path_under "$rpath" "$dir" && return 0
  [[ -n "$rdir" ]] && deploy_path_under "$path" "$rdir" && return 0
  [[ -n "$rpath" && -n "$rdir" ]] && deploy_path_under "$rpath" "$rdir"
}

# deploy_pub_digest_of_key: <private key>. sha256 of the DER of the public half derived from the key.
# DER rather than the PEM text, so line wrapping and headers can never make two equal keys differ.
# Fails (nothing printed) when openssl cannot read the key.
deploy_pub_digest_of_key() {
  openssl pkey -in "$1" -pubout -outform DER 2>/dev/null </dev/null | sha256sum | cut -d' ' -f1 \
    | { read -r d; [[ -n "$d" && "$d" != "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" ]] && echo "$d"; }
}

# deploy_pub_digest_of_pub: <public key file>. sha256 of the DER of a PEM public key. Fails when
# openssl cannot read it.
deploy_pub_digest_of_pub() {
  openssl pkey -pubin -in "$1" -outform DER 2>/dev/null </dev/null | sha256sum | cut -d' ' -f1 \
    | { read -r d; [[ -n "$d" && "$d" != "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" ]] && echo "$d"; }
}

# deploy_refuse_production_path: <path> <label>. Returns 1 when the path, as written or resolved, is
# under $HOME/.maran-release (the production release key directory, as written or resolved). The
# decision is made on the path ALONE, so nothing is opened. The label (signing key, ssh identity)
# names what was refused. One spelling of this rule serves the signing key and the ssh identity.
deploy_refuse_production_path() {
  local path="${1:-}" label="${2:-path}"
  if [[ -z "${HOME:-}" ]]; then
    echo "deploy-local: refused - HOME is not set, so the $label cannot be checked against the production key directory" >&2
    return 1
  fi
  if deploy_path_within "$path" "$HOME/.maran-release"; then
    echo "deploy-local: refused - the $label lives in the production release key directory; use a dedicated test key outside it (maran deploy-local --help)" >&2
    return 1
  fi
}

# deploy_require_outside_repo: <path> <option class>. Refuses a path that, as written or resolved, is
# under the repository root as written or resolved (a symlinked checkout does not bypass it). The
# class (key, identity, config) names the option in the message. A key, an identity or a config inside
# the checkout is one `git add` from being published.
deploy_require_outside_repo() {
  local path="${1:-}" class="${2:-path}"
  if [[ -z "$(deploy_resolve "$path")" ]]; then
    echo "deploy-local: refused - the $class path cannot be resolved" >&2
    return 1
  fi
  if deploy_path_within "$path" "${root:?root is not set}"; then
    echo "deploy-local: refused - the $class file must live outside the repository (it is one 'git add' from being published)" >&2
    return 1
  fi
}

# deploy_refuse_production_key: <key> <repo public key>. Returns 0 only for a readable, unencrypted
# P-384 key that is not the production release key. Refusal order matters: the path rules (1) are
# decided on the path ALONE, before the file is opened, so the production key is not even read; the
# public-half comparison (6) catches a copy of it kept anywhere else. Refused when:
#  1. the path is under $HOME/.maran-release (deploy_refuse_production_path) or under the repository
#     (deploy_require_outside_repo), or equals the resolved MARAN_RELEASE_SIGNING_KEY;
#  2. it is not a readable regular file;
#  3. its first lines say ENCRYPTED (openssl would prompt for a passphrase on the terminal);
#  4. openssl does not report a P-384 (secp384r1) key;
#  5. either public-half digest cannot be computed (an unreadable key is refused, never assumed fine,
#     and exactly one side empty is refused like both empty);
#  6. the sha256 of its public half (DER) equals that of the repository public key.
# Every openssl call reads </dev/null, so none can ever wait on the terminal.
deploy_refuse_production_key() {
  local key="${1:-}" repo_pub="${2:-}" resolved env_key key_digest pub_digest
  if [[ -z "$key" || -z "$repo_pub" ]]; then
    echo "deploy-local: refused - no signing key or repository public key given" >&2
    return 1
  fi
  resolved="$(deploy_resolve "$key")" || resolved=""
  if [[ -z "$resolved" ]]; then
    echo "deploy-local: refused - the signing key path cannot be resolved" >&2
    return 1
  fi
  deploy_refuse_production_path "$key" "signing key" || return 1
  deploy_require_outside_repo "$key" "signing key" || return 1
  if [[ -n "${MARAN_RELEASE_SIGNING_KEY:-}" ]]; then
    env_key="$(deploy_resolve "$MARAN_RELEASE_SIGNING_KEY")" || env_key=""
    if [[ -n "$env_key" && "$resolved" == "$env_key" ]]; then
      echo "deploy-local: refused - the signing key is the production release key named by MARAN_RELEASE_SIGNING_KEY" >&2
      return 1
    fi
  fi
  if [[ ! -f "$resolved" || ! -r "$resolved" ]]; then
    echo "deploy-local: refused - the signing key is not a readable regular file" >&2
    return 1
  fi
  if head -n 5 -- "$resolved" 2>/dev/null | grep -q 'ENCRYPTED'; then
    echo "deploy-local: refused - the signing key is passphrase-protected; use an unencrypted test key" >&2
    return 1
  fi
  if ! openssl pkey -in "$resolved" -text -noout 2>/dev/null </dev/null | grep -Eqi 'secp384r1|P-384'; then
    echo "deploy-local: refused - the signing key is not a readable P-384 (secp384r1) key" >&2
    return 1
  fi
  key_digest="$(deploy_pub_digest_of_key "$resolved")" || key_digest=""
  pub_digest="$(deploy_pub_digest_of_pub "$repo_pub")" || pub_digest=""
  if [[ -z "$key_digest" || -z "$pub_digest" ]]; then
    echo "deploy-local: refused - could not derive the public half to compare against the repository key" >&2
    return 1
  fi
  if [[ "$key_digest" == "$pub_digest" ]]; then
    echo "deploy-local: refused - this is the production release key (its public half equals installer/keys/release-signing.pub); a development host must trust a separate test key" >&2
    return 1
  fi
  return 0
}

# deploy_valid_host: <host>. A DNS name or IPv4 address: letters, digits, dot, underscore and hyphen,
# starting with a letter or digit (so it can never be read as an ssh option), at most 253 characters.
# A colon is not allowed: scp would split an IPv6 literal at it, so use a DNS name for such a host.
deploy_valid_host() {
  local h="${1:-}"
  if [[ ${#h} -gt 253 || ! "$h" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
    echo "deploy-local: refused - '$h' is not an acceptable host name (letters, digits, '.', '_', '-'; must not start with '-')" >&2
    return 1
  fi
}

# deploy_valid_user: <user>. A portable account name: lowercase letter or underscore first, then
# lowercase letters, digits, underscore or hyphen, at most 32 characters.
deploy_valid_user() {
  local u="${1:-}"
  if [[ ${#u} -gt 32 || ! "$u" =~ ^[a-z_][a-z0-9_-]*$ ]]; then
    echo "deploy-local: refused - '$u' is not an acceptable user name" >&2
    return 1
  fi
}

# deploy_valid_port: <port>. Digits only, between 1 and 65535.
deploy_valid_port() {
  local p="${1:-}"
  if [[ ! "$p" =~ ^[0-9]{1,5}$ ]] || (( 10#$p < 1 || 10#$p > 65535 )); then
    echo "deploy-local: refused - '$p' is not a port between 1 and 65535" >&2
    return 1
  fi
}

# deploy_valid_stage_path: <path>. The remote staging directory, accepted only in the exact shape the
# verb asks the host to create (`mktemp -d /tmp/maran-deploy-stage.XXXXXXXXXX`): /tmp/maran-deploy-stage.
# followed by exactly ten letters or digits. It is fed back into later remote commands, so any other
# shape - a different directory, a '..', a longer or shorter suffix - is refused.
deploy_valid_stage_path() {
  local p="${1:-}" pattern='^/tmp/maran-deploy-stage\.[A-Za-z0-9]{10}$'
  if [[ ! "$p" =~ $pattern ]]; then
    echo "deploy-local: refused - the remote staging path is not /tmp/maran-deploy-stage.<10 characters>" >&2
    return 1
  fi
}

# deploy_expand_home: <key> <value>. Prints the value with a leading `~/` replaced by "$HOME/"; any
# other value is printed unchanged. A `~user/` or bare `~` form is refused (returns 1): only the
# caller's own home is ever expanded, and only for the identity and key paths, so the repository
# containment check and ssh see the same path.
deploy_expand_home() {
  local k="$1" v="$2"
  if [[ "$v" == "~/"* ]]; then
    if [[ -z "${HOME:-}" || "$HOME" != /* ]]; then
      echo "deploy-local: refused - config $k uses '~/' but HOME is not an absolute path" >&2
      return 1
    fi
    printf '%s' "$HOME/${v:2}"
  elif [[ "$v" == "~"* ]]; then
    echo "deploy-local: refused - config $k: only '~/' is expanded, not '$v'; write the absolute path" >&2
    return 1
  else
    printf '%s' "$v"
  fi
}

# deploy_valid_config_value: <key> <value>. Validates one value by the key it belongs to: host, user
# and port with the checks above, arch against its two names, identity and key as non-empty paths
# that do not start with '-'. Returns 1 with a reason on stderr otherwise.
deploy_valid_config_value() {
  local k="$1" v="$2"
  case "$k" in
    host) deploy_valid_host "$v" ;;
    user) deploy_valid_user "$v" ;;
    port) deploy_valid_port "$v" ;;
    arch)
      if [[ "$v" != "x86_64" && "$v" != "aarch64" ]]; then
        echo "deploy-local: refused - config arch '$v' must be x86_64 or aarch64" >&2
        return 1
      fi ;;
    identity | key)
      if [[ -z "$v" || "$v" == -* ]]; then
        echo "deploy-local: refused - config $k must be a non-empty path not starting with '-'" >&2
        return 1
      fi ;;
  esac
}

# deploy_read_config: <file>. Strict reader for the deploy-local config. The file must be a regular,
# non-symlink file owned by the caller and not writable by group or others. Lines are `key=value`,
# `#` comments or blank. Allowed keys: host user port identity key arch. Anything else is refused BY
# NAME; password-like keys get an explicit message. Prints the validated `key=value` lines on stdout.
# A leading `~/` in identity and key is expanded to "$HOME/" first (deploy_expand_home), so the
# values checked are the paths ssh will use. It never sources or evals the file: values are only ever
# data. Returns 1 on the first problem.
deploy_read_config() {
  local file="${1:-}" line k v mode
  if [[ -L "$file" || ! -f "$file" ]]; then
    echo "deploy-local: refused - config '$file' is not a regular file (symlinks are not followed)" >&2
    return 1
  fi
  if [[ "$(stat -c %u -- "$file")" != "$(id -u)" ]]; then
    echo "deploy-local: refused - config '$file' is not owned by you" >&2
    return 1
  fi
  mode="$(stat -c %a -- "$file")"
  if (( (8#$mode & 8#022) != 0 )); then
    echo "deploy-local: refused - config '$file' is writable by group or others (mode $mode); chmod 600 it" >&2
    return 1
  fi
  local out=""
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    line="${line#"${line%%[![:space:]]*}"}"
    [[ -z "$line" || "$line" == \#* ]] && continue
    if [[ "$line" != *=* ]]; then
      echo "deploy-local: refused - config line is not key=value" >&2
      return 1
    fi
    k="${line%%=*}"
    v="${line#*=}"
    k="${k%"${k##*[![:space:]]}"}"
    v="${v#"${v%%[![:space:]]*}"}"
    v="${v%"${v##*[![:space:]]}"}"
    case "${k,,}" in
      host | user | port | identity | key | arch) k="${k,,}" ;;
      password | pass | passwd | sudo_password | token)
        echo "deploy-local: refused - config key '$k': this file never holds a password; use key auth or --ask-sudo-password" >&2
        return 1 ;;
      *)
        echo "deploy-local: refused - unknown config key '$k' (allowed: host user port identity key arch)" >&2
        return 1 ;;
    esac
    if [[ "$k" == "identity" || "$k" == "key" ]]; then
      v="$(deploy_expand_home "$k" "$v")" || return 1
    fi
    deploy_valid_config_value "$k" "$v" || return 1
    out+="$k=$v"$'\n'
  done < "$file"
  printf '%s' "$out"
}
