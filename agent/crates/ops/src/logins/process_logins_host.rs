//! The [`LoginsHost`] that actually touches this machine.

use std::fs;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::utils::system_account::SystemAccount;
use maran_agent_core::utils::system_accounts::system_accounts;

use crate::logins::logins_error::LoginsError;
use crate::logins::logins_host::LoginsHost;

/// Reads the real password database and runs the real `passwd` and `usermod`.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: every decision worth reviewing lives in the
/// operations, where it is tested against a fake. What is left here is reading
/// one file and spawning one program.
pub struct ProcessLoginsHost;

impl ProcessLoginsHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl Default for ProcessLoginsHost {
    /// The host has no state, so the default is the only value there is.
    fn default() -> Self {
        Self::new()
    }
}

impl LoginsHost for ProcessLoginsHost {
    /// Reads the passwd file and parses it with the one parser this repository
    /// has.
    ///
    /// [`system_accounts`] is what the monitoring area enumerates accounts
    /// with too: where a home or a uid field sits in a passwd line is a
    /// question about the host and not about file transfer, so the two areas
    /// read it through one unit rather than each counting fields for itself.
    ///
    /// The file rather than a `getpwent` walk, and that is a deliberate
    /// narrowing: enumerating the password database through libc means holding
    /// iterator state across a root process's threads, whereas every login this
    /// panel creates is a local entry in this one file. What is given up is
    /// visibility of logins served by LDAP or another name service — which this
    /// panel never creates, and which it must not lock.
    ///
    /// # Errors
    ///
    /// Returns [`LoginsError::AccountMissing`] when the file cannot be read.
    fn read_passwd(&self, passwd_database: &str) -> Result<Vec<SystemAccount>, LoginsError> {
        let passwd =
            fs::read_to_string(passwd_database).map_err(|_| LoginsError::AccountMissing)?;

        Ok(system_accounts(&passwd))
    }

    /// Spawns `program` with `arguments` as an argv array.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse. `program` comes from the `DistroAdapter`'s
    /// allow-list and never from a request. The spawn itself is [`spawn_argv`],
    /// shared with every other host that runs an argv array and needs nothing
    /// on the child's standard input.
    ///
    /// # Errors
    ///
    /// Returns [`LoginsError::SpawnFailed`] with a `code` of `-1` when the
    /// program cannot be started at all.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, LoginsError> {
        spawn_argv(program, arguments).map_err(|_| LoginsError::program_unavailable())
    }
}
