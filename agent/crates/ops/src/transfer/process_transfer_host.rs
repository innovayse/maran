//! The [`TransferHost`] that actually touches this machine.

use std::fs::{self, DirBuilder, File, OpenOptions};
use std::io::{ErrorKind, Read, Write};
use std::net::{IpAddr, ToSocketAddrs};
use std::os::unix::fs::{DirBuilderExt, MetadataExt, OpenOptionsExt};
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::child_output::ChildOutput;
use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::utils::read_host_addresses::read_host_addresses;
use maran_agent_core::utils::run_bounded::run_bounded;
use maran_agent_core::utils::spawn_request::SpawnRequest;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;
use maran_distro::AdapterBinary;

use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// The most standard error any program run through this host may write. `ssh`
/// at `LogLevel=ERROR` prints a line; this is far above that and far below the
/// core's default.
const STDERR_CAP: u64 = 64 * 1024;

/// Length of the random part of a scratch directory's name, in bytes.
const SCRATCH_NAME_BYTES: usize = 16;

/// Creates root-only scratch directories, spawns the SSH client programs and
/// reads this machine's own addresses.
///
/// The smallest piece of the area: every decision worth reviewing (which
/// address is allowed, what the argv is, how a failure is told apart) lives in
/// the operations, where a test chooses the input.
pub struct ProcessTransferHost {
    /// The root-only directory under which each call's scratch directory is made.
    scratch_root: PathBuf,
}

impl ProcessTransferHost {
    /// A host using the agent's own transfer scratch area.
    #[must_use]
    pub fn new() -> Self {
        Self::at(PathBuf::from(AgentPaths::TRANSFER_SCRATCH_DIRECTORY))
    }

    /// A host using `scratch_root`, which must be a directory this process owns
    /// with no group or other permission. Tests use a temporary directory.
    #[must_use]
    pub fn at(scratch_root: PathBuf) -> Self {
        Self { scratch_root }
    }

    /// Creates the scratch area when it is absent and proves it is a root-only
    /// directory this process owns: not a link, mode without group or other
    /// bits, owned by the current user.
    fn ensure_scratch_root(&self) -> Result<(), TransferOpError> {
        DirBuilder::new()
            .recursive(true)
            .mode(0o700)
            .create(&self.scratch_root)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;

        let metadata = fs::symlink_metadata(&self.scratch_root)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;
        let owner = current_uid().map_err(|_| TransferOpError::ScratchUnavailable)?;

        if !metadata.is_dir() || metadata.uid() != owner || metadata.mode() & 0o077 != 0 {
            return Err(TransferOpError::ScratchUnavailable);
        }

        Ok(())
    }

    /// Whether `path` lies below the scratch area, by its components.
    fn is_inside_scratch(&self, path: &Path) -> bool {
        path.starts_with(&self.scratch_root)
            && !path
                .components()
                .any(|part| matches!(part, std::path::Component::ParentDir))
    }
}

impl Default for ProcessTransferHost {
    /// The agent's own scratch area.
    fn default() -> Self {
        Self::new()
    }
}

impl TransferHost for ProcessTransferHost {
    /// Makes `<scratch root>/<random hex>` with mode `0700`.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when the root is not a
    /// root-only directory of this process, randomness is unavailable, or the
    /// directory cannot be created (an existing one is never reused).
    fn create_scratch(&self) -> Result<PathBuf, TransferOpError> {
        self.ensure_scratch_root()?;

        let mut random = [0_u8; SCRATCH_NAME_BYTES];
        getrandom::fill(&mut random).map_err(|_| TransferOpError::ScratchUnavailable)?;
        let name: String = random.iter().map(|byte| format!("{byte:02x}")).collect();
        let path = self.scratch_root.join(name);

        DirBuilder::new()
            .mode(0o700)
            .create(&path)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;

        Ok(path)
    }

