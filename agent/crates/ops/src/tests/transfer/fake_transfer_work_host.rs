//! The in-memory [`TransferWorkHost`] the pull and dump tests decide against.
//!
//! It records every receiver directory made and removed, every file written to
//! one (with its content, so a test can hunt for a planted secret), every run of
//! `rsync` and every run of `ssh` into a dump file. The dump file is a real
//! temporary file, because the loader is handed a descriptor. Nothing touches
//! the machine.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::VecDeque;
use std::fs::File;
use std::io::Write as _;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::Duration;

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::AdapterBinary;

use crate::transfer::model::dump_file::DumpFile;
use crate::transfer::model::rsync_slice_outcome::RsyncSliceOutcome;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_work_host::TransferWorkHost;

/// One run of `rsync` as the host saw it.
#[derive(Clone)]
pub(crate) struct RecordedSlice {
    /// The account the run was made as.
    pub(crate) account: String,
    /// The program.
    pub(crate) program: PathBuf,
    /// The argument vector.
    pub(crate) arguments: Vec<String>,
    /// The deadline the run was given.
    pub(crate) deadline: Duration,
}

/// What the host has been asked to do so far.
#[derive(Default)]
struct Record {
    /// Room checks made: the account and the bytes asked for.
    room_checks: Vec<(String, u64)>,
    /// Receiver directories made.
    receivers_created: Vec<PathBuf>,
    /// Receiver directories removed.
    receivers_removed: Vec<PathBuf>,
    /// Files written into receiver directories, with their content.
    receiver_files: Vec<(PathBuf, Vec<u8>)>,
    /// Runs of rsync.
    slices: Vec<RecordedSlice>,
    /// Dump files made.
    dumps_created: Vec<PathBuf>,
    /// Dump files removed.
    dumps_removed: Vec<PathBuf>,
    /// The argument vectors of the runs of ssh into a dump file.
    ssh_runs: Vec<Vec<String>>,
}

/// A host whose every answer is chosen by the test.
pub(crate) struct FakeTransferWorkHost {
    /// What has happened.
    record: Mutex<Record>,
    /// The outcomes the next runs of rsync answer with, in order.
    outcomes: Mutex<VecDeque<RsyncSliceOutcome>>,
    /// Whether the room check refuses.
    refuse_room: bool,
    /// What the dump command writes into the file.
    dump_text: String,
    /// The exit status of the dump command.
    dump_status: i32,
    /// Whether the dump command crosses the byte cap.
    dump_too_large: bool,
    /// Where dump files live.
    directory: tempfile::TempDir,
}

impl FakeTransferWorkHost {
    /// A host that answers every room check with room, finishes the first run of
    /// rsync and has a dump command that writes a small dump and exits 0.
    pub(crate) fn new() -> Self {
        Self {
            record: Mutex::new(Record::default()),
            outcomes: Mutex::new(VecDeque::new()),
            refuse_room: false,
            dump_text: "CREATE TABLE t (id int);\n".to_owned(),
            dump_status: 0,
            dump_too_large: false,
            directory: tempfile::tempdir().unwrap(),
        }
    }

    /// Answers the runs of rsync with `outcomes`, in order; a run past the end
    /// finishes.
    pub(crate) fn running(self, outcomes: Vec<RsyncSliceOutcome>) -> Self {
        *self.outcomes.lock().unwrap() = outcomes.into();
        self
    }

    /// Makes the room check refuse.
    pub(crate) fn without_room(mut self) -> Self {
        self.refuse_room = true;
        self
    }

    /// Makes the dump command write `text` and exit with `status`.
    pub(crate) fn dumping(mut self, text: &str, status: i32) -> Self {
        self.dump_text = text.to_owned();
        self.dump_status = status;
        self
    }

    /// Makes the dump command cross the byte cap.
    pub(crate) fn dumping_too_much(mut self) -> Self {
        self.dump_too_large = true;
        self
    }

    /// The room checks made.
    pub(crate) fn room_checks(&self) -> Vec<(String, u64)> {
        self.record.lock().unwrap().room_checks.clone()
    }

    /// The receiver directories made.
    pub(crate) fn receivers_created(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().receivers_created.clone()
    }

    /// The receiver directories removed.
    pub(crate) fn receivers_removed(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().receivers_removed.clone()
    }

