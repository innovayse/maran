//! Where one verification reads, and whose every file and directory must be.

use std::path::PathBuf;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

/// Where one verification puts its single scratch directory, and the uid
/// everything it touches must belong to.
///
/// A struct for the reason `RestorePlacement` is one: the public entry point
/// fills it from `AgentPaths` and root's uid, and a test fills it with
/// directories it can really create and the uid it really runs as. Every field
/// is agent-derived and nothing in it comes from a request.
pub(super) struct VerificationPlacement {
    /// The first directory of the scratch chain this operation is willing to
    /// state: `/` on a real host, so every level of the real path is checked.
    pub(super) scratch_base: PathBuf,
    /// The scratch root: this directory and everything under it must be `0700`
    /// and root's. `AgentPaths::BULK_SCRATCH_ROOT` on a real host.
    pub(super) scratch_root: PathBuf,
    /// The root-only directory the manifest is extracted into, removed on every
    /// exit path. `AgentPaths::backup_verification_scratch_dir` on a real host.
    pub(super) scratch: PathBuf,
    /// The uid the artifact, its sidecar and every level of the scratch chain
    /// must belong to: root's on a real host.
    ///
    /// One field for all three because they are one requirement, "root's and
    /// nobody else's"; the account's own uid has no part in a verification,
    /// which never extracts as the account.
    pub(super) owner: u32,
    /// The free-space floor the scratch filesystem must still have before the
    /// manifest is extracted into it (issue #148).
    pub(super) floor: FreeSpaceFloor,
}
