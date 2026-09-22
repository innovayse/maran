#!/usr/bin/env bash
# Step 90: report the result of the install and hand over the one-time setup token.
#
# The token was generated in 60-config.sh and lives only in /etc/maran/panel.env
# (root:maran 0640); this step reads it back. No password is printed, because none
# exists yet — the administrator sets one during first sign-in, so no credential of any
# kind reaches the log or the shell history.
#
# The /setup page exists (frontend/src/pages/auth/SetupPage.vue, route '/setup') and takes the
# token as an ordinary field, which is why this step prints the link and the token as two
# separate lines. An earlier revision of this comment said the page reads the token out of the
# query string; it did, and no longer does — a secret in a web address reaches nginx's access
# log, its error log (which takes no log_format and cannot be given one), and the operator's
# browser history. The link is still printed, and the reason is worth keeping: an installer
# that ends by telling the operator to open a page that does not exist
# teaches them not to trust anything else it says, so nothing printed here may name a screen
# or a certificate this repository does not actually ship.
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
  #
  # THE TOKEN IS NOT IN THE URL, and that is the whole point of these two lines rather than one.
  # It used to be: the link read `/setup?token=<token>`, /setup prefills its field from the query
  # string, and the operator pasted one thing instead of transcribing a 48-character secret.
  # The price of that convenience was measured rather than argued
  # (docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md):
  #
  #   * The panel's own nginx wrote the whole request line, query string included, into
  #     /var/log/maran/nginx-access.log — a file inside a directory every uid in the service
  #     group can read, which nothing rotated. An install that is begun and abandoned therefore
  #     left a LIVE token on disk for the life of the host, which rules/security.md item 8
  #     forbids by name.
  #   * The operator was being instructed to put a credential into a browser's address bar,
  #     where it enters the history database, the autocomplete suggestions and whatever the
  #     profile synchronises. Nothing in this product can unwrite any of those.
  #   * A URL is the thing people paste into a ticket or a chat; a line labelled as a secret is
  #     not. The URL form made the worst outcome the most convenient one.
  #
  # The link therefore names the screen — which is what an earlier version of this step got right
  # and must not lose, since an installer that ends by naming a page that does not exist teaches
  # the operator to distrust everything else it printed — and the token is handed over on its own
  # labelled line for the operator to paste into the field that is already on that screen
  # (frontend/src/pages/auth/SetupPage.vue). The cost is one extra copy-and-paste, and nothing
  # else: the field is a normal required input. It did once prefill itself from the query
  # string; that prefill is gone, and arriving with '?token=' now clears the address and says
  # so on screen rather than honouring a secret the log would have recorded.
  #
  # The port comes from install.sh's MARAN_PANEL_PORT, not from a literal: a URL printed
  # with a port nginx is not listening on sends the operator to a connection refusal on
  # the one screen they must reach, and it would go wrong the first time the port changes.
  local setup_url="https://${hostname}:${MARAN_PANEL_PORT}/setup"
  local handover
  printf -v handover '\nCreate the first administrator here (one time only):\n\n  %s\n\nPaste this one-time setup token into the token field on that page:\n\n  %s\n\nIt is not part of the link on purpose: a secret in a web address is recorded by the web\nserver that serves it and by the browser that opens it.\n\n' \
    "$setup_url" "$token"
  if [ -w /dev/fd/3 ] 2>/dev/null; then
    printf '%s' "$handover" >&3
  else
    printf '%s' "$handover" > /dev/tty
  fi

  cat <<EOF

Maran is installed and reachable at https://${hostname}:${MARAN_PANEL_PORT}/

Your browser will warn the first time you open that address, and the warning is expected: this
installer generated the certificate and signed it itself. The connection is encrypted all the
same, and the panel answers over HTTPS only — it opens no plain-HTTP port — but a certificate a
browser trusts is issued only against a publicly resolving name, proved by a challenge no
installer can answer on a host it has just met. When this server has such a name, put your own
certificate and key at /etc/maran/tls/panel.crt and /etc/maran/tls/panel.key and reload nginx;
whatever is already at those paths is left alone, by this install and by the next. Replacing it
is a manual step today: the panel's SSL page issues certificates for the sites you host, not for
the panel's own address.

Open the link above and paste the setup token printed with it to create the first
administrator. The token stops working the moment the panel has a user, so it is worth
nothing to anyone who finds it afterwards — but until then it grants the whole server, so
do not paste it into a chat or a ticket. It was printed on this terminal only: it is
deliberately absent from the install log, and deliberately not part of the link, so that
neither this server's web server log nor your browser's history ever records it.

If you lose it, read Setup__Token from /etc/maran/panel.env (root:maran 0640), or re-run
the installer to issue a new one.

The certificate is self-signed until you point a real hostname at this server, so your
browser will warn once. That is expected on a fresh install.

Next steps:
  - Confirm both services are healthy: systemctl status maran-api maran-agent
  - Confirm the panel answers:        curl -k https://${hostname}:${MARAN_PANEL_PORT}/health
                                      (-k because of the certificate described above)
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
