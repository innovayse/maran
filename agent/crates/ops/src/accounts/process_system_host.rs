//! The [`SystemHost`] that actually runs programs on this machine.

use std::fs;
use std::os::unix::fs::MetadataExt;
use std::path::Path;
use std::time::Duration;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::directory::directory_size;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_distro::{AdapterBinary, DistroAdapter};

use crate::accounts::model::home_metadata::HomeMetadata;
use crate::accounts::{AccountError, CommandOutcome, SystemHost};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

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
    /// an argv array, and it is bounded: the child gets a group of its own, a
    /// deadline taken from `class` and a ceiling on what it may print. The
    /// `LC_ALL=C` pin this file used to carry moved there with it and is now on
    /// every spawn in the agent rather than on this one file's:
    /// `quota` links gettext, and a translated header made its parse silently
    /// yield "unlimited", while `remove_crontab` reads `crontab`'s own message to
    /// decide there was nothing to remove.
    ///
    /// # Errors
    ///
    /// Returns [`AccountError::CommandUnavailable`] when the program cannot be
    /// started, carrying the operating system's reason, and
    /// [`AccountError::Bound`] when it outlived its deadline or its output
    /// ceiling. A bound is never reported as "unavailable": the tool ran, and may
    /// have done its work. A non-zero exit is not an error here — it comes back
    /// in the outcome for the operations to read.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, AccountError> {
        spawn_argv(program.as_path(), arguments, class)
            .map_err(|error| AccountError::from_spawn(program, error))
    }

    /// Looks a user up with `id`.
    ///
    /// `id` answers for every name source the host is configured with — local
    /// files, LDAP, anything in `nsswitch.conf` — where reading `/etc/passwd`
    /// directly would only see one of them and report a name as free that is not.
    ///
    /// A `Query`: `id` reads the name services and changes nothing. `--` ends
    /// option parsing in coreutils' `id` on both families.
    fn user_exists(&self, username: &str) -> Result<bool, AccountError> {
        Ok(self
            .run(
                &self.distro.id_binary(),
                &["-u", END_OF_OPTIONS, username],
                CallClass::Query,
            )?
            .status
            == 0)
    }

    /// Measures a directory tree by walking it.
    ///
    /// Walks rather than shelling out to `du`: the walk needs no external program,
    /// and a missing tree is answered as zero rather than as a command failure —
    /// which is what a caller measuring a home directory before deleting it wants.
    fn directory_size(&self, path: &str) -> Result<u64, AccountError> {
        Ok(directory_size(Path::new(path)))
    }

    /// Reads the host's own passwd file, whole.
    fn read_password_database(&self, path: &str) -> Result<String, AccountError> {
        fs::read_to_string(path).map_err(|_| AccountError::HomeInspection {
            reason: format!("could not read {path}"),
        })
    }

    /// Reads `/proc/mounts` from the kernel's own procfs, whole.
    fn read_mounts(&self) -> Result<String, AccountError> {
        fs::read_to_string("/proc/mounts").map_err(|_| AccountError::HomeInspection {
            reason: "could not read /proc/mounts".to_owned(),
        })
    }

    /// Reads `path`'s metadata with `lstat` — `std::fs::symlink_metadata`,
    /// which is precisely the syscall that does not follow a symlink.
    fn home_metadata(&self, path: &str) -> Result<Option<HomeMetadata>, AccountError> {
        match fs::symlink_metadata(path) {
            Ok(metadata) => Ok(Some(HomeMetadata {
                is_symlink: metadata.file_type().is_symlink(),
                is_directory: metadata.file_type().is_dir(),
                owner_uid: metadata.uid(),
                group_gid: metadata.gid(),
                device: metadata.dev(),
            })),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(AccountError::HomeInspection {
                reason: format!("could not read the metadata of {path}"),
            }),
        }
    }

    /// Sleeps the calling thread for `interval`.
    ///
    /// Every caller is already inside the rpc's `spawn_blocking`, so this parks
    /// a blocking-pool thread and never a runtime worker.
    fn pause(&self, interval: Duration) {
        std::thread::sleep(interval);
    }
}

#[cfg(test)]
#[path = "../tests/accounts/process_system_host_tests.rs"]
mod tests;
