#!/usr/bin/env bash
# Step 90: print the one-time setup URL for creating the first administrator in the
# browser. The setup token was generated in 60-config.sh and lives only in
# /etc/maran/panel.env (root:panel 0640) — this step reads it back to build the
# URL but never prints a password (there is none yet; the admin sets it in the browser
# after following this link, so no credential of any kind reaches the log or history).
set -euo pipefail

step_finish() {
  local token hostname url
  token="$(awk -F= '$1=="Setup__Token" { $1=""; sub(/^=/,""); print; exit }' /etc/maran/panel.env)"
  hostname="$(hostname -f 2>/dev/null || hostname)"
  url="https://${hostname}:8443/setup?token=${token}"

  # The setup link carries a token that is, by itself, permission to create the first
  # administrator. It goes to the terminal on fd 3 (opened by install.sh before logging
  # was redirected) so it never lands in /var/log/maran/install.log, which outlives
  # the install and is readable by anyone in the log directory's group.
  if [ -w /dev/fd/3 ] 2>/dev/null; then
    printf '\nOpen this one-time link in your browser to create the first administrator:\n\n  %s\n\n' "$url" >&3
  else
    printf '\nOpen this one-time link in your browser to create the first administrator:\n\n  %s\n\n' "$url" > /dev/tty
  fi

  cat <<EOF

Maran is installed.

The one-time setup link was printed above, on this terminal only — it is deliberately
absent from the install log. If you lost it, read the token from /etc/maran/panel.env
(root:panel 0640) or re-run the installer to issue a new one.

Next steps:
  - Complete the browser setup above to create the admin account and password
    (the password is set in the browser and is never written to any log).
  - Check service status any time with: systemctl status maran-api maran-agent
  - The full install log is at /var/log/maran/install.log

EOF
}
