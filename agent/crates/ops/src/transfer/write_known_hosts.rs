//! The agent's own known_hosts file for one connection.

use std::path::Path;

use maran_agent_core::validation::web::host_key::HostKey;

use crate::transfer::known_hosts_line::known_hosts_line;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// Writes the pinned host key as the only line of a new known_hosts file at
/// `path`.
///
/// The file is the agent's own, written from the key the administrator
/// confirmed; the client reads no other (`GlobalKnownHostsFile` and `-F` are
/// pointed at nothing in `ssh_argv`). With `StrictHostKeyChecking=yes` any other
/// key, or no key of the pinned algorithm, ends the connection before
/// authentication.
///
/// # Errors
///
/// Returns [`TransferOpError::ScratchUnavailable`] when the file cannot be
/// created.
pub fn write_known_hosts(
    host: &dyn TransferHost,
    path: &Path,
    pinned: &HostKey,
) -> Result<(), TransferOpError> {
    let line = known_hosts_line(pinned);

    host.write_private_file(path, line.as_bytes())
}

#[cfg(test)]
#[path = "../tests/transfer/write_known_hosts_tests.rs"]
mod tests;
