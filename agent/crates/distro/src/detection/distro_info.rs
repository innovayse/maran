//! Identity of the host distribution.

use crate::family::DistroFamily;

/// What the agent knows about the host it runs on.
///
/// Both the exact `id` and the `family` are kept: the family drives behaviour,
/// while the id and version are what an operator needs to see in a support
/// ticket when something behaves differently on Rocky than on AlmaLinux.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DistroInfo {
    /// The os-release `ID` field (`ubuntu`, `debian`, `almalinux`, `rocky`).
    pub id: String,
    /// The family the adapter layer keys on.
    pub family: DistroFamily,
    /// The os-release `VERSION_ID` field (`24.04`, `12`, `9.4`).
    pub version_id: String,
}
