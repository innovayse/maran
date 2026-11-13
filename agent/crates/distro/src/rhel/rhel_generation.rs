//! The generations of the RHEL family the adapter tells apart.

use crate::rhel::rhel_packages::release_major;

/// The first release major that no longer ships module streams.
const FIRST_RELEASE_WITHOUT_MODULES: u32 = 10;

/// A generation of the RHEL family, from the os-release `VERSION_ID`
/// ([`DistroInfo::version_id`](crate::DistroInfo::version_id)).
///
/// A VERSION fact and not a family fact: AlmaLinux, Rocky and Oracle 9 deliver Node.js as module
/// streams of one `nodejs` package, and the same distributions' 10 deliver it as plain packages,
/// one per major (`nodejs`, `nodejs24`), because the 10 generation ships no module streams
/// (issue #542). The adapter is chosen once from the release the daemon read
/// ([`crate::adapter_for_release()`]), so no caller compares a version string.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RhelGeneration {
    /// The 9 generation (and, until the adapter learns another, every release below 10 or one
    /// whose version is not a number): AppStream carries module streams.
    El9,
    /// The 10 generation and newer: AppStream carries plain packages and no module streams.
    /// Observed in `almalinux:10` (10.2, dnf 4.20.0); a release newer than 10 is UNVERIFIED and
    /// read as this generation on the ground that modularity was dropped and is not coming back.
    El10,
}

impl RhelGeneration {
    /// The generation of the release `version_id` names (`9.4`, `10.2`).
    #[must_use]
    pub fn of(version_id: &str) -> Self {
        match release_major(version_id).parse::<u32>() {
            Ok(major) if major >= FIRST_RELEASE_WITHOUT_MODULES => Self::El10,
            _ => Self::El9,
        }
    }
}

#[cfg(test)]
#[path = "../tests/rhel/rhel_generation_tests.rs"]
mod tests;
