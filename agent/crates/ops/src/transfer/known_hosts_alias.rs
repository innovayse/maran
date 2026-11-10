//! The name a pinned host key is filed under in the agent's known_hosts file.

/// The host name under which the pinned key is filed and looked up
/// (`HostKeyAlias`).
///
/// A constant, not the source's name: the file holds exactly one line for one
/// connection, the client is told to look the key up under this alias, and the
/// connection is made to a literal address, so no name has to match anything and
/// nothing in the file depends on text the administrator typed.
pub(crate) const KNOWN_HOSTS_ALIAS: &str = "maran-source";
