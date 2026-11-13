//! The in-memory [`TransferHost`] the connection tests decide against.
//!
//! It records every scratch directory made and removed, every file written
//! (with its content, so a test can hunt for a planted secret), and every
//! program run (with the files that existed at that moment, so a test can prove
//! the key was on disk while `ssh` ran and gone afterwards). Nothing touches the
//! machine: names resolve from a table and the host's own addresses are chosen
//! by the test.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeMap;
use std::net::IpAddr;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::host_addresses_error::HostAddressesError;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;
use maran_distro::AdapterBinary;

use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// What a program run looked like to the host.
#[derive(Clone)]
pub(crate) struct RecordedRun {
    /// The absolute path of the program, from the adapter.
    pub(crate) program: PathBuf,
    /// The argument vector, one entry per `execve` argument.
    pub(crate) arguments: Vec<String>,
    /// The deadline class.
    pub(crate) class: CallClass,
    /// The standard output cap.
    pub(crate) stdout_cap: u64,
    /// Every file that existed when the program was started.
    pub(crate) files: BTreeMap<PathBuf, Vec<u8>>,
}

/// The answer to one program run, chosen by the test from the run itself.
pub(crate) type Responder = Box<
    dyn Fn(&FakeTransferHost, &RecordedRun) -> Result<CommandOutcome, TransferOpError>
        + Send
        + Sync,
>;

/// What the host has been asked to do so far.
#[derive(Default)]
struct Record {
    /// Scratch directories made, in order.
    created: Vec<PathBuf>,
    /// Scratch directories removed, in order.
    removed: Vec<PathBuf>,
    /// Files currently present, with their content.
    files: BTreeMap<PathBuf, Vec<u8>>,
    /// Every file ever written, kept after its scratch directory is removed.
    ever_written: Vec<(PathBuf, Vec<u8>)>,
    /// Programs run, in order.
    runs: Vec<RecordedRun>,
    /// How many names were resolved.
    resolutions: u32,
}

/// A host whose every answer is chosen by the test.
pub(crate) struct FakeTransferHost {
    /// What has happened.
    record: Mutex<Record>,
    /// How a program run is answered.
    responder: Responder,
    /// The addresses each name resolves to.
    names: BTreeMap<String, Vec<IpAddr>>,
    /// This host's own addresses, or `None` when they cannot be listed.
    local: Option<Vec<IpAddr>>,
}

/// An outcome with the given status and output.
pub(crate) fn outcome(status: i32, stdout: &str, stderr: &str) -> CommandOutcome {
    CommandOutcome {
        status,
        stdout: stdout.to_owned(),
        stderr: stderr.to_owned(),
    }
}

impl FakeTransferHost {
    /// A host that answers every program with status 0 and no output, resolves
    /// nothing and has only the loopback address of its own.
    pub(crate) fn new() -> Self {
        Self {
            record: Mutex::new(Record::default()),
            responder: Box::new(|_, _| Ok(outcome(0, "", ""))),
            names: BTreeMap::new(),
            local: Some(vec!["127.0.0.1".parse().unwrap()]),
        }
    }

    /// Answers program runs with `responder`.
    pub(crate) fn answering(mut self, responder: Responder) -> Self {
        self.responder = responder;
        self
    }

    /// Makes `name` resolve to `addresses`.
    pub(crate) fn resolving(mut self, name: &str, addresses: &[&str]) -> Self {
        self.names.insert(
            name.to_owned(),
            addresses.iter().map(|text| text.parse().unwrap()).collect(),
        );
        self
    }

    /// Gives this host the own addresses `addresses`.
    pub(crate) fn owning(mut self, addresses: &[&str]) -> Self {
        self.local = Some(addresses.iter().map(|text| text.parse().unwrap()).collect());
        self
    }

    /// Makes this host unable to list its own addresses.
    pub(crate) fn without_address_table(mut self) -> Self {
        self.local = None;
        self
    }

    /// Puts a file in place as if a program had written it.
    pub(crate) fn put_file(&self, path: &Path, content: &str) {
        self.record
            .lock()
            .unwrap()
            .files
            .insert(path.to_path_buf(), content.as_bytes().to_vec());
    }

    /// Every program run so far.
    pub(crate) fn runs(&self) -> Vec<RecordedRun> {
        self.record.lock().unwrap().runs.clone()
    }

    /// The scratch directories made.
    pub(crate) fn created(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().created.clone()
    }

    /// The scratch directories removed.
    pub(crate) fn removed(&self) -> Vec<PathBuf> {
        self.record.lock().unwrap().removed.clone()
    }

    /// Files that still exist.
    pub(crate) fn files(&self) -> BTreeMap<PathBuf, Vec<u8>> {
        self.record.lock().unwrap().files.clone()
    }

    /// The content of every file ever written through `write_private_file`.
    pub(crate) fn everything_written(&self) -> Vec<(PathBuf, Vec<u8>)> {
        self.record.lock().unwrap().ever_written.clone()
    }

    /// How many names were resolved.
    pub(crate) fn resolutions(&self) -> u32 {
        self.record.lock().unwrap().resolutions
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
        let mut record = self.record.lock().unwrap();
        record.removed.push(scratch.to_path_buf());
        record.files.retain(|path, _| !path.starts_with(scratch));

        Ok(())
    }

    fn write_private_file(&self, path: &Path, content: &[u8]) -> Result<(), TransferOpError> {
        let mut record = self.record.lock().unwrap();
        if record.files.contains_key(path) {
            return Err(TransferOpError::ScratchUnavailable);
        }
        record
            .ever_written
            .push((path.to_path_buf(), content.to_vec()));
        record.files.insert(path.to_path_buf(), content.to_vec());

        Ok(())
    }

    fn read_small_file(&self, path: &Path, limit: u64) -> Result<String, TransferOpError> {
        let record = self.record.lock().unwrap();
        let content = record
            .files
            .get(path)
            .ok_or(TransferOpError::ScratchUnavailable)?;
        if content.len() as u64 > limit {
            return Err(TransferOpError::ScratchUnavailable);
        }

        String::from_utf8(content.clone()).map_err(|_| TransferOpError::ScratchUnavailable)
    }

    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
        stdout_cap: u64,
    ) -> Result<CommandOutcome, TransferOpError> {
        let run = {
            let mut record = self.record.lock().unwrap();
            let run = RecordedRun {
                program: program.as_path().to_path_buf(),
                arguments: arguments
                    .iter()
                    .map(|argument| (*argument).to_owned())
                    .collect(),
                class,
                stdout_cap,
                files: record.files.clone(),
            };
            record.runs.push(run.clone());
            run
        };

        (self.responder)(self, &run)
    }

    fn resolve(&self, host: &RemoteHost, _port: Port) -> Result<Vec<IpAddr>, TransferOpError> {
        self.record.lock().unwrap().resolutions += 1;

        self.names
            .get(host.as_str())
            .cloned()
            .ok_or(TransferOpError::Unreachable)
    }

    fn local_addresses(&self) -> Result<Vec<IpAddr>, TransferOpError> {
        self.local
            .clone()
            .ok_or(TransferOpError::HostAddressesUnavailable(
                HostAddressesError::Ipv4Missing,
            ))
    }
}
