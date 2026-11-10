//! The one line of the agent's known_hosts file for a pinned key.

use maran_agent_core::validation::web::host_key::HostKey;

use crate::transfer::known_hosts_alias::KNOWN_HOSTS_ALIAS;

/// The one line of a known_hosts file for the pinned key: the alias, a space,
/// the key's `<algorithm> <base64>` pair and a line feed.
///
/// Shared by the root-owned file the connection operations write and the
/// account-owned copy the file transfer writes, so the two cannot disagree about
/// what the client is told to require.
#[must_use]
pub fn known_hosts_line(pinned: &HostKey) -> String {
    format!("{KNOWN_HOSTS_ALIAS} {}\n", pinned.as_str())
}

#[cfg(test)]
#[path = "../tests/transfer/known_hosts_line_tests.rs"]
mod tests;