    /// Every file written into a receiver directory.
    pub(crate) fn receiver_files(&self) -> Vec<(PathBuf, Vec<u8>)> {
        self.record.lock().unwrap().receiver_files.clone()
    }

    /// The runs of rsync.
    pub(crate) fn slices(&self) -> Vec<RecordedSlice> {
        self.record.lock().unwrap().slices.clone()
    }

    /// The dump files made.
    pub(crate) fn dumps_created(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().dumps_created.clone()
    }

    /// The dump files removed.
    pub(crate) fn dumps_removed(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().dumps_removed.clone()
    }

    /// The runs of ssh into a dump file.
    pub(crate) fn ssh_runs(&self) -> Vec<Vec<String>> {
        self.record.lock().unwrap().ssh_runs.clone()
    }
}

impl TransferWorkHost for FakeTransferWorkHost {
    fn require_home_room(&self, account: &AccountName, needed: u64) -> Result<(), TransferOpError> {
        self.record
            .lock()
            .unwrap()
            .room_checks
            .push((account.as_str().to_owned(), needed));

        if self.refuse_room {
            Err(TransferOpError::DiskInsufficient)
        } else {
            Ok(())
        }
    }

    fn create_receiver_directory(
        &self,
        _account: &AccountName,
    ) -> Result<PathBuf, TransferOpError> {
        let mut record = self.record.lock().unwrap();
        let path = PathBuf::from(format!("/receivers/{:02}", record.receivers_created.len()));
        record.receivers_created.push(path.clone());

        Ok(path)
    }

    fn write_receiver_file(
        &self,
        _account: &AccountName,
        path: &Path,
        content: &[u8],
    ) -> Result<(), TransferOpError> {
        self.record
            .lock()
            .unwrap()
            .receiver_files
            .push((path.to_path_buf(), content.to_vec()));

        Ok(())
    }

    fn remove_receiver_directory(&self, path: &Path) -> Result<(), TransferOpError> {
        self.record
            .lock()
            .unwrap()
            .receivers_removed
            .push(path.to_path_buf());

        Ok(())
    }

    fn run_rsync_slice(
        &self,
        account: &AccountName,
        program: &AdapterBinary,
        arguments: &[&str],
        deadline: Duration,
    ) -> Result<RsyncSliceOutcome, TransferOpError> {
        self.record.lock().unwrap().slices.push(RecordedSlice {
            account: account.as_str().to_owned(),
            program: program.as_path().to_path_buf(),
            arguments: arguments
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
            deadline,
        });

        Ok(self
            .outcomes
            .lock()
            .unwrap()
            .pop_front()
            .unwrap_or(RsyncSliceOutcome::Finished))
    }

    fn create_dump_file(&self, needed: u64) -> Result<DumpFile, TransferOpError> {
        if self.refuse_room {
            let _ = needed;
            return Err(TransferOpError::DiskInsufficient);
        }
        let mut record = self.record.lock().unwrap();
        let path = self
            .directory
            .path()
            .join(format!("dump-{:02}.sql", record.dumps_created.len()));
        let file = std::fs::OpenOptions::new()
            .read(true)
            .write(true)
            .create_new(true)
            .open(&path)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;
        record.dumps_created.push(path.clone());

        Ok(DumpFile { file, path })
    }

    fn run_ssh_into(
        &self,
        _program: &AdapterBinary,
        arguments: &[&str],
        output: &File,
        cap: u64,
        _deadline: Duration,
    ) -> Result<i32, TransferOpError> {
        self.record.lock().unwrap().ssh_runs.push(
            arguments
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
        );

        if self.dump_too_large {
            return Err(TransferOpError::OutputBound(
                maran_agent_core::utils::bound_exceeded::BoundExceeded::OutputTooLarge {
                    program: "ssh".to_owned(),
                    stream: maran_agent_core::utils::output_stream::OutputStream::Stdout,
                    cap,
                },
            ));
        }
        let mut writer = output;
        writer.write_all(self.dump_text.as_bytes()).unwrap();

        Ok(self.dump_status)
    }

    fn remove_dump_file(&self, path: &Path) -> Result<(), TransferOpError> {
        self.record
            .lock()
            .unwrap()
            .dumps_removed
            .push(path.to_path_buf());
        let _ = std::fs::remove_file(path);

        Ok(())
    }
}
