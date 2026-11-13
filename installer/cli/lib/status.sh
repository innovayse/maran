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

# The list a published release records of the public keys it pins, written beside the panel binary by
# `maran release build` (scripts/lib/release-build-components.sh) and covered by the release's integrity
# manifest. One `<file name> <sha256>` per line.
readonly MARAN_CLI_PINNED_KEYS_FILE="/usr/local/maran/api/pinned-keys.txt"

# cli_status_pinned_keys: prints which public keys this release pins, as name and the first twelve hex
# characters of the SHA-256 (a fingerprint), never any key content. A line that is not exactly a plain
# `name.pub` and 64 hex characters is skipped, so a damaged file prints nothing it should not. A missing
# file is information (a development build records none), not ill health.
cli_status_pinned_keys() {
  local name sha shown=0
  echo "Pinned keys (name, SHA-256 fingerprint):"
  if [ -f "$MARAN_CLI_PINNED_KEYS_FILE" ] && [ ! -L "$MARAN_CLI_PINNED_KEYS_FILE" ]; then
    while read -r name sha; do
      [[ "$name" =~ ^[a-z-]+\.pub$ && "$sha" =~ ^[0-9a-f]{64}$ ]] || continue
      printf '  %-28s %s\n' "$name" "${sha:0:12}"
      shown=$((shown + 1))
    done < "$MARAN_CLI_PINNED_KEYS_FILE"
  fi
  [ "$shown" -gt 0 ] || echo "  none recorded (this build does not list its pinned keys)"
}

# cli_status: the verb. Takes no arguments.
cli_status() {
  [ "$#" -eq 0 ] || cli_fail "status takes no arguments"
  local healthy=0 unit licence_lines

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
  else
    # Read-only lines: the licence, then (only when it is close to expiring) the warning with the renewal
    # steps, then this server's id (the value a licence is bound to, which the marketplace asks for when
    # one is claimed). A licence that is absent, lapsed or about to expire is information, not ill health,
    # so a failure to print them never changes the exit status.
    # The lines after the first belong to the panel's report, so they are indented under it.
    licence_lines=$(cli_dotnet licence-status) || licence_lines="unavailable"
    printf 'Licence: %s\n' "$(printf '%s\n' "$licence_lines" | sed -n '1p')"
    printf '%s\n' "$licence_lines" | sed -n '2,$p' | sed 's/^/  /'
  fi
  echo
  cli_status_pinned_keys
  return "$healthy"
}
