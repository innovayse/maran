//! Selecting the adapter for a detected host, release included.
//!
//! The family-only selection is [`crate::adapter_for()`]; this adds the one thing the family does not
//! say, the RHEL generation (issue #542).

use crate::adapter::DistroAdapter;
use crate::adapter_for::adapter_for;
use crate::detection::distro_info::DistroInfo;
use crate::family::DistroFamily;
use crate::rhel::RhelAdapter;

/// The process-wide adapter for the host `distro` describes: its family, and for the RHEL family
/// its generation, read from the os-release `VERSION_ID` the daemon read at start.
///
/// The one place a release is turned into behaviour, so no operation compares a version string.
#[must_use]
pub fn adapter_for_release(distro: &DistroInfo) -> &'static dyn DistroAdapter {
    match distro.family {
        DistroFamily::Rhel => RhelAdapter::for_release(&distro.version_id),
        family => adapter_for(family),
    }
}

#[cfg(test)]
#[path = "tests/adapter_for_release_tests.rs"]
mod tests;
