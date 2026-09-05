//! The [`SystemHost`] that actually runs programs on this machine.

use std::path::Path;

use maran_agent_core::utils::directory::directory_size;
use maran_agent_core::utils::spawn_argv::spawn_argv;
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

impl SystemHost for ProcessSystemHost {
    /// Spawns `program` with `arguments` as an argv array.
    ///
    /// No shell is involved, at any point (rules/security.md item 3). `Command`
    /// passes the arguments to `execve` one by one, so a name containing a space, a
    /// quote or a semicolon is one argument containing those characters — there is
    /// no string for anything to re-parse. The agent also never builds a command
    /// line by concatenation, which is the other half of the same rule.
    ///
    /// The spawn itself is [`spawn_argv`], shared with every other host that runs
    /// an argv array. The `LC_ALL=C` pin this file used to carry moved there with
    /// it and is now on every spawn in the agent rather than on this one file's:
    /// `quota` links gettext, and a translated header made its parse silently
    /// yield "unlimited", while `remove_crontab` reads `crontab`'s own message to
    /// decide there was nothing to remove.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandUnavailable`] when the program cannot be
    /// started, carrying the operating system's reason. A non-zero exit is not an
    /// error here — it comes back in the outcome for the operations to read.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, AccountError> {
        spawn_argv(program, arguments).map_err(|error| AccountError::CommandUnavailable {
            program: program.to_owned(),
            reason: error.to_string(),
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
