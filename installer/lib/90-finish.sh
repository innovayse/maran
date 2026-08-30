#!/usr/bin/env bash
# Step 90: report the result of the install and hand over the one-time setup token.
#
# The token was generated in 60-config.sh and lives only in /etc/maran/panel.env
# (root:panel 0640); this step reads it back. No password is printed, because none
# exists yet — the administrator sets one during first sign-in, so no credential of any
# kind reaches the log or the shell history.
#
# The /setup page itself ships with authentication, which is the NEXT plan, not this
# one. Until then this step prints the token and says so rather than printing a link
# that answers 404: an installer that ends by telling the operator to open a page that
# does not exist teaches them not to trust anything else it says.
set -euo pipefail

step_finish() {
  local token hostname
  # Raw remainder of the line after the first '=' — splitting on '=' and reassembling
  # prefixed the token with a space (awk's OFS) and dropped any '=' padding, handing the
  # operator a token that would not have been accepted.
  token="$(awk 'index($0, "Setup__Token=") == 1 { print substr($0, 14); exit }' /etc/maran/panel.env)"
  hostname="$(hostname -f 2>/dev/null || hostname)"

  # The token is, by itself, permission to create the first administrator. It goes to the
  # terminal on fd 3 (opened by install.sh before logging was redirected) so it never lands
  # in /var/log/maran/install.log, which outlives the install and is readable by anyone in
  # the log directory's group.
  # The link, not just the token: /setup reads ?token= and prefills the field, so the operator
  # pastes one thing instead of transcribing a 48-character secret by hand. Printed to the
  # terminal only — never to the install log, which is world-readable for support purposes.
  local setup_url="https://${hostname}:8443/setup?token=${token}"
  if [ -w /dev/fd/3 ] 2>/dev/null; then
    printf '\nCreate the first administrator here (one time only):\n\n  %s\n\n' "$setup_url" >&3
  else
    printf '\nCreate the first administrator here (one time only):\n\n  %s\n\n' "$setup_url" > /dev/tty
  fi

  cat <<EOF

Maran is installed and reachable at https://${hostname}:8443/

Open the link above to create the first administrator. It carries a one-time token that
stops working the moment the panel has a user, so it is worth nothing to anyone who finds
it afterwards — but until then it grants the whole server, so do not paste it into a chat
or a ticket. It was printed on this terminal only and is deliberately absent from the
install log.

If you lose it, read Setup__Token from /etc/maran/panel.env (root:panel 0640), or re-run
the installer to issue a new one.

The certificate is self-signed until you point a real hostname at this server, so your
browser will warn once. That is expected on a fresh install.

Next steps:
  - Confirm both services are healthy: systemctl status maran-api maran-agent
  - Confirm the panel answers:        curl -k https://${hostname}:8443/health
  - The full install log is at /var/log/maran/install.log

EOF
}
