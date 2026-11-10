//! The seam between the inventory and the way a source's commands are run.

use maran_agent_core::command_outcome::CommandOutcome;

use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::transfer_op_error::TransferOpError;

/// Runs [`RemoteCommand`]s on one registered source.
///
/// A trait so the inventory is driven against a fake in tests and so that the
/// only thing a caller can ask a source is a variant of the closed enum: there is
/// no method that takes text. The production implementation is
/// [`crate::transfer::ssh_source_shell::SshSourceShell`].
pub trait SourceShell {
    /// Runs `command` on the source and returns its exit status and output.
    ///
    /// A command that ran and exited non-zero is an OUTCOME, not an error: its
    /// status is the caller's decision (a user without a crontab exits 1). The
    /// standard output is bounded at one mebibyte; the standard error that the
    /// remote command wrote is discarded on the source and is never returned.
    ///
    /// # Errors
    ///
    /// Returns [`TransferOpError::HostKeyChanged`], [`TransferOpError::AuthRefused`]
    /// or [`TransferOpError::Unreachable`] when the connection itself failed,
    /// [`TransferOpError::OutputBound`] when the command crossed its deadline or
    /// output cap (the answer is whole or absent, never cut short), and
    /// [`TransferOpError::Spawn`] when the client could not be run.
    fn run(&self, command: &RemoteCommand) -> Result<CommandOutcome, TransferOpError>;
}
