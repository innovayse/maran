#!/usr/bin/env bash
# The bootstrap behind `curl -sSL https://get.maran.innovayse.com | bash`.
#
# WHY THIS FILE EXISTS. The README's headline command could not work without it, and the failure
# was not a small one. `install.sh` is not a single script: it resolves SCRIPT_DIR from
# BASH_SOURCE and sources every step from `lib/`, its nginx and systemd templates from beside
# itself, and the release signing public key from `keys/release-signing.pub`. Piped through a
# shell, BASH_SOURCE is unset, SCRIPT_DIR collapses to the caller's working directory, and the
# first step is looked for at `./lib/10-preflight.sh` — which is not there. Nothing checked for
# that, so an operator following the README would have reached a root shell and then a cryptic
# `no such file` from `.`, halfway into an install.
#
# So what the download endpoint serves is this: a script whose whole job is to fetch the
# installer PACKAGE, prove it is the one we published, unpack it into a private directory, and
# hand over to the real installer from there.
#
# WHAT IT REFUSES TO DO. It does not verify with a key it downloaded alongside the thing it is
# verifying — that proves nothing, and is the shape of every "signed" download that is not.
# The checksum it compares against is baked into THIS file at publish time, and this file is
# fetched over TLS from a host the operator typed. That is the trust root, stated plainly rather
# than implied: whoever controls that host and that TLS certificate controls what runs as root
# here. An operator who wants a stronger one downloads the tarball and its signature by hand and
# runs `install.sh --offline-tarball`, which verifies against the key shipped inside the package.
set -euo pipefail

readonly INSTALLER_URL="${MARAN_INSTALLER_URL:-https://releases.maran.innovayse.com/installer/maran-installer.tar.gz}"
# Replaced at publish time by `maran release publish-get`. A literal here rather than a second
# download, for the reason in the header.
readonly INSTALLER_SHA256="${MARAN_INSTALLER_SHA256:-REPLACED_AT_PUBLISH_TIME}"

fail() { echo "maran-get: $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || fail "must be run as root (try: curl -sSL <url> | sudo bash)"

for tool in curl tar sha256sum; do
  command -v "$tool" >/dev/null 2>&1 || fail "missing required tool: $tool"
done

work="$(mktemp -d)"
# The trap fires on success and on every failure alike: a half-unpacked installer left in /tmp is
# the next person's confusing artefact.
trap 'rm -rf -- "$work"' EXIT INT TERM

echo "Fetching the Maran installer from ${INSTALLER_URL}"
curl --fail --silent --show-error --location --proto '=https' --tlsv1.2 \
  --output "${work}/installer.tar.gz" "$INSTALLER_URL" \
  || fail "download failed — nothing has been changed on this host"

# Checked by SHAPE, not by comparison with the placeholder text. The publish step stamps the real
# checksum with a substitution, and a substitution that matched the sentinel in BOTH the assignment
# and a comparison against it turned this guard into `sha = sha` — always true, so the published
# script refused every download. That is not hypothetical: it is what the first stamped copy did,
# and it was found by running it rather than by reading it. A sha256 is 64 lowercase hex digits and
# no placeholder can be mistaken for one.
case "$INSTALLER_SHA256" in
  [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]) ;;
  *) fail "this copy of get.sh was never published: its expected checksum is not a sha256.
       Refusing rather than running an unverified installer as root." ;;
esac

actual="$(sha256sum "${work}/installer.tar.gz" | cut -d' ' -f1)"
if [ "$actual" != "$INSTALLER_SHA256" ]; then
  fail "checksum mismatch — expected ${INSTALLER_SHA256}, got ${actual}.
       This can mean a corrupted download or a compromised mirror. Nothing has been changed on
       this host. Do not retry blindly."
fi

tar -xzf "${work}/installer.tar.gz" -C "$work"
[ -x "${work}/installer/install.sh" ] || fail "the package does not contain installer/install.sh"

# The channel this endpoint publishes. install.sh defaults to `stable`, and what is published
# today is a BETA — so a bootstrap that did not say so would send every `curl | bash` operator to
# a directory that does not exist, or worse, to a stable channel that does and is older. The
# operator's own --channel wins: this only supplies the default.
channel_given=0
for arg in "$@"; do
  [ "$arg" = "--channel" ] && channel_given=1
done

echo "Verified. Handing over to the installer."
if [ "$channel_given" -eq 1 ]; then
  exec bash "${work}/installer/install.sh" "$@"
fi
exec bash "${work}/installer/install.sh" --channel "${MARAN_GET_CHANNEL:-beta}" "$@"
