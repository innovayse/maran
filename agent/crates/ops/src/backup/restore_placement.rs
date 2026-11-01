//! Where one restore puts things, and whose the result must be.

use std::path::PathBuf;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

/// Where one restore puts things, and whose the result must be.
///
/// A struct rather than seven parameters, and it exists for testability as much
/// as for readability: the public entry point fills it from `AgentPaths` and
/// root's uid, and a test fills it with directories it can really
/// create. Every field is agent-derived — nothing in it comes from a request.
pub(super) struct RestorePlacement {
    /// The directory holding every account's home. `/home` on a real host.
    pub(super) home_root: PathBuf,
    /// The first directory of the scratch chain this operation is willing to
    /// state — `/` on a real host, so every level of the real path is checked.
    /// A test owns nothing above its temporary directory and passes that
    /// instead, because no test can say anything true about `/tmp`'s mode.
    pub(super) scratch_base: PathBuf,
    /// The scratch root: this directory and everything under it must be
    /// `0700` and root's, while the levels above it need only be root's and
    /// unwritable by anybody else. `AgentPaths::BULK_SCRATCH_ROOT` on a real
    /// host.
    pub(super) scratch_root: PathBuf,
    /// The root-only scratch this restore stages dumps in.
    pub(super) scratch: PathBuf,
    /// The uid every level of the scratch chain must belong to.
    ///
    /// Deliberately NOT [`Self::owner`], which is the account's own uid: the
    /// staging tree is the account's while the account fills it, and the
    /// scratch is root's and never anybody else's — the archive's database
    /// dumps are extracted into it as root. One field for both would be one
    /// field saying two things, and the one it would end up saying is the
    /// account's.
    pub(super) scratch_owner: u32,
    /// Where the replacement home is built before it becomes the home.
    pub(super) staging: PathBuf,
    /// Where the home being replaced is parked until the restore has finished.
    pub(super) previous: PathBuf,
    /// The document written immediately before the first rename of the swap and
    /// removed after the last step, so that a process which never saw this
    /// request can finish what a killed one started. See
    /// `crate::backup::recover_restores`.
    pub(super) marker: PathBuf,
    /// The uid the restored home root must end up owned by, and the uid the
    /// staging tree is handed to before it is filled.
    pub(super) owner: u32,
    /// The account's own gid, which the staging tree is handed to.
    ///
    /// Not the same as [`Self::group`], and the difference is the whole point
    /// of having both: the staging tree belongs to the account while the
    /// account is writing into it, and the finished home belongs to the web
    /// server's group so that the server can traverse it.
    pub(super) account_group: u32,
    /// The gid the restored home root must end up owned by — the web server's
    /// group, resolved one layer up from the distro adapter.
    pub(super) group: u32,
    /// The free-space floor the home filesystem must still have before
    /// anything is extracted into it (issue #148).
    pub(super) floor: FreeSpaceFloor,
}
