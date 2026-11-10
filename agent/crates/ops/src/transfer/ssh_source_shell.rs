//! The [`SourceShell`] that runs each command over `ssh`.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::transfer::classify_ssh_failure::classify_ssh_failure;
use crate::transfer::model::ssh_connection::SshConnection;
use crate::transfer::remote_command::RemoteCommand;
use crate::transfer::source_shell::SourceShell;
use crate::transfer::ssh_argv::ssh_argv;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_scratch::TransferScratch;

/// The most standard output one remote command may produce, in bytes: exactly
/// this many are accepted, one more is [`TransferOpError::OutputBound`].
pub(crate) const SOURCE_OUTPUT_LIMIT_BYTES: u64 = 1024 * 1024;

/// The exit status `ssh` itself uses when it could not complete the connection.
const SSH_FAILURE_STATUS: i32 = 255;

/// One open, pinned connection to a source, ready to run commands.
///
/// It owns the scratch directory holding the private key and the known_hosts
/// file, so the key lives exactly as long as this value: dropping it removes the
/// directory, on every path out of the operation that opened it.
pub struct SshSourceShell<'a> {
    /// The host that spawns the client.
    host: &'a dyn TransferHost,
    /// The platform facts, for the client's absolute path.
    distro: &'a dyn DistroAdapter,
    /// The resolved, laid-out connection every run uses.
    connection: SshConnection,
    /// The directory holding the key; removed when this value is dropped.
    _scratch: TransferScratch<'a>,
}

impl<'a> SshSourceShell<'a> {
    /// A shell over `connection`, which keeps `scratch` alive until it is
    /// dropped.
    #[must_use]
    pub fn new(
        host: &'a dyn TransferHost,
        distro: &'a dyn DistroAdapter,
        connection: SshConnection,
        scratch: TransferScratch<'a>,
    ) -> Self {
        Self {
            host,
            distro,
            connection,
            _scratch: scratch,
        }
    }
}

impl SourceShell for SshSourceShell<'_> {
    /// Runs the rendered command through `ssh` with the pinned options.
    ///
    /// Status 255 is the client's own "the connection did not complete" and is
    /// classified from its words; any other status is the remote command's and
    /// comes back as an outcome.
    ///
    /// # Errors
    ///
    /// As documented on [`SourceShell::run`].
    fn run(&self, command: &RemoteCommand) -> Result<CommandOutcome, TransferOpError> {
        let argv = ssh_argv(&self.connection, &command.render());
        let arguments: Vec<&str> = argv.iter().map(String::as_str).collect();
        let outcome = self.host.run(
            &self.distro.ssh_binary(),
            &arguments,
            CallClass::SourceRead,
            SOURCE_OUTPUT_LIMIT_BYTES,
        )?;

        if outcome.status == SSH_FAILURE_STATUS {
            return Err(classify_ssh_failure(&outcome.stderr));
        }

        Ok(outcome)
    }
}

#[cfg(test)]
#[path = "../tests/transfer/ssh_source_shell_tests.rs"]
mod tests;
