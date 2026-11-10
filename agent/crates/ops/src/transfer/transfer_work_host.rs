//! The seam between the transfer operations and the machine they move data on.

use std::fs::File;
use std::path::{Path, PathBuf};
use std::time::Duration;

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::AdapterBinary;

use crate::transfer::model::dump_file::DumpFile;
use crate::transfer::model::rsync_slice_outcome::RsyncSliceOutcome;
use crate::transfer::transfer_op_error::TransferOpError;

/// What a pull of files and a dump of a database need from this host: a
/// directory the receiving account can read its key from, one run of `rsync` as
/// that account, a root-only file on the disk for a dump, and one run of `ssh`
/// whose output is that file.
///
/// A trait, because every method touches the real machine: forks as a customer,
/// writes key files, spawns real `rsync` and `ssh`, measures a real filesystem.
/// Behind this seam the decisions worth reviewing (the argument vectors, the
/// order, the cleanup on every path, how an exit status is read) are what a test
/// examines.
///
/// Implementations MUST spawn with an argv array against a program taken from the
/// `DistroAdapter`, never through a shell and never by a name resolved through
/// `PATH` (rules/security.md item 3), and MUST be called from
/// `tokio::task::spawn_blocking`: each waits on a child for as long as a
/// customer's data takes.
pub trait TransferWorkHost: Send + Sync {
    /// Refuses unless `needed` bytes fit on the filesystem that holds the
    /// account's home while the host's free-space floor stays intact.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::DiskInsufficient`] when they do not fit, and
    /// [`TransferOpError::ScratchUnavailable`] when the free space cannot be
    /// measured (a failed measurement is never read as "room").
    fn require_home_room(&self, account: &AccountName, needed: u64) -> Result<(), TransferOpError>;

    /// Creates a fresh directory in the receivers area that only `account` can
    /// enter (mode `0700`, owned by the account) and returns its path.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::ScratchUnavailable`] when the area is not a directory
    /// this process owns, or the new directory cannot be made or handed over.
    fn create_receiver_directory(&self, account: &AccountName) -> Result<PathBuf, TransferOpError>;

    /// Writes `content` to a NEW file at `path` in a receiver directory, mode
    /// `0600`, owned by `account`. The file must not exist.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::ScratchUnavailable`] when the file exists or cannot be
    /// written or handed over.
    fn write_receiver_file(
        &self,
        account: &AccountName,
        path: &Path,
        content: &[u8],
    ) -> Result<(), TransferOpError>;

    /// Removes a directory [`Self::create_receiver_directory`] returned, with
    /// everything in it. Removing one that is already gone succeeds.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::ScratchUnavailable`] when it could not be removed.
    fn remove_receiver_directory(&self, path: &Path) -> Result<(), TransferOpError>;

    /// Runs `program` (the `rsync` of the distro adapter) with `arguments` as an
    /// argv array, AS `account`, for at most `deadline`.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::Spawn`] when the account cannot be assumed or the
    /// program cannot be started or waited for. A run that exited non-zero is an
    /// [`RsyncSliceOutcome::Failed`], and one that reached its deadline is
    /// [`RsyncSliceOutcome::SliceEnded`]; neither is an error here.
    fn run_rsync_slice(
        &self,
        account: &AccountName,
        program: &AdapterBinary,
        arguments: &[&str],
        deadline: Duration,
    ) -> Result<RsyncSliceOutcome, TransferOpError>;

    /// Creates a new, empty, root-only file for a dump of at most `needed` bytes
    /// and refuses first when the disk has no room for it above the floor.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::DiskInsufficient`] when the room is not there, and
    /// [`TransferOpError::ScratchUnavailable`] when the file cannot be made.
    fn create_dump_file(&self, needed: u64) -> Result<DumpFile, TransferOpError>;

    /// Runs `program` (the `ssh` of the distro adapter) with `arguments` as an
    /// argv array, its standard input closed and its standard output written to
    /// `output`, for at most `deadline`, refusing more than `cap` bytes.
    ///
    /// Returns the program's exit status.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::OutputBound`] when it reached its deadline or wrote past
    /// the cap, and [`TransferOpError::Spawn`] when it could not be started or
    /// waited for. A program that ran and exited non-zero is NOT an error here.
    fn run_ssh_into(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        output: &File,
        cap: u64,
        deadline: Duration,
    ) -> Result<i32, TransferOpError>;

    /// Removes a dump file [`Self::create_dump_file`] made. Removing one that is
    /// already gone succeeds.
    ///
    /// # Errors
    ///
    /// [`TransferOpError::ScratchUnavailable`] when it could not be removed.
    fn remove_dump_file(&self, path: &Path) -> Result<(), TransferOpError>;
}
