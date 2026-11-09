#!/usr/bin/env bash
# panel-certificate.sh: `maran issue-panel-certificate` — orders a trusted certificate for the panel's
# own configured host name and installs it as the pair the interface is served with. Run again, it
# renews. For the operator who cannot ask for it from the interface.
#
# This file validates that nothing was typed and hands over. Every decision — whether a host name is
# configured, what the authority answers, what the agent installs, the row that makes the daily sweep
# renew it, the journal entry — is the panel's own code running as the service account.
#
# The verb takes no argument on purpose: the host name is the operator's configuration, read by the
# panel, and there is no value here for a shell to misread or for a typo to aim at another name.

# cli_issue_panel_certificate: the verb. Takes no arguments.
cli_issue_panel_certificate() {
  [ "$#" -eq 0 ] \
    || cli_fail "issue-panel-certificate takes no arguments; the host name is read from the panel's configuration"

  local status=0
  cli_dotnet issue-panel-certificate || status=$?

  if [ "$status" -eq 0 ]; then
    cli_audit_log issue-panel-certificate issued
  else
    cli_audit_log issue-panel-certificate "refused-or-failed-${status}"
  fi
  return "$status"
}
