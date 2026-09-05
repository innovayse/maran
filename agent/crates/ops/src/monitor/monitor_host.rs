//! The seam between the monitoring operations and the machine they read.

use std::path::Path;

use maran_agent_core::command_outcome::CommandOutcome;

use crate::monitor::model::filesystem_usage::FilesystemUsage;
use crate::monitor::monitor_error::MonitorError;

/// The readings the monitoring area needs from the operating system.
///
/// A trait rather than direct calls to `std::fs` and `std::process::Command`,
/// and for a reason this area feels more sharply than the others: every number
/// here is whatever the machine running the tests happens to be doing. A test
/// that read the real `/proc` would assert against the load of the build
/// server, and a test that ran the real `systemctl` would report on whatever
/// units that host has. Behind this seam the numbers are chosen by the test and
/// the parsing is what is under examination.
///
/// Every method is READ-ONLY. Nothing in this area changes the machine, and the
/// trait is shaped so that nothing in it could: there is no write, no unit
/// start, no unit stop. The one method that spawns a program is
/// [`Self::run`], and its only caller asks the service manager to `show` —
/// a subcommand that reports and does not act.
///
/// Implementations MUST spawn with an argv array against an absolute path taken
/// from the `DistroAdapter`, never through a shell and never through a program
/// name resolved by `PATH` (rules/security.md item 3).
pub trait MonitorHost: Send + Sync {
    /// The kernel's processor time accounting, verbatim.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when the kernel's
    /// statistics cannot be read at all.
    fn read_cpu_times(&self) -> Result<String, MonitorError>;

    /// Waits between the two processor readings a utilisation figure needs.
    ///
    /// Processor time is a counter, so a percentage only exists between two
    /// readings and something has to pass between them. The wait is behind this
    /// seam rather than in the operation for two reasons: a unit test must not
    /// really wait, and the length of the wait is a property of the machine
    /// this runs on rather than of the arithmetic.
    fn pause_between_cpu_samples(&self);

    /// The kernel's memory accounting, verbatim.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when it cannot be
    /// read.
    fn read_memory(&self) -> Result<String, MonitorError>;

    /// The kernel's load-average line, verbatim.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when it cannot be
    /// read.
    fn read_load_average(&self) -> Result<String, MonitorError>;

    /// The kernel's per-interface byte counters, verbatim.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::HostStatisticsUnavailable`] when they cannot be
    /// read.
    fn read_network_counters(&self) -> Result<String, MonitorError>;

    /// How full the filesystem holding `path` is.
    ///
    /// Asked as a filesystem question rather than by walking the tree: a walk
    /// of a whole server's disk on every dashboard refresh is a denial of
    /// service against the panel, and it would answer a different question —
    /// the sum of the files it could see, rather than the space that decides
    /// when writes start failing.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::FilesystemUnavailable`] when the filesystem
    /// cannot be queried.
    fn filesystem_usage(&self, path: &Path) -> Result<FilesystemUsage, MonitorError>;

    /// Runs `program` with `arguments` and waits for it.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::ServiceManagerUnavailable`] with a `code` of
    /// `-1` when the program cannot be started at all. A non-zero exit is NOT
    /// an error here — it is returned in the outcome, because what a status
    /// means is the caller's business and not this seam's.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, MonitorError>;

    /// The text of the host's local password database.
    ///
    /// `path` is what the `DistroAdapter` answers for it, passed in rather than
    /// known here for the same reason `run` is given an absolute program path:
    /// `ops` names no platform location of its own.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::AccountsUnavailable`] when the file cannot be
    /// read.
    fn read_password_database(&self, path: &str) -> Result<String, MonitorError>;

    /// Bytes the tree at `path` occupies.
    ///
    /// Infallible by design: a path that is not there, or that cannot be read,
    /// measures zero. The number is shown to a person as "space used", and
    /// refusing to report an entire host's accounts because one directory could
    /// not be walked is worse than being low by that directory.
    fn directory_size(&self, path: &Path) -> u64;
}
