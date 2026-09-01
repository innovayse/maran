//! The [`SiteHost`] that actually touches this machine.

use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::path::resolve_in_home;

use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{CommandOutcome, ConfigHost, SafeWriteError, remove_config, write_config};
use crate::sites::follow_log::follow_log;
use crate::sites::log_sink::LogSink;
use crate::sites::model::log_tail_request::LogTailRequest;
use crate::sites::model::tail_end::TailEnd;
use crate::sites::{SiteHost, SiteMaintenanceHost, SitesOpError};

/// Runs the real `nginx -t`, the real `systemctl reload`, and the real
/// directory creation inside a customer's home.
///
/// The only implementation that touches the machine, and deliberately the
/// smallest piece of the area: every decision worth reviewing lives in the
/// operations, where it is tested against a fake. What is left here is
/// spawning, reading a file, and forking to the account.
pub struct ProcessSiteHost;

impl ProcessSiteHost {
    /// Creates the host.
    #[must_use]
    pub fn new() -> Self {
        Self
    }
}

impl Default for ProcessSiteHost {
    fn default() -> Self {
        Self::new()
    }
}

impl ConfigHost for ProcessSiteHost {
    /// Spawns `program` with `arguments` as an argv array.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse. `program` comes from the `DistroAdapter`'s
    /// allow-list and never from a request.
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

impl SiteHost for ProcessSiteHost {
    /// Reads the vhost, distinguishing "absent" from "unreadable".
    fn read_config(&self, path: &Path) -> Result<Option<String>, SitesOpError> {
        match std::fs::read_to_string(path) {
            Ok(contents) => Ok(Some(contents)),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(_) => Err(SitesOpError::ConfigUnreadable {
                path: path.display().to_string(),
            }),
        }
    }

    /// Creates the directories in a forked child that has dropped to the
    /// account.
    ///
    /// The ids are resolved here, at the moment of use, and never cached: an
    /// account deleted and recreated between two operations gets a different
    /// uid, and a cached one would write into whoever now holds it.
    fn create_directories_as_account(
        &self,
        account: &AccountName,
        directories: &[&Path],
    ) -> Result<(), SitesOpError> {
        let ids = AccountIds::resolve(account)?;

        // The child does the narrowest possible unit of work and exits: it
        // creates directories and nothing else. It must not allocate freely
        // (only the forking thread survives into it), which is why the paths
        // are built by the parent and merely read here.
        fork_as_account(&ids, || {
            for directory in directories {
                std::fs::create_dir_all(directory).map_err(|_| PrivError::WorkFailed)?;
            }
            Ok(())
        })?;

        Ok(())
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
    ) -> Result<(), SitesOpError> {
        Ok(write_config(self, target, contents, validator, reload)?)
    }

    /// Delegates to the removal half of the same protocol.
    fn remove_config(
        &self,
        target: &Path,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        Ok(remove_config(self, target, validator, reload)?)
    }

    /// Delegates to `agent-core`'s one containment primitive.
    ///
    /// The check lives in `agent-core` so that "is this path inside the
    /// account's home?" has exactly one answer in the workspace rather than
    /// one per call site (rules/security.md: defense in depth).
    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, SitesOpError> {
        Ok(resolve_in_home(account, relative)?)
    }
}

impl SiteMaintenanceHost for ProcessSiteHost {
    /// Runs the validator, then the reload, and writes nothing.
    fn validate_and_reload(
        &self,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        let checked = ConfigHost::run(self, validator.program, validator.arguments)?;
        if checked.status != 0 {
            return Err(SitesOpError::NginxValidation {
                stderr: checked.stderr,
            });
        }

        let reloaded = ConfigHost::run(self, reload.program, reload.arguments)?;
        if reloaded.status != 0 {
            return Err(SitesOpError::ReloadFailed {
                stderr: reloaded.stderr,
            });
        }

        Ok(())
    }

    /// Delegates to the module that does the reading.
    ///
    /// Every protection the tail needs — the pinned directory descriptor, the
    /// `fstat` that refuses a FIFO or a hardlink, the byte budgets, the idle
    /// ceiling — lives in `follow_log`, which is where a reviewer of a root-side
    /// read of a customer file should be looking. This is the seam and nothing
    /// else.
    fn tail_log(
        &self,
        request: &LogTailRequest,
        sink: &mut dyn LogSink,
    ) -> Result<TailEnd, SitesOpError> {
        follow_log(request, sink)
    }
}
