#!/usr/bin/env bash
# Step 60: generate /etc/maran/panel.env (root:panel 0640) with a freshly generated
# 256-bit encryption key and the PostgreSQL connection string. This is Maran's one
# secrets file (rules/security.md: "New config values with secrets go to panel.env, not
# appsettings"). The key is generated here, on the customer's machine, at install time —
# it never ships in the repository or the release artifacts.
set -euo pipefail

readonly MARAN_CONFIG_DIR="/etc/maran"
readonly MARAN_CONFIG_FILE="${MARAN_CONFIG_DIR}/panel.env"

# generate_encryption_key: 256 bits of randomness from the kernel CSPRNG, base64-encoded
# (matches the shape DataProtection/appsettings already expect — see scripts/dev-env.sh's
# throwaway dev key for the same encoding). Never echoed to the log: this function's
# stdout is captured directly into a variable by the caller, not printed.
generate_encryption_key() {
  openssl rand -base64 32
}

# generate_setup_token: a one-time, unguessable token used by 90-finish.sh to build the
# first-admin setup URL. Generated here (alongside the other secrets) so it lands in the
# same 0640 file and never touches stdout/the install log.
generate_setup_token() {
  openssl rand -hex 24
}

# write_config: renders panel.env to a temp file first, then atomically renames it into
# place, so a crash mid-write can never leave a half-written secrets file readable by
# the wrong mode/owner. Preserves an already-generated encryption key on re-run (see
# step_config) instead of rotating it, since rotating silently would break existing
# encrypted data in PostgreSQL.
write_config() {
  local encryption_key="$1" setup_token="$2" tmp
  tmp="$(mktemp)"
  {
    echo "# Managed by the Maran installer. Do not edit by hand; re-running the"
    echo "# installer regenerates this file except for values marked 'preserved on re-run'."
    echo "#"
    echo "# Names use the .NET convention where '__' is configuration nesting, so Database__Host"
    echo "# sets the 'Database:Host' setting. They must match what the panel actually reads —"
    echo "# see backend/src/Maran.Host/Configuration and installer/panel.env.example."
    echo ""
    echo "# Database over the local unix socket: Host is the socket DIRECTORY, so no port and no"
    echo "# password apply — PostgreSQL authenticates by operating-system user (peer auth)."
    echo "Database__Host=/var/run/postgresql"
    echo "Database__Database=maran"
    echo "Database__Username=panel"
    echo ""
    echo "# Preserved on re-run: rotating this key without re-encrypting makes stored secrets unreadable."
    echo "Security__EncryptionKey=${encryption_key}"
    echo ""
    echo "# One-time token authorizing first-administrator creation in the browser."
    echo "Setup__Token=${setup_token}"
    echo ""
    echo "# The api listens on loopback only; nginx terminates TLS on 8443 and proxies to it (see installer/nginx/maran.conf: the port here and the proxy_pass there must match)."
    echo "ASPNETCORE_URLS=http://127.0.0.1:5080"
  } > "$tmp"
  chown root:panel "$tmp"
  chmod 0640 "$tmp"
  mv -f "$tmp" "$MARAN_CONFIG_FILE"
}

# existing_value: reads one KEY=value out of an already-existing panel.env, used to
# preserve the encryption key (and only the encryption key) across re-runs.
existing_value() {
  local key="$1"
  [ -f "$MARAN_CONFIG_FILE" ] || return 0
  awk -F= -v k="$key" '$1==k { $1=""; sub(/^=/,""); print; exit }' "$MARAN_CONFIG_FILE"
}

step_config() {
  echo "Generating ${MARAN_CONFIG_FILE}..."
  install -d -o root -g panel -m 0750 "$MARAN_CONFIG_DIR"

  local key token
  key="$(existing_value Security__EncryptionKey)"
  if [ -z "$key" ]; then
    key="$(generate_encryption_key)"
    echo "Generated a new encryption key."
  else
    echo "Preserving existing encryption key from a previous install run."
  fi

  # The setup token IS rotated on every re-run before first admin creation completes,
  # so an interrupted install never leaves a stale, possibly-leaked token valid.
  token="$(generate_setup_token)"

  write_config "$key" "$token"
  echo "Config written with mode 0640, owner root:panel. Secret values were not logged."
}
