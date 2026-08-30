//! The adapter seam: behaviour that differs between distribution families.
//!
//! Operational code never branches on a distribution name; it asks the adapter
//! (rules/architecture.md). [`crate::adapter_for()`] is what chooses
//! the implementation.

use crate::family::DistroFamily;

/// Behaviour that differs between distribution families.
///
/// Deliberately narrow for now: package installation, service names and firewall
/// specifics are added additively by the plans that first need them, so that each
/// method arrives with a caller rather than as speculation.
pub trait DistroAdapter: Send + Sync {
    /// The family this adapter implements.
    fn family(&self) -> DistroFamily;

    /// Absolute path of the shell given to an account that must not log in.
    ///
    /// A hosting account is not a person with a terminal: SFTP and cron work through
    /// it, and an interactive login is exactly what must not. The path differs between
    /// families — Debian ships it at `/usr/sbin/nologin`, RHEL documents `/sbin/nologin`
    /// — which is why it is asked of the adapter rather than written into an operation
    /// (rules/rust.md "Distro adapter": ops never hard-codes a platform path).
    fn nologin_shell(&self) -> &'static str;
}
