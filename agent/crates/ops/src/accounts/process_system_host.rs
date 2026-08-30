//! The [`SystemHost`] that actually runs programs on this machine.

use std::path::Path;
use std::process::Command;

use maran_agent_core::utils::directory::directory_size;

use crate::accounts::{AccountError, CommandOutcome, SystemHost};

/// Runs the real `useradd`, `usermod`, `userdel`, `setquota` and friends.
///
/// The only implementation that touches the machine, and deliberately the smallest
/// piece of the account module: every decision worth reviewing lives in
/// [`super::AccountOperations`], where it is tested. What is left here is spawning
/// and reading output, which cannot be unit-tested without creating real users.
pub struct ProcessSystemHost;

impl ProcessSystemHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl Default for ProcessSystemHost {
    fn default() -> Self {
        Self::new()
    }
}

impl SystemHost for ProcessSystemHost {
    /// Spawns `program` with `arguments` as an argv array.
    ///
    /// No shell is involved, at any point (rules/security.md item 3). `Command`
    /// passes the arguments to `execve` one by one, so a name containing a space, a
    /// quote or a semicolon is one argument containing those characters — there is
    /// no string for anything to re-parse. The agent also never builds a command
    /// line by concatenation, which is the other half of the same rule.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, AccountError> {
        let output = Command::new(program)
            .args(arguments)
            .output()
            .map_err(|error| AccountError::CommandUnavailable {
                program: program.to_owned(),
                reason: error.to_string(),
            })?;

        Ok(CommandOutcome {
            // -1 for a process killed by a signal: it did not exit, and reporting 0
            // would read as success to every caller that checks the status.
            status: output.status.code().unwrap_or(-1),
            stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
        })
    }

    /// Looks a user up with `id`.
    ///
    /// `id` answers for every name source the host is configured with — local
    /// files, LDAP, anything in `nsswitch.conf` — where reading `/etc/passwd`
    /// directly would only see one of them and report a name as free that is not.
    fn user_exists(&self, username: &str) -> Result<bool, AccountError> {
        Ok(self.run("id", &["-u", username])?.status == 0)
    }

    /// Measures a directory tree by walking it.
    ///
    /// Walks rather than shelling out to `du`: the walk needs no external program,
    /// and a missing tree is answered as zero rather than as a command failure —
    /// which is what a caller measuring a home directory before deleting it wants.
    fn directory_size(&self, path: &str) -> Result<u64, AccountError> {
        Ok(directory_size(Path::new(path)))
    }
}
