//! What one backup creation is asked to make, and where.

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::secrets::archive_key::ArchiveKey;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

/// The five validated values a creation is asked for, borrowed together.
///
/// Every field is a type that can only be built valid, so the bundle holds no
/// check of its own. It exists so that the entry point and the two steps below
/// it take ONE argument naming the request, instead of five that a caller could
/// pass in the wrong order. `key` is the field whose silent default would write
/// a plain archive where a sealed one was asked for, which is why a caller must
/// state it (`None` spelled out) and not leave it to a builder default.
#[derive(Debug, Clone, Copy)]
pub struct CreationRequest<'a> {
    /// The account whose home and databases are archived.
    pub account: &'a AccountName,

    /// The id the finished artifact is published under.
    pub backup_id: &'a BackupId,

    /// The validated backup root the artifact is published into.
    pub root: &'a LocalBackupRoot,

    /// The free-space floor the backup directory's filesystem must still have
    /// before anything is written (issue #148).
    pub floor: &'a FreeSpaceFloor,

    /// The archive key the finished archive is sealed with, or `None` for a
    /// plain archive.
    pub key: Option<&'a ArchiveKey>,
}
