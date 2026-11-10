//! The seam between the connection operations and the machine they run on.

use std::net::IpAddr;
use std::path::{Path, PathBuf};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;
use maran_distro::AdapterBinary;

use crate::transfer::transfer_op_error::TransferOpError;

/// What the migration connection operations need from this host: a root-only
/// scratch area, the program spawn, name resolution and the list of this host's
/// own addresses.
///
/// A trait, because every one of them touches the real machine: a test that
/// used the real one would resolve real names, write real key files and spawn
/// real `ssh`. Behind this seam the answers are chosen by the test and the
/// decisions (the address policy, the argv, the classification of a failure,
/// the removal of the key) are what is under examination.
///
/// Implementations MUST spawn with an argv array against a program taken from
/// the `DistroAdapter`, never through a shell and never by a name resolved
/// through `PATH` (rules/security.md item 3).
pub trait TransferHost: Send + Sync {
    /// Creates a fresh directory only this process can enter and returns its
    /// path. Each call returns a new one; none is reused.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when the area is not a
    /// root-only directory this process owns, or the new directory cannot be
    /// created.
    fn create_scratch(&self) -> Result<PathBuf, TransferOpError>;

    /// Removes a directory [`Self::create_scratch`] returned, with everything in
    /// it. Removing one that is already gone succeeds.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when it could not be
    /// removed.
    fn remove_scratch(&self, scratch: &Path) -> Result<(), TransferOpError>;

    /// Writes `content` to a NEW file at `path` with mode `0600`, owned by this
    /// process. The file must not exist.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when the file exists or
    /// cannot be written.
    fn write_private_file(&self, path: &Path, content: &[u8]) -> Result<(), TransferOpError>;

    /// Reads a small text file whole.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when the file cannot be
    /// read, is larger than `limit` bytes, or is not UTF-8.
    fn read_small_file(&self, path: &Path, limit: u64) -> Result<String, TransferOpError>;

    /// Runs `program` with `arguments` as an argv array, standard input closed,
    /// under the deadline of `class`, keeping at most `stdout_cap` bytes of
    /// standard output (and a fixed small cap of standard error).
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::OutputBound`] when the program was killed at
    /// its deadline or crossed a cap, and [`TransferOpError::Spawn`] when it
    /// could not be started or waited for. A program that ran and exited
    /// non-zero is NOT an error here: its status is the caller's decision.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
        stdout_cap: u64,
    ) -> Result<CommandOutcome, TransferOpError>;

    /// Resolves a DNS name to the addresses it has, once.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::Unreachable`] when the name does not resolve.
    fn resolve(&self, host: &RemoteHost, port: Port) -> Result<Vec<IpAddr>, TransferOpError>;

    /// Every address configured on this host's own interfaces, IPv4 and IPv6.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::HostAddressesUnavailable`] when the kernel's
    /// IPv4 table cannot be read or understood.
    fn local_addresses(&self) -> Result<Vec<IpAddr>, TransferOpError>;
}
