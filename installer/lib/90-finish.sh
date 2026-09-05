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

# panel_env_value: the raw remainder of one KEY= line of the generated panel.env, the same
# way 60-config.sh reads its own file back — splitting on '=' and reassembling prefixed the
# value with a space and dropped any '=' padding, which once handed the operator a setup
# token that would not have been accepted.
panel_env_value() {
  awk -v k="$1" 'index($0, k "=") == 1 { print substr($0, length(k) + 2); exit }' /etc/maran/panel.env
}

step_finish() {
  local token hostname whitelist_seed
  token="$(panel_env_value Setup__Token)"
  hostname="$(hostname -f 2>/dev/null || hostname)"
  # Absent whenever the install saw no client address to seed the firewall whitelist with.
  whitelist_seed="$(panel_env_value Firewall__SeedWhitelistCidr)"

  # The token is, by itself, permission to create the first administrator. It goes to the
  # terminal on fd 3 (opened by install.sh before logging was redirected) so it never lands
  # in /var/log/maran/install.log, which outlives the install and is readable by anyone in
  # the log directory's group.
  # The link, not just the token: /setup reads ?token= and prefills the field, so the operator
  # pastes one thing instead of transcribing a 48-character secret by hand. Printed to the
  # terminal only — never to the install log, which is world-readable for support purposes.
  # The port comes from install.sh's MARAN_PANEL_PORT, not from a literal: a URL printed
  # with a port nginx is not listening on sends the operator to a connection refusal on
  # the one screen they must reach, and it would go wrong the first time the port changes.
  local setup_url="https://${hostname}:${MARAN_PANEL_PORT}/setup?token=${token}"
  if [ -w /dev/fd/3 ] 2>/dev/null; then
    printf '\nCreate the first administrator here (one time only):\n\n  %s\n\n' "$setup_url" >&3
  else
    printf '\nCreate the first administrator here (one time only):\n\n  %s\n\n' "$setup_url" > /dev/tty
  fi

  cat <<EOF

Maran is installed and reachable at https://${hostname}:${MARAN_PANEL_PORT}/

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
  - Confirm the panel answers:        curl -k https://${hostname}:${MARAN_PANEL_PORT}/health
  - The full install log is at /var/log/maran/install.log

EOF

  # Last thing on the screen, and in the log, because it is the one outstanding decision this
  # install could not make for the operator. It is printed on stdout rather than on fd 3: it
  # names no secret, and a warning that survives in the install log is one an operator can
  # still find tomorrow.
  if [ -z "$whitelist_seed" ]; then
    cat <<EOF
WARNING: the firewall whitelist is empty, because this install saw no client address to seed
it with — either it was run locally, or sudo dropped the address on the way to root. Nothing
therefore exempts you from the panel's automatic brute-force bans, which can lock you out of
this server. Add your own address to the firewall whitelist in the panel BEFORE you enable
automatic bans.

EOF
  elif ! seed_whitelist_cidr_is_usable "$whitelist_seed"; then
    # The second half of the same warning, and the reason it exists: a value that is PRESENT is
    # not a value the panel will accept. This branch used to be absent, so a seed the panel would
    # refuse at boot ended the install silently — the transcript said the whitelist had been
    # seeded, the whitelist was empty, and the only contradiction was one line in a log.
    #
    # seed_whitelist_cidr_is_usable is defined in 60-config.sh, which install.sh sources earlier
    # in the same shell; the steps run in order, so it is defined by the time this runs.
    cat <<EOF
WARNING: Firewall__SeedWhitelistCidr in /etc/maran/panel.env is ${whitelist_seed}, which the
panel will not store as a whitelist row — so the firewall whitelist will start empty and nothing
exempts you from the panel's automatic brute-force bans, which can lock you out of this server.
Add your own address to the firewall whitelist in the panel BEFORE you enable automatic bans.

EOF
  fi
}
