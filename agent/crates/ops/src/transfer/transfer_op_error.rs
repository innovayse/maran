//! Errors returned by the server-migration connection operations.

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

/// Why an operation against a registered source produced no answer.
///
/// **No variant carries text from the source, an argument vector, a captured
/// stream or a byte of the credential.** The first four are the cases the panel
/// tells apart, and each is decided from the exit status and the CLIENT's own
/// words on standard error (`classify_ssh_failure.rs`), never from what the
/// source printed. The type structurally cannot hold the private key: nothing
/// that reaches it does.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum TransferOpError {
    /// The name resolved to, or the literal was, an address this agent never
    /// connects to: loopback, link-local, unspecified, multicast, broadcast, or
    /// one of this host's own addresses.
    #[error("the source address is one this agent never connects to")]
    AddressRefused,
    /// The source presented a host key other than the pinned one, or none of
    /// the pinned algorithm. A possible interception, or the source was
    /// reinstalled. Never retried.
    #[error("the source's host key is not the pinned one")]
    HostKeyChanged,
    /// The source refused the key: not in `authorized_keys`, the wrong user, or
    /// a restriction rejected the connection.
    #[error("the source refused the key")]
    AuthRefused,
    /// No SSH answer: refused, filtered, timed out, closed during the
    /// handshake, or not resolvable.
    #[error("the source did not answer")]
    Unreachable,
    /// `ssh-keygen` did not produce a key pair this agent accepts.
    #[error("a key pair could not be generated")]
    KeyGenerationFailed,
    /// The root-only scratch area could not be created, written, read or
    /// removed.
    #[error("the transfer scratch area is unusable")]
    ScratchUnavailable,
    /// This host's own addresses could not be listed, so the address policy
    /// cannot be applied. The connection fails closed.
    #[error("this host's own addresses could not be listed")]
    HostAddressesUnavailable,
    /// A deadline passed or an output cap was crossed, and the child's process
    /// group was killed. It is never read as an empty answer.
    #[error(transparent)]
    OutputBound(BoundExceeded),
    /// A program could not be started, fed or waited for.
    #[error(transparent)]
    Spawn(SpawnError),
    /// The source's answer did not match the grammar of the command that asked
    /// for it. The text is not kept.
    #[error("the source's answer was not in the expected form")]
    InvalidRemoteOutput,
    /// This host has no room for the transfer: the free-space floor would be
    /// crossed, or the filesystem is full.
    #[error("there is not enough room on this host for the transfer")]
    DiskInsufficient,
    /// `rsync` ended with a status that is a failure (an unreadable source file,
    /// a connection that dropped, a quota reached). The status is kept; nothing
    /// `rsync` printed is.
    #[error("the copy of the files ended with status {status}")]
    PullFailed {
        /// The exit status.
        status: i32,
    },
    /// The pull used up its time allowance without finishing.
    #[error("the copy of the files did not finish within its time allowance")]
    PullIncomplete,
    /// The dump command on the source ended with a failure status.
    #[error("the dump of the database ended with status {status}")]
    DumpFailed {
        /// The exit status.
        status: i32,
    },
    /// The dump is larger than the allowance the request stated.
    #[error("the dump of the database is larger than the allowance")]
    DumpTooLarge,
    /// The restricted loader's scan refused the dump.
    #[error("the dump was refused by the restricted loader")]
    LoadRefused,
    /// The server refused a statement of the dump, or the load could not run.
    #[error("the dump could not be loaded")]
    LoadFailed,
    /// The client that asked for the transfer left; it was stopped at its next
    /// boundary.
    #[error("the client left and the transfer was stopped")]
    Cancelled,
}

impl From<SpawnError> for TransferOpError {
    /// Keeps a bound apart from every other way a spawn can fail.
    ///
    /// A deadline or a cap is [`Self::OutputBound`], carried by name, because a
    /// program that was killed has been shown to be neither unreachable nor
    /// refused; everything else is [`Self::Spawn`].
    fn from(error: SpawnError) -> Self {
        match error {
            SpawnError::Bound(bound) => Self::OutputBound(bound),
            other => Self::Spawn(other),
        }
    }
}

#[cfg(test)]
#[path = "../tests/transfer/transfer_op_error_tests.rs"]
mod tests;
