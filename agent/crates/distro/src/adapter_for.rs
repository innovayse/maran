//! Selecting the adapter for a detected family.
//!
//! The branch on family happens exactly once, here, so supporting a new family is
//! a new implementation folder rather than a new `match` arm in every operation
//! that installs a package or restarts a service (rules/architecture.md).

use crate::adapter::DistroAdapter;
use crate::debian::DebianAdapter;
use crate::family::DistroFamily;
use crate::rhel::RhelAdapter;
use crate::suse::SuseAdapter;

/// The process-wide adapter for `family`, for a caller that knows the family and not the release.
///
/// The adapters hold no state, so one shared reference each is enough and callers
/// never have to thread ownership of an adapter through their signatures. The RHEL family
/// answers as its 9 generation; a caller that has read the host's `os-release` asks
/// [`crate::adapter_for_release()`] instead, because the 10 generation packages Node.js differently
/// (issue #542).
#[must_use]
pub fn adapter_for(family: DistroFamily) -> &'static dyn DistroAdapter {
    match family {
        DistroFamily::Debian => &DebianAdapter,
        DistroFamily::Rhel => &RhelAdapter::EL9,
        DistroFamily::Suse => &SuseAdapter,
    }
}

#[cfg(test)]
#[path = "tests/adapter_for_tests.rs"]
mod tests;
