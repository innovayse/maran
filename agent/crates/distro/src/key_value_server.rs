//! The Redis-protocol server a host runs for the spam filter's rate limiting.

/// What a family's "Redis-protocol server" is called, packaged as and configured by.
///
/// The product needs one thing of it: a key-value store the spam filter speaks the Redis protocol
/// to over a unix socket. Which program provides that is a fact of the family AND the release —
/// RHEL 10 replaced Redis with Valkey, a wire-compatible fork with its own package, unit, user and
/// configuration file — so callers never write the word "redis" and ask for these four names.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct KeyValueServer {
    /// The package that installs the server.
    pub package: &'static str,
    /// The systemd unit that runs it.
    pub service: &'static str,
    /// The system user the daemon runs as.
    pub user: &'static str,
    /// The absolute path of the configuration file the package ships.
    pub config_file: &'static str,
}
