# Rendered by the Maran agent. Hand edits are overwritten on the next apply.
#
# The milter Postfix talks to. Rspamd scans in this process (self_scan) and
# listens on a unix socket only, so the packaged loopback port is not bound. This
# file is an override and not a local addition: a local addition would be merged
# with the packaged bind_socket and leave that port open as well.
milter = yes;
timeout = 120s;
upstream "local" {
  default = yes;
  self_scan = yes;
}
bind_socket = "/run/rspamd/maran-milter.sock mode=0660 group=postfix";
