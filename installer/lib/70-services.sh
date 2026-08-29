#!/usr/bin/env bash
# Step 70: install the hardened systemd units from installer/systemd/ and enable them.
# Does not start them yet — 80-nginx.sh must install the vhost first so the api's first
# health check has something proxying to it, and both are started together at the end
# of this step's counterpart call order in install.sh (api after nginx is configured
# is not required by systemd itself, only by operator expectation, so we start here
# and let 80-nginx.sh reload nginx afterward).
set -euo pipefail

readonly MARAN_UNIT_DIR="/etc/systemd/system"

install_units() {
  install -m 0644 "${LIB_DIR}/../systemd/maran-api.service" "${MARAN_UNIT_DIR}/maran-api.service"
  install -m 0644 "${LIB_DIR}/../systemd/maran-agent.service" "${MARAN_UNIT_DIR}/maran-agent.service"
}

step_services() {
  echo "Installing systemd units..."
  install_units
  systemctl daemon-reload
  systemctl enable maran-agent.service maran-api.service
  # Start the agent first: the api's health check depends on an agent handshake, and
  # starting order here matches the After= dependency declared in maran-api.service.
  systemctl restart maran-agent.service
  systemctl restart maran-api.service
  echo "Services installed and started."
}
