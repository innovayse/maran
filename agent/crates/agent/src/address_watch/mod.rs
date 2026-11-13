//! The periodic check of the host's own addresses, and the two layers of the
//! port-25 lockdown that follow them (issue #535).
//!
//! The lockdown refuses a customer's connection to this host's own port 25 twice:
//! Postfix refuses the host's addresses in its client table, and the firewall's
//! output chain resets the connection against two address sets. Both are filled
//! from the host's addresses, and a host gains addresses without anybody editing a
//! rule (a lease, a new interface, a privacy address). [`host_address_watch`] reads
//! the addresses ONCE per interval through [`host_address_source`] and hands the
//! one reading to every [`host_address_layer`], so the layers cannot follow
//! different hosts; a layer that fails is retried at the next interval while the
//! others stay as they were. A layer is recorded as current only when it really
//! followed: one that had nothing to follow yet (no firewall loaded, mail stopped)
//! answers "pending" and is asked again, so "nothing to do" is never counted as done.

pub(crate) mod firewall_sets_layer;
pub(crate) mod host_address_layer;
pub(crate) mod host_address_layer_error;
pub(crate) mod host_address_layer_outcome;
pub(crate) mod host_address_source;
pub(crate) mod host_address_watch;
pub(crate) mod mail_clients_layer;
pub(crate) mod run_host_address_watch;
pub(crate) mod watch_tick;
