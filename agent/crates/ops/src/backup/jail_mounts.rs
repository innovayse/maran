//! The seam that makes an account's file-transfer jails show its current home.

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;

/// Brings every file-transfer jail of an account back onto its home directory.
///
/// A seam and not a call into `ops::sftp` and `ops::ftps`, for the reason
/// [`DatabaseCatalog`](crate::backup::DatabaseCatalog) is one: **`ops::backup`
/// does not import another `ops` area.** The composition happens one layer up,
/// in `agent/src/services/backup/`, which wires both protocols' refresh into
/// this trait.
///
/// It exists because a jail is a bind mount of `/home/<account>`, a bind mount
/// follows the inode it was made from, and a whole-account restore replaces the
/// home with a different directory (issue #487). Without this, the jail keeps
/// showing the displaced tree, the restore deletes it, and every file the
/// customer uploads afterwards over SFTP or FTPS lands in a tree nothing serves.
///
/// Implementations are called with the account's lock held and MUST NOT take it,
/// and MUST be called from `tokio::task::spawn_blocking`: the real one waits on
/// the service manager.
pub trait JailMounts: Send + Sync {
    /// Mounts the account's current home into each of its jails.
    ///
    /// An account with no jail is success, not an error: most accounts never
    /// have a file-transfer login.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::JailsNotRefreshed`] when a jail that exists could
    /// not be brought onto the current home.
    fn refresh(&self, account: &AccountName) -> Result<(), BackupError>;
}