    /// Removes a directory this host created, never anything else.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when `scratch` is not
    /// a direct child of the scratch area, is not a real directory, or cannot
    /// be removed. A directory that is already gone is success.
    fn remove_scratch(&self, scratch: &Path) -> Result<(), TransferOpError> {
        let direct_child = scratch.parent() == Some(self.scratch_root.as_path());
        if !direct_child || !self.is_inside_scratch(scratch) {
            return Err(TransferOpError::ScratchUnavailable);
        }

        match fs::symlink_metadata(scratch) {
            Ok(metadata) if metadata.is_dir() => fs::remove_dir_all(scratch)
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

    /// Creates the file exclusively with mode `0600` and writes `content`.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when `path` is outside
    /// the scratch area, exists, or cannot be written.
    fn write_private_file(&self, path: &Path, content: &[u8]) -> Result<(), TransferOpError> {
        if !self.is_inside_scratch(path) {
            return Err(TransferOpError::ScratchUnavailable);
        }

        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .mode(0o600)
            .open(path)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;

        file.write_all(content)
            .map_err(|_| TransferOpError::ScratchUnavailable)
    }

    /// Reads a file of the scratch area whole.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::ScratchUnavailable`] when `path` is outside
    /// the scratch area, cannot be read, is longer than `limit` or is not
    /// UTF-8.
    fn read_small_file(&self, path: &Path, limit: u64) -> Result<String, TransferOpError> {
        if !self.is_inside_scratch(path) {
            return Err(TransferOpError::ScratchUnavailable);
        }

        let file = File::open(path).map_err(|_| TransferOpError::ScratchUnavailable)?;
        let mut text = String::new();
        file.take(limit.saturating_add(1))
            .read_to_string(&mut text)
            .map_err(|_| TransferOpError::ScratchUnavailable)?;

        if text.len() as u64 > limit {
            return Err(TransferOpError::ScratchUnavailable);
        }

        Ok(text)
    }

    /// Spawns `program` through the bounded core with standard input closed.
    ///
    /// No shell is involved (rules/security.md item 3); `program` comes from the
    /// `DistroAdapter` and the arguments from the argv builders of this area.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::OutputBound`] at a deadline or cap and
    /// [`TransferOpError::Spawn`] when the program cannot be started or waited
    /// for.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
        stdout_cap: u64,
    ) -> Result<CommandOutcome, TransferOpError> {
        run_bounded(SpawnRequest {
            program: program.as_path(),
            arguments,
            class,
            input: ChildInput::Null,
            output: ChildOutput::Capture {
                stdout_cap,
                stderr_cap: STDERR_CAP,
            },
        })
        .map_err(TransferOpError::from)
    }

    /// Asks the system resolver for the name's addresses, once.
    ///
    /// The lookup is the resolver's own and is bounded by its configured
    /// timeouts, not by this agent.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::Unreachable`] when the name does not resolve
    /// to any address.
    fn resolve(&self, host: &RemoteHost, port: Port) -> Result<Vec<IpAddr>, TransferOpError> {
        let resolved = (host.as_str(), port.value())
            .to_socket_addrs()
            .map_err(|_| TransferOpError::Unreachable)?;
        let mut addresses: Vec<IpAddr> = Vec::new();
        for socket in resolved {
            if !addresses.contains(&socket.ip()) {
                addresses.push(socket.ip());
            }
        }

        if addresses.is_empty() {
            return Err(TransferOpError::Unreachable);
        }

        Ok(addresses)
    }

    /// Reads the kernel's IPv4 and IPv6 address tables.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::HostAddressesUnavailable`] when the IPv4 table
    /// cannot be read or is over its bound, or the IPv6 table exists and cannot
    /// be read. An absent IPv6 table is IPv6 turned off, which lists nothing.
    fn local_addresses(&self) -> Result<Vec<IpAddr>, TransferOpError> {
        read_host_addresses()
            .map(|host| host.addresses)
            .map_err(TransferOpError::HostAddressesUnavailable)
    }
}

#[cfg(test)]
#[path = "../tests/transfer/process_transfer_host_tests.rs"]
mod tests;
