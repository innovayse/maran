//! The [`PhpHost`] that actually touches this machine.

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;
use std::process::Command;

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::system::name::AccountName;

use crate::php::{PhpHost, PhpOpError};
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, remove_config, write_config};

/// Runs the real package manager, the real `php-fpm -t`, and the real
/// `systemctl` — the one place in this area that spawns a process.
///
/// Deliberately the smallest piece of the area: every decision worth reviewing
/// lives in the operations, where it is tested against a fake. What is left
/// here is spawning, a `stat`, and a `mkdir`.
pub struct ProcessPhpHost;

impl ProcessPhpHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl Default for ProcessPhpHost {
    fn default() -> Self {
        Self::new()
    }
}

impl ConfigHost for ProcessPhpHost {
    /// Spawns `program` with `arguments` as an argv array.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse — which matters more here than anywhere else in
    /// the agent, because one of these argv arrays contains a package name.
    /// `program` comes from the `DistroAdapter`'s allow-list and never from a
    /// request.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        let output = Command::new(program)
            .args(arguments)
            .output()
            .map_err(|error| SafeWriteError::ReloadFailed {
                stderr: format!("could not run {program}: {error}"),
            })?;

        Ok(CommandOutcome {
            // -1 for a process killed by a signal: it did not exit, and
            // reporting 0 would read as success to every caller.
            status: output.status.code().unwrap_or(-1),
            stdout: String::from_utf8_lossy(&output.stdout).into_owned(),
            stderr: String::from_utf8_lossy(&output.stderr).into_owned(),
        })
    }
}

impl PhpHost for ProcessPhpHost {
    /// Asks the filesystem whether the version's pool directory is there.
    fn directory_exists(&self, path: &Path) -> bool {
        path.is_dir()
    }

    /// Creates the agent's own socket directory and sets its mode explicitly.
    ///
    /// The mode is set after creation rather than being left to
    /// `create_dir_all`, whose result is `0o777 & !umask` and therefore
    /// depends on how the daemon happened to be started. It is also re-applied
    /// on every call, so a directory that already exists with the wrong mode
    /// is corrected rather than accepted.
    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), PhpOpError> {
        let failed = |error: std::io::Error| PhpOpError::ConfigWrite {
            reason: format!("could not create {}: {error}", path.display()),
        };

        std::fs::create_dir_all(path).map_err(failed)?;
        std::fs::set_permissions(path, std::fs::Permissions::from_mode(mode)).map_err(failed)
    }

    /// Creates the customer's directories in a forked child that has dropped
    /// to the account, and sets the mode of each explicitly.
    ///
    /// The ids are resolved here, at the moment of use, and never cached: an
    /// account deleted and recreated between two operations gets a different
    /// uid, and a cached one would write into whoever now holds it.
    ///
    /// The mode is set inside the child, as the account — a `chmod` performed
    /// by the root parent afterwards would be a second window in which the
    /// directory sits at its umask-derived mode, and it would be root touching
    /// a customer path (rules/security.md). It is re-applied on every call, so
    /// a directory that already exists at a laxer mode is corrected rather
    /// than accepted.
    fn create_directories_as_account(
        &self,
        account: &AccountName,
        directories: &[&Path],
        mode: u32,
    ) -> Result<(), PhpOpError> {
        let ids = AccountIds::resolve(account).map_err(drop_failed)?;

        // The child does the narrowest possible unit of work and exits: it
        // creates directories and nothing else. It must not allocate freely
        // (only the forking thread survives into it), which is why the paths
        // are built by the parent and merely read here.
        fork_as_account(&ids, || {
            for directory in directories {
                std::fs::create_dir_all(directory).map_err(|_| PrivError::WorkFailed)?;
                std::fs::set_permissions(directory, std::fs::Permissions::from_mode(mode))
                    .map_err(|_| PrivError::WorkFailed)?;
            }
            Ok(())
        })
        .map_err(drop_failed)
    }

    /// Delegates to the one implementation of the config-write protocol,
    /// passing itself as the [`ConfigHost`] that runs the validator and the
    /// reload — the same process spawning the rest of this file does.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        Ok(write_config(self, target, contents, validator, reload)?)
    }

    /// Delegates to the removal half of the same protocol.
    fn remove_config(
        &self,
        target: &Path,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        Ok(remove_config(self, target, validator, reload)?)
    }
}

/// Reports a failure to do work as the account.
///
/// Its own function rather than a `From` impl on [`PhpOpError`]: the privilege
/// dropper is used by several areas, and a blanket conversion would let a
/// future call site turn a failed drop into a generic write error without
/// anyone deciding that it should.
fn drop_failed(error: PrivError) -> PhpOpError {
    PhpOpError::ConfigWrite {
        reason: format!("could not create the account's directories: {error}"),
    }
}
