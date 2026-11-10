//! The [`TransferWorkHost`] that actually touches this machine.

use std::fs::{self, DirBuilder, File, OpenOptions};
use std::io::{ErrorKind, Write};
use std::os::unix::fs::{DirBuilderExt, MetadataExt, OpenOptionsExt, chown};
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account_for_bytes::fork_as_account_for_bytes;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::available_bytes::available_bytes;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::utils::run_unthreaded::run_unthreaded;
use maran_agent_core::utils::run_unthreaded_to_file::run_unthreaded_to_file;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::AdapterBinary;

use crate::transfer::model::dump_file::DumpFile;
use crate::transfer::model::rsync_slice_outcome::RsyncSliceOutcome;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_work_host::TransferWorkHost;

/// Length of the random part of a directory or file name, in bytes.
const NAME_BYTES: usize = 16;

/// What the forked child writes when `rsync` ended by itself: this byte, then
/// its exit status.
const TAG_ENDED: u8 = b'E';

/// What the forked child writes when `rsync` reached its deadline and was killed.
const TAG_TIMED_OUT: u8 = b'T';

/// The prefix of every dump file this host makes, so a removal can refuse a name
/// it did not give.
const DUMP_PREFIX: &str = "dump-";

/// Forks as an account to run `rsync`, writes key files an account can read,
/// and keeps dump files on the disk-backed scratch root.
pub struct ProcessTransferWorkHost {
    /// The `0711` directory under which each run's receiver directory is made.
    receivers_root: PathBuf,
    /// The root-only directory under which a dump file is made.
    dump_root: PathBuf,
    /// The floor a write is refused below.
    floor: FreeSpaceFloor,
}

impl ProcessTransferWorkHost {
    /// A host using the agent's own receiver area and bulk scratch root.
    #[must_use]
    pub fn new(floor: FreeSpaceFloor) -> Self {
        Self::at(
            PathBuf::from(AgentPaths::TRANSFER_RECEIVER_DIRECTORY),
            PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT).join("transfer-dumps"),
            floor,
        )
    }

    /// A host using the given directories, which must be this process's own.
    /// Tests use temporary directories.
    #[must_use]
    pub fn at(receivers_root: PathBuf, dump_root: PathBuf, floor: FreeSpaceFloor) -> Self {
        Self {
            receivers_root,
            dump_root,
            floor,
        }
    }

    /// Creates `directory` when it is absent and proves it is a real directory
    /// this process owns with no group or other WRITE permission and, when
    /// `private`, no group or other permission at all.
    fn ensure_directory(directory: &Path, mode: u32, private: bool) -> Result<(), TransferOpError> {
        DirBuilder::new()
            .recursive(true)
            .mode(mode)
            .create(directory)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;

        let metadata =
            fs::symlink_metadata(directory).map_err(|_| TransferOpError::ScratchUnavailable)?;
        let owner = current_uid().map_err(|_| TransferOpError::ScratchUnavailable)?;
        let forbidden = if private { 0o077 } else { 0o022 };

        if !metadata.is_dir() || metadata.uid() != owner || metadata.mode() & forbidden != 0 {
            return Err(TransferOpError::ScratchUnavailable);
        }

        Ok(())
    }

    /// A fresh random hexadecimal name.
    fn random_name() -> Result<String, TransferOpError> {
        let mut random = [0_u8; NAME_BYTES];
        getrandom::fill(&mut random).map_err(|_| TransferOpError::ScratchUnavailable)?;

        Ok(random.iter().map(|byte| format!("{byte:02x}")).collect())
    }

    /// Whether `path` is a direct child of `root`, named without a parent
    /// reference.
    fn is_direct_child(root: &Path, path: &Path) -> bool {
        path.parent() == Some(root)
            && !path
                .components()
                .any(|part| matches!(part, std::path::Component::ParentDir))
    }

    /// The start failure reported when the account cannot be assumed.
    fn account_unavailable() -> TransferOpError {
        TransferOpError::Spawn(SpawnError::Start {
            program: "rsync".to_owned(),
            reason: "the account could not be assumed".to_owned(),
        })
    }
}

impl TransferWorkHost for ProcessTransferWorkHost {
    /// Measures where the account's home lives and applies the floor to what
    /// would be left after `needed` bytes.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::require_home_room`].
    fn require_home_room(&self, account: &AccountName, needed: u64) -> Result<(), TransferOpError> {
        let home = PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str());
        let available = available_bytes(&home).map_err(|_| TransferOpError::ScratchUnavailable)?;

        if available < needed {
            return Err(TransferOpError::DiskInsufficient);
        }

