//! Where one import stages what it reads out of an archive.

use std::path::PathBuf;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

/// Where one import extracts the archive's own manifest, and whose it must be.
pub(super) struct ImportScratch {
    /// The first directory of the scratch chain this operation is willing to
    /// state: `/` on a real host.
    pub(super) base: PathBuf,
    /// The scratch root, `0700` and root's all the way.
    pub(super) root: PathBuf,
    /// The directory the manifest is extracted into; removed by the caller.
    pub(super) scratch: PathBuf,
    /// The uid every level of the chain must belong to.
    pub(super) owner: u32,
    /// The free-space floor the scratch filesystem must keep.
    pub(super) floor: FreeSpaceFloor,
}
