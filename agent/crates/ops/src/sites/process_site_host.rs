//! The [`SiteHost`] that actually touches this machine.

use std::os::unix::fs::DirBuilderExt as _;
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::validation::fs::path::resolve_in_home;
use maran_agent_core::validation::system::name::AccountName;

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
    ///
    /// The spawn itself is [`spawn_argv`], shared with every other host that
    /// runs an argv array: the locale pin, the closed standard input and the
    /// signal-killed `-1` are settled once there rather than five times here.
    ///
    /// # Errors
    ///
    /// Returns [`SafeWriteError::SpawnFailed`] when the program cannot be
    /// started — `nginx` not installed, not executable — carrying the
    /// operating system's reason. A program that started and exited non-zero
    /// is not an error: its status comes back in the outcome, and whether that
    /// means a validator refused or a reload refused is the protocol's
    /// decision, not this host's.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        spawn_argv(program, arguments).map_err(|error| SafeWriteError::SpawnFailed {
            program: program.to_owned(),
            reason: error.to_string(),
        })
    }
}

/// The extension every vhost the web server includes carries.
///
/// Named rather than written into the filter, because it is the same fact
/// `SitePaths::for_site` spells when it builds a vhost's name: the directory is
/// enumerated for exactly the files this agent writes into it.
const CONFIG_EXTENSION: &str = "conf";

/// The mode every level of a site's log directory is created with.
///
/// `0750` and owned by root, matching `/var/log/maran/sites` itself. Group
/// `root`, so the PANEL uid — which can traverse `/var/log/maran` because it is
/// in the `maran` group — stops here: it matches `other`, which is `---`.
/// Nothing but root ever opens a path beneath this, and the one reader that
/// does (`follow_log`, on behalf of `TailSiteLog`) is the root daemon itself.
const SITE_LOG_DIRECTORY_MODE: u32 = 0o750;

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

    /// Lists the `.conf` files in the agent's own nginx include directory.
    ///
    /// Read as root and not through `fork_as_account`: this directory is
    /// `/etc/maran/nginx/sites`, which the agent owns outright and no customer
    /// can write into, so there is no untrusted path to be led down. Only
    /// regular-file entries ending in `.conf` are returned, which is what the
    /// web server itself includes.
    fn list_config_paths(&self) -> Result<Vec<PathBuf>, SitesOpError> {
        let directory = Path::new(AgentPaths::NGINX_INCLUDE_DIRECTORY);
        let entries = std::fs::read_dir(directory).map_err(|_| SitesOpError::ConfigUnreadable {
            path: directory.display().to_string(),
        })?;

        Ok(entries
            .filter_map(Result::ok)
            .map(|entry| entry.path())
            .filter(|path| {
                path.extension()
                    .is_some_and(|extension| extension == CONFIG_EXTENSION)
            })
            .collect())
    }

    /// Creates `/var/log/maran/sites/<account>` as root, `0750`, if it is not
    /// already there.
    ///
    /// `DirBuilder` with an explicit `mode`, not `create_dir_all`: the mode of
    /// a directory the root daemon creates must be the mode this code chose,
    /// not whatever the daemon's inherited umask happens to be. `recursive`
    /// covers a host where `/var/log/maran/sites` itself is missing — a manual
    /// upgrade that skipped the installer step — and every level it creates
    /// gets the same explicit mode.
    ///
    /// `AlreadyExists` is success. Every operation that renders a vhost calls
    /// this, so the common case is a directory that has been there since the
    /// account's first site.
    ///
    /// There is no ownership check here and none is needed for containment:
    /// `/var/log/maran` is `root:maran 0750` and `/var/log/maran/sites` is
    /// `root:root 0750`, both created and asserted by the installer, so no uid
    /// but root can put anything at this name in the first place. The installer
    /// is where that claim is checked, once, rather than on every site
    /// operation.
    fn create_site_log_directory(&self, account: &AccountName) -> Result<(), SitesOpError> {
        let directory = AgentPaths::account_site_log_dir(account);

        match std::fs::DirBuilder::new()
            .recursive(true)
            .mode(SITE_LOG_DIRECTORY_MODE)
            .create(&directory)
        {
            Ok(()) => Ok(()),
            Err(error) => Err(SitesOpError::LogDirectory {
                reason: format!("{}: {error}", directory.display()),
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

#[cfg(test)]
#[path = "../tests/sites/process_site_host_tests.rs"]
mod tests;
