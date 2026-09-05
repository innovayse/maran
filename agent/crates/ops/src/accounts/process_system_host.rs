//! The [`SystemHost`] that actually runs programs on this machine.

use std::path::Path;
use std::process::Command;

use maran_agent_core::utils::directory::directory_size;
use maran_distro::DistroAdapter;

use crate::accounts::{AccountError, CommandOutcome, SystemHost};

/// Runs the real `useradd`, `usermod`, `userdel`, `setquota` and friends.
///
/// The only implementation that touches the machine, and deliberately the smallest
/// piece of the account module: every decision worth reviewing lives in
/// [`super::AccountOperations`], where it is tested. What is left here is spawning
/// and reading output, which cannot be unit-tested without creating real users.
pub struct ProcessSystemHost {
    /// Where the absolute path of every tool this host runs comes from.
    ///
    /// Held even though the operations above pass their own program paths,
    /// because ONE call originates here rather than in `AccountOperations`:
    /// [`SystemHost::user_exists`] chooses the tool itself, and a bare name
    /// there would be resolved through `PATH` by a process running as uid 0.
    /// That is exactly the substitution the rest of this module was changed to
    /// close, and it survived the first pass — the sweep test that was meant to
    /// catch it runs against a fake host, where this method is stubbed and
    /// spawns nothing.
    distro: &'static dyn DistroAdapter,
}

impl ProcessSystemHost {
    /// Creates the host bound to this machine's distribution adapter.
    ///
    /// `distro` is the adapter naming the absolute path of each tool.
    #[must_use]
    pub fn new(distro: &'static dyn DistroAdapter) -> Self {
        Self { distro }
    }
}

/// The locale variable every spawn in this file sets.
///
/// `LC_ALL` and not `LANG`, because `LC_ALL` overrides every other locale
/// variable — one assignment settles the question whatever the daemon's own
/// environment holds. The cron module pins the same variable for the same
/// reason, and this file had not, which made an account with no crontab
/// undeletable under any non-English locale: `remove_crontab` decides "there
/// was nothing to remove" by reading `crontab`'s own message, and a message in
/// another language is a refusal it cannot recognise. Nothing sets a locale on
/// the agent's unit, so the daemon's environment decided it.
const LOCALE_VARIABLE: &str = "LC_ALL";

/// The locale every spawn in this file runs under.
///
/// `C`, so the diagnostics this host reads back are the ones its matching was
/// written against. It is pinned on the SPAWN rather than per call site: every
/// decision here that reads a program's output has the same exposure, and a
/// rule honoured at one call site and forgotten at the next is the shape of
/// defect this repository keeps finding.
const LOCALE_VALUE: &str = "C";

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
            .env(LOCALE_VARIABLE, LOCALE_VALUE)
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
        Ok(self.run(self.distro.id_binary(), &["-u", username])?.status == 0)
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

#[cfg(test)]
#[path = "../tests/accounts/process_system_host_tests.rs"]
mod tests;
