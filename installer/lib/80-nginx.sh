#!/usr/bin/env bash
# Step 80: install the panel's nginx vhost (installer/nginx/maran.conf) with a
# self-signed certificate, listening on 8443. Config is rendered to a temp file,
# validated with `nginx -t`, then atomically moved into place — the same
# render/validate/atomic-rename discipline the agent uses for customer configs
# (rules/architecture.md), so a bad render never reaches a live nginx.
set -euo pipefail

readonly MARAN_TLS_DIR="/etc/maran/tls"
readonly MARAN_CERT_PATH="${MARAN_TLS_DIR}/panel.crt"
readonly MARAN_KEY_PATH="${MARAN_TLS_DIR}/panel.key"

nginx_conf_dest() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "/etc/nginx/conf.d/maran.conf" ;;
    rhel)   echo "/etc/nginx/conf.d/maran.conf" ;;
  esac
}

# generate_self_signed_cert: a 10-year self-signed cert scoped to the machine's
# hostname, generated once. It exists only so the panel is reachable over TLS
# immediately after install; the operator can point a real hostname at Let's Encrypt
# later per the design's update path. Skipped if a cert already exists (idempotent;
# re-running the installer must not silently invalidate a cert an operator already
# swapped in, e.g. after enabling Let's Encrypt).
generate_self_signed_cert() {
  if [ -f "$MARAN_CERT_PATH" ] && [ -f "$MARAN_KEY_PATH" ]; then
    echo "TLS certificate already present, leaving it in place."
    return
  fi
  install -d -o root -g panel -m 0750 "$MARAN_TLS_DIR"
  local hostname
  hostname="$(hostname -f 2>/dev/null || hostname)"
  openssl req -x509 -nodes -newkey ed25519 \
    -keyout "$MARAN_KEY_PATH" -out "$MARAN_CERT_PATH" \
    -days 3650 -subj "/CN=${hostname}" \
    -addext "subjectAltName=DNS:${hostname}"
  chown root:panel "$MARAN_KEY_PATH" "$MARAN_CERT_PATH"
  chmod 0640 "$MARAN_KEY_PATH"
  chmod 0644 "$MARAN_CERT_PATH"
}

# render_vhost: substitutes the placeholders in the shipped template into a temp file.
render_vhost() {
  local out="$1" hostname
  hostname="$(hostname -f 2>/dev/null || echo "_")"
  sed \
    -e "s#__MARAN_SERVER_NAME__#${hostname}#g" \
    -e "s#__MARAN_CERT_PATH__#${MARAN_CERT_PATH}#g" \
    -e "s#__MARAN_KEY_PATH__#${MARAN_KEY_PATH}#g" \
    "${LIB_DIR}/../nginx/maran.conf" > "$out"
}

step_nginx() {
  echo "Installing nginx vhost on port 8443..."
  generate_self_signed_cert

  local dest tmp
  dest="$(nginx_conf_dest)"
  tmp="$(mktemp)"
  render_vhost "$tmp"

  # Validate against the real nginx config tree before touching the live file: copy
  # into place under a throwaway name is unnecessary because `nginx -t` reads the
  # actual conf.d directory, so we render straight to a staging path inside it, test,
  # then rename over the final name only on success.
  local staging="${dest}.staging"
  install -m 0644 "$tmp" "$staging"
  rm -f "$tmp"
  if ! nginx -t 2>&1; then
    rm -f "$staging"
    echo "80-nginx.sh: rendered vhost failed 'nginx -t' validation; aborting without touching the live config." >&2
    exit 1
  fi
  mv -f "$staging" "$dest"

  systemctl enable nginx
  systemctl reload nginx 2>/dev/null || systemctl restart nginx
  echo "nginx vhost installed at ${dest}; panel reachable on port 8443."
}
