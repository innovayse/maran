//! The [`FilesHost`] that actually touches this machine.

use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::path::resolve_in_home;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_parent_directory::open_parent_directory;
use crate::files::remove_in_home::remove_in_home;
use crate::files::write_in_home::write_in_home;

/// Prefix every temporary file this host creates carries.
///
/// Named after the product and starting with a dot so that a temporary left
/// behind by a crash is recognisable as ours and is not served by a web server
/// configured to hide dotfiles. It is only ever visible for the microseconds
/// between the create and the rename; the prefix is for the case where it is
/// not.
const TEMPORARY_PREFIX: &str = ".maran-write";

/// Forks to the account for every operation and resolves paths as root.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: what is left here is resolving an account's ids,
/// naming a temporary file, and forking. Everything that a reviewer of a
/// privileged write should read is in `write_in_home`, `remove_in_home` and
/// `open_parent_directory`.
pub struct ProcessFilesHost;

impl ProcessFilesHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }

    /// The absolute home directory of `account`.
    ///
    /// From [`AgentPaths`], so this area and the site area agree on where an
    /// account lives without either writing the literal.
    fn home(account: &AccountName) -> PathBuf {
        PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str())
    }

    /// A name for the temporary file the write renames into place.
    ///
    /// Built in the PARENT, before the fork, because `write_in_home` runs in a
    /// forked child of a multi-threaded daemon and everything it does not have
    /// to do there is better done here (`fork_as_account`'s contract). It also
    /// means the name is a plain `&str` by the time the child sees it, with no
    /// formatting and no clock call left on that side.
    ///
    /// Uniqueness comes from the process id and the wall clock together: two
    /// writes into one directory at the same nanosecond from the same daemon do
    /// not happen, and if they somehow did, the temporary file is created with
    /// `O_EXCL` — so the second write fails loudly rather than the two of them
    /// interleaving into one file.
    fn temporary_name() -> String {
        let nanoseconds = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_or(0, |since| since.as_nanos());

        format!("{TEMPORARY_PREFIX}-{}-{nanoseconds}", std::process::id())
    }
}

impl Default for ProcessFilesHost {
    fn default() -> Self {
        Self::new()
    }
}

impl super::FilesHost for ProcessFilesHost {
    /// Creates the directory chain in a forked child that has dropped to the
    /// account.
    ///
    /// The ids are resolved here, at the moment of use, and never cached: an
    /// account deleted and recreated between two operations gets a different
    /// uid, and a cached one would write into whoever now holds it.
    fn create_parents_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError> {
        let ids = AccountIds::resolve(account)?;
        let home = Self::home(account);
        let uid = ids.uid();

        // The child's outcome crosses back as an exit status and nothing else
        // (`fork_as_account`'s contract), so the typed reason a level was
        // refused cannot come with it. That is why the failure below is
        // `DirectoryUnusable` rather than whatever the walk decided: it is the
        // honest resolution of what the parent can actually know.
        fork_as_account(&ids, || {
            open_parent_directory(&home, relative, uid, MissingParents::Create)
                .map(|_| ())
                .map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| match error {
            PrivError::WorkFailed => FilesOpError::DirectoryUnusable,
            other => FilesOpError::Privilege(other),
        })
    }

    /// Writes the file in a forked child that has dropped to the account.
    fn write_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
        contents: &[u8],
        mode: FileMode,
    ) -> Result<(), FilesOpError> {
        let ids = AccountIds::resolve(account)?;
        let home = Self::home(account);
        let uid = ids.uid();
        let temporary = Self::temporary_name();

        fork_as_account(&ids, || {
            write_in_home(&home, relative, &temporary, contents, mode, uid)
                .map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| match error {
            PrivError::WorkFailed => FilesOpError::WriteFailed,
            other => FilesOpError::Privilege(other),
        })
    }

    /// Removes the file in a forked child that has dropped to the account.
    fn remove_as_account(
        &self,
        account: &AccountName,
        relative: &RelativePath,
    ) -> Result<(), FilesOpError> {
        let ids = AccountIds::resolve(account)?;
        let home = Self::home(account);
        let uid = ids.uid();

        fork_as_account(&ids, || {
            remove_in_home(&home, relative, uid).map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| match error {
            // NOT `NotFound`, tempting as it is: "already gone" is the common
            // reason a removal fails, but it is not the only one, and the
            // others — a FIFO or a hardlink left at the challenge name, a
            // symlink refused by `O_NOFOLLOW` — are somebody trying something.
            // The child cannot say which it hit, so reporting them all as
            // "nothing was there" would erase exactly the ones worth seeing.
            // The idempotent `NotFound` is produced instead by the root-side
            // containment check in `delete_entry`, which runs BEFORE this and
            // can tell an absent file from a present one.
            PrivError::WorkFailed => FilesOpError::RemoveFailed,
            other => FilesOpError::Privilege(other),
        })
    }

    /// Delegates to `agent-core`'s one containment primitive.
    ///
    /// Runs as root, unlike everything above it, and that is the point: it
    /// needs to see the whole path, including a level the account's own process
    /// might not be allowed to traverse.
    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, FilesOpError> {
        Ok(resolve_in_home(account, relative)?)
    }
}
