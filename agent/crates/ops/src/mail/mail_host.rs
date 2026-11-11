//! The seam between the mail operations and the machine they run on.

use std::path::Path;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::AdapterBinary;

use crate::mail::mail_error::MailError;
use crate::mail::model::listening_socket::ListeningSocket;
use crate::mail::model::path_facts::PathFacts;
use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};
use crate::ssl::CertificateState;

/// The operating-system operations the mail lifecycle needs.
///
/// A trait rather than direct calls to `std::process::Command` and `std::fs`:
/// starting root daemons and reading the kernel's socket table are exactly what a
/// unit test must never really do. Behind this seam every decision — which
/// daemons are required, in what order they start, what a refusal rolls back —
/// stays in the operations, where a test can drive it; the one implementation
/// that touches the machine stays small enough to read in full.
///
/// Implementations MUST spawn with an argv array against an absolute path taken
/// from the `DistroAdapter`, never through a shell and never through a name
/// resolved by `PATH` (rules/security.md item 3).
pub trait MailHost: Send + Sync {
    /// Runs `program` with `arguments` as an argv array and waits for it, for at
    /// most the deadline of `class`.
    ///
    /// A non-zero exit is NOT an error here: `is-active` answers a question with
    /// its status, while a refused `unmask` is a failure, and each caller reads a
    /// status differently.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::SpawnFailed`] with a negative code when the program
    /// cannot be started, and [`MailError::Bound`] when it outran its deadline or
    /// its output ceiling.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, MailError>;

    /// Reads a live file, or answers `None` when it is not there.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::ConfigUnreadable`] when the file exists and cannot be
    /// read. An unreadable file is never flattened into "absent", because that
    /// would let an I/O failure authorise a rewrite of a live daemon's
    /// configuration.
    fn read_config(&self, target: &Path) -> Result<Option<String>, MailError>;

    /// Reads the owner and mode of `path` without following a final symlink, or
    /// answers `None` when nothing is there.
    ///
    /// Asked only by security invariants (is `postdrop` still setgid root).
    ///
    /// # Errors
    ///
    /// Returns [`MailError::ConfigUnreadable`] when the path exists and cannot be
    /// inspected, never flattened into "absent".
    fn path_facts(&self, path: &Path) -> Result<Option<PathFacts>, MailError>;

    /// Writes `files` as ONE change through the config-write protocol: every file
    /// renamed into place, then `validator`, then `reload`, with every file
    /// restored if either refuses (rules/rust.md "Config writes"). The one
    /// implementation delegates to `crate::safe_write::write_config_set`.
    ///
    /// `daemon` and `unit` say whose files these are, so a refusal is reported
    /// with a name.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::ConfigRejected`] when the validator exited non-zero,
    /// [`MailError::ServiceRefused`] when the reload did,
    /// [`MailError::SpawnFailed`] when either could not be started,
    /// [`MailError::Bound`] when either outran its deadline, and
    /// [`MailError::ConfigWrite`] for the protocol's mechanical failures.
    fn write_config_set(
        &self,
        daemon: crate::mail::model::mail_daemon::MailDaemon,
        unit: &str,
        files: &[ConfigFile<'_>],
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), MailError>;

    /// Writes ONE file through the config-write protocol with a validator and no
    /// reload, for a file its readers re-open on every use (a lookup table, a
    /// passwd-file, a selector map). Delegates to
    /// `crate::safe_write::write_unreloaded_config`.
    ///
    /// # Errors
    ///
    /// As [`Self::write_config_set`], without the reload errors.
    fn write_file_unreloaded(
        &self,
        daemon: crate::mail::model::mail_daemon::MailDaemon,
        unit: &str,
        file: &ConfigFile<'_>,
        validator: &Validator<'_>,
    ) -> Result<(), MailError>;

    /// Reports whether anything exists at `path`.
    ///
    /// Asked of the programs and configuration files that say a package is
    /// installed. It answers `false` for a path that cannot be inspected as well
    /// as for one that is not there: the only use is "is this installed", and the
    /// refusal that follows names the daemon.
    fn path_exists(&self, path: &Path) -> bool;

    /// Lists every TCP socket the kernel reports as listening, IPv4 and IPv6.
    ///
    /// Read from the kernel's own table and not from a configuration file: a
    /// status that reported what was rendered would agree with the panel whatever
    /// the host did.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::SocketsUnreadable`] when the IPv4 table cannot be
    /// read. A host without an IPv6 table is an empty IPv6 half and not an error.
    fn listening_sockets(&self) -> Result<Vec<ListeningSocket>, MailError>;

    /// Answers what certificate material is installed for `domain`.
    ///
    /// Delegates to `ops::ssl::certificate_state`, which reads three file names
    /// and parses nothing.
    ///
    /// # Errors
    ///
    /// Returns [`MailError::ConfigUnreadable`] when a file of the material exists
    /// and cannot be read.
    fn certificate_state(&self, domain: &Domain) -> Result<CertificateState, MailError>;

    /// Sleeps the calling thread for `duration`; a seam so a test drives the wait
    /// for ports to bind as virtual time (rules/testing.md "Determinism").
    fn pause(&self, duration: Duration);

    /// Time elapsed since an arbitrary fixed origin on a monotonic clock; only
    /// differences mean anything.
    fn monotonic_now(&self) -> Duration;
}
