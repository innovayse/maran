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
}
