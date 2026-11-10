//! A [`TransferHost`] that touches nothing, for the tests of the service layer.
//!
//! It records every scratch directory made and removed and every file written,
//! answers every program run with one fixed outcome, resolves every name to one
//! fixed address list, and reports no addresses of its own.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;
use maran_distro::AdapterBinary;
use maran_ops::transfer::{TransferHost, TransferOpError};

/// What the host has been asked to do so far.
#[derive(Default)]
struct Record {
    /// Scratch directories made.
    created: Vec<PathBuf>,
    /// Scratch directories removed.
    removed: Vec<PathBuf>,
    /// Every file ever written, with its content.
    written: Vec<Vec<u8>>,
    /// How many programs were run.
    runs: u32,
}

/// A host whose every answer is fixed by the test.
pub struct FakeTransferHost {
    /// What has happened.
    record: Mutex<Record>,
    /// The answer to every program run.
    outcome: CommandOutcome,
    /// The addresses every name resolves to.
    resolved: Vec<IpAddr>,
}

impl FakeTransferHost {
    /// A host that answers every run with `status` and `stderr` and resolves every
    /// name to `resolved`.
    pub fn new(status: i32, stderr: &str, resolved: Vec<IpAddr>) -> Self {
        Self {
            record: Mutex::new(Record::default()),
            outcome: CommandOutcome {
                status,
                stdout: String::new(),
                stderr: stderr.to_owned(),
            },
            resolved,
        }
    }

    /// The scratch directories made.
    pub fn created(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().created.clone()
    }

    /// The scratch directories removed.
    pub fn removed(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().removed.clone()
    }

    /// Whether any file ever written held `needle`.
    pub fn ever_wrote(&self, needle: &[u8]) -> bool {
        self.record
            .lock()
            .unwrap()
            .written
            .iter()
            .any(|content| content.windows(needle.len()).any(|window| window == needle))
    }

    /// How many programs were run.
    pub fn runs(&self) -> u32 {
        self.record.lock().unwrap().runs
    }
}

impl TransferHost for FakeTransferHost {
    fn create_scratch(&self) -> Result<PathBuf, TransferOpError> {
        let mut record = self.record.lock().unwrap();
        let path = PathBuf::from(format!("/scratch/{:02}", record.created.len()));
        record.created.push(path.clone());

        Ok(path)
    }

    fn remove_scratch(&self, scratch: &Path) -> Result<(), TransferOpError> {
        self.record
            .lock()
            .unwrap()
            .removed
            .push(scratch.to_path_buf());

        Ok(())
    }

    fn write_private_file(&self, _path: &Path, content: &[u8]) -> Result<(), TransferOpError> {
        self.record.lock().unwrap().written.push(content.to_vec());

        Ok(())
    }

    fn read_small_file(&self, _path: &Path, _limit: u64) -> Result<String, TransferOpError> {
        Err(TransferOpError::ScratchUnavailable)
    }

    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
        _stdout_cap: u64,
    ) -> Result<CommandOutcome, TransferOpError> {
        self.record.lock().unwrap().runs += 1;

        Ok(CommandOutcome {
            status: self.outcome.status,
            stdout: self.outcome.stdout.clone(),
            stderr: self.outcome.stderr.clone(),
        })
    }

    fn resolve(&self, _host: &RemoteHost, _port: Port) -> Result<Vec<IpAddr>, TransferOpError> {
        Ok(self.resolved.clone())
    }

    fn local_addresses(&self) -> Result<Vec<IpAddr>, TransferOpError> {
        Ok(Vec::new())
    }
}
