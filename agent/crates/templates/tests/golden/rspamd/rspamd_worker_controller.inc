# Rendered by the Maran agent. Hand edits are overwritten on the next apply.
#
# The controller on a root-only unix socket, so the packaged loopback port is
# not bound. An override and not a local addition, for the reason the proxy
# worker's file gives.
bind_socket = "/run/rspamd/maran-controller.sock mode=0600";
