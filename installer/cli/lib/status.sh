#!/usr/bin/env bash
# status.sh: `maran status` — what is running, and what the panel itself says about its health.
#
# Two sections, and the second may fail without taking the first with it: the systemd facts need
# only systemd, so an operator whose database or panel is broken still learns which services are
# down. Exit 0 when every section is healthy, 1 otherwise. Prints no secret and writes no log line.

# cli_status_unit: prints one `unit: active/enabled` line and returns non-zero when the unit is not
# active. A unit that does not exist is reported as such rather than as a failure of the command.
cli_status_unit() {
  local unit="$1" active enabled
  active="$(systemctl is-active -- "$unit" 2>/dev/null || true)"
  enabled="$(systemctl is-enabled -- "$unit" 2>/dev/null || true)"
  printf '  %-22s %s / %s\n' "$unit" "${active:-unknown}" "${enabled:-not-installed}"
  [ "$active" = "active" ]
}

# cli_status: the verb. Takes no arguments.
cli_status() {
  [ "$#" -eq 0 ] || cli_fail "status takes no arguments"
  local healthy=0 unit

  echo "Services (active / enabled):"
  for unit in "$MARAN_CLI_API_UNIT" "$MARAN_CLI_AGENT_UNIT" postgresql.service nginx.service; do
    cli_status_unit "$unit" || healthy=1
  done
  # FTPS ships switched off; it is only a health question once an administrator has enabled it.
  if systemctl is-enabled --quiet maran-ftps.service 2>/dev/null; then
    cli_status_unit maran-ftps.service || healthy=1
  fi
  # DNS ships switched off for the same reason.
  if systemctl is-enabled --quiet maran-dns.service 2>/dev/null; then
    cli_status_unit maran-dns.service || healthy=1
  fi

  echo
  echo "Panel:"
  if ! cli_dotnet status; then
    echo "  The panel's own report is unavailable (database or binary not working)."
    echo "  The service facts above are still accurate."
    healthy=1
  fi
  return "$healthy"
}