        self.floor
            .admit(Ok(available - needed))
            .map_err(|_| TransferOpError::DiskInsufficient)
    }

    /// Makes `<receivers>/<random hex>` with mode `0700`, owned by the account.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::create_receiver_directory`].
    fn create_receiver_directory(&self, account: &AccountName) -> Result<PathBuf, TransferOpError> {
        Self::ensure_directory(&self.receivers_root, 0o711, false)?;
        let ids = AccountIds::resolve(account).map_err(|_| Self::account_unavailable())?;
        let path = self.receivers_root.join(Self::random_name()?);

        DirBuilder::new()
            .mode(0o700)
            .create(&path)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;
        chown(&path, Some(ids.uid()), Some(ids.gid())).map_err(|_| {
            let _ = fs::remove_dir(&path);
            TransferOpError::ScratchUnavailable
        })?;

        Ok(path)
    }

    /// Creates the file exclusively with mode `0600`, writes `content` and hands
    /// the file to the account.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::write_receiver_file`].
    fn write_receiver_file(
        &self,
        account: &AccountName,
        path: &Path,
        content: &[u8],
    ) -> Result<(), TransferOpError> {
        let inside = path
            .parent()
            .is_some_and(|parent| Self::is_direct_child(&self.receivers_root, parent));
        if !inside {
            return Err(TransferOpError::ScratchUnavailable);
        }
        let ids = AccountIds::resolve(account).map_err(|_| Self::account_unavailable())?;

        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .mode(0o600)
            .open(path)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;
        file.write_all(content)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;
        chown(path, Some(ids.uid()), Some(ids.gid()))
            .map_err(|_| TransferOpError::ScratchUnavailable)
    }

    /// Removes a directory this host created, never anything else.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::remove_receiver_directory`].
    fn remove_receiver_directory(&self, path: &Path) -> Result<(), TransferOpError> {
        if !Self::is_direct_child(&self.receivers_root, path) {
            return Err(TransferOpError::ScratchUnavailable);
        }

        match fs::symlink_metadata(path) {
            Ok(metadata) if metadata.is_dir() => fs::remove_dir_all(path)
                .or_else(|error| match error.kind() {
                    ErrorKind::NotFound => Ok(()),
                    _ => Err(error),
                })
                .map_err(|_| TransferOpError::ScratchUnavailable),
            Ok(_) => Err(TransferOpError::ScratchUnavailable),
            Err(error) if error.kind() == ErrorKind::NotFound => Ok(()),
            Err(_) => Err(TransferOpError::ScratchUnavailable),
        }
    }

    /// Forks as the account and runs `rsync` there under `deadline`.
    ///
    /// The child brings back two bytes at most: whether `rsync` ended by itself
    /// and with which status, or that it reached its deadline. Nothing `rsync`
    /// printed is read (its streams are the null device), so a source's text
    /// never enters this root process.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::run_rsync_slice`].
    fn run_rsync_slice(
        &self,
        account: &AccountName,
        program: &AdapterBinary,
        arguments: &[&str],
        deadline: Duration,
    ) -> Result<RsyncSliceOutcome, TransferOpError> {
        let ids = AccountIds::resolve(account).map_err(|_| Self::account_unavailable())?;

        let bytes = fork_as_account_for_bytes(&ids, 2, |sink| {
            let report: [u8; 2] =
                match run_unthreaded(program.as_path(), arguments, Stdio::null(), deadline) {
                    Ok(code) => [TAG_ENDED, u8::try_from(code).unwrap_or(u8::MAX)],
                    Err(SpawnError::Bound(BoundExceeded::TimedOut { .. })) => [TAG_TIMED_OUT, 0],
                    Err(_) => return Err(PrivError::WorkFailed),
                };
            let wanted = if report[0] == TAG_ENDED { 2 } else { 1 };

            sink.write_all(&report[..wanted])
                .map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|_| {
            TransferOpError::Spawn(SpawnError::Wait {
                program: "rsync".to_owned(),
                reason: "the run as the account did not complete".to_owned(),
            })
        })?;

        match bytes.as_slice() {
            [TAG_ENDED, status] => Ok(RsyncSliceOutcome::of_status(i32::from(*status))),
            [TAG_TIMED_OUT] => Ok(RsyncSliceOutcome::SliceEnded),
            _ => Err(TransferOpError::Spawn(SpawnError::Wait {
                program: "rsync".to_owned(),
                reason: "the run as the account reported nothing usable".to_owned(),
            })),
        }
    }

    /// Makes `<dump root>/dump-<random hex>.sql` with mode `0600`, after
    /// proving the disk has room for `needed` bytes above the floor.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::create_dump_file`].
    fn create_dump_file(&self, needed: u64) -> Result<DumpFile, TransferOpError> {
        Self::ensure_directory(&self.dump_root, 0o700, true)?;

        let available =
            available_bytes(&self.dump_root).map_err(|_| TransferOpError::ScratchUnavailable)?;
        if available < needed {
            return Err(TransferOpError::DiskInsufficient);
        }
        self.floor
            .admit(Ok(available - needed))
            .map_err(|_| TransferOpError::DiskInsufficient)?;

        let path = self
            .dump_root
            .join(format!("{DUMP_PREFIX}{}.sql", Self::random_name()?));
        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .create_new(true)
            .mode(0o600)
            .open(&path)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;

        Ok(DumpFile { file, path })
    }

    /// Runs `ssh` through the bounded core with its output in `output`.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::run_ssh_into`].
    fn run_ssh_into(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        output: &File,
        cap: u64,
        deadline: Duration,
    ) -> Result<i32, TransferOpError> {
        run_unthreaded_to_file(
            program.as_path(),
            arguments,
            Stdio::null(),
            output,
            cap,
            deadline,
        )
        .map_err(TransferOpError::from)
    }

    /// Removes a dump file this host made, never anything else.
    ///
    /// # Errors
    ///
    /// As documented on [`TransferWorkHost::remove_dump_file`].
    fn remove_dump_file(&self, path: &Path) -> Result<(), TransferOpError> {
        let named_by_us = path
            .file_name()
            .and_then(|name| name.to_str())
            .is_some_and(|name| name.starts_with(DUMP_PREFIX));
        if !Self::is_direct_child(&self.dump_root, path) || !named_by_us {
            return Err(TransferOpError::ScratchUnavailable);
        }

        match fs::remove_file(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == ErrorKind::NotFound => Ok(()),
            Err(_) => Err(TransferOpError::ScratchUnavailable),
        }
    }
}

#[cfg(test)]
#[path = "../tests/transfer/process_transfer_work_host_tests.rs"]
mod tests;
