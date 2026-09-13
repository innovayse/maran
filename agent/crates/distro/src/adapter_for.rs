//! Selecting the adapter for a detected family.
//!
//! The branch on family happens exactly once, here, so supporting a new family is
//! a new implementation folder rather than a new `match` arm in every operation
//! that installs a package or restarts a service (rules/architecture.md).

use crate::adapter::DistroAdapter;
use crate::debian::DebianAdapter;
use crate::family::DistroFamily;
use crate::rhel::RhelAdapter;

/// The process-wide adapter for `family`.
///
/// The adapters hold no state, so one shared reference each is enough and callers
/// never have to thread ownership of an adapter through their signatures.
#[must_use]
pub fn adapter_for(family: DistroFamily) -> &'static dyn DistroAdapter {
    match family {
        DistroFamily::Debian => &DebianAdapter,
        DistroFamily::Rhel => &RhelAdapter,
    }
}

#[cfg(test)]
#[path = "tests/adapter_for_tests.rs"]
mod tests;
