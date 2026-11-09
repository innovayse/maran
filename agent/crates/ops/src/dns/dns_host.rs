//! The seam between the DNS operations and the machine they run on.

use std::path::Path;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::dns::dns_error::DnsError;
use crate::safe_write::model::{Reload, Validator};

/// The operating-system operations the DNS area needs.
///
/// A trait rather than direct calls to `std::process::Command`, `std::net` and
/// `std::fs`: starting a root daemon and connecting to port 53 are exactly the
/// operations a unit test must never really perform. Behind this seam every
/// decision — what a status reading means, when a wait gives up, whether a
/// failed start is rolled back — stays in the operations, where a test can drive
/// it.
///
/// Implementations MUST spawn with an argv array against an absolute path taken
/// from the `DistroAdapter`, never through a shell and never through a program
/// name resolved by `PATH` (rules/security.md item 3).
pub trait DnsHost: Send + Sync {
    /// Runs `program` with `arguments` as an argv array and waits for it, for at
    /// most the deadline of `class`.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::SpawnFailed`] with a negative code when the program
    /// cannot be started at all, and [`DnsError::Bound`] when it outran the
    /// deadline of `class` or its output ceiling. A non-zero exit is NOT an error
    /// here: `is-active` answers with its status, while a refused `restart` is a
    /// failure the caller decides on.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, DnsError>;

    /// Reads the live file at `target`, or answers `None` when it is not there.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ConfigUnreadable`] when the file exists and cannot be
    /// read. An unreadable file is never flattened into "absent": that would let
    /// an I/O failure authorise a rewrite of a live daemon's configuration.
    fn read_config(&self, target: &Path) -> Result<Option<String>, DnsError>;

    /// Writes `contents` to `target` through the config-write protocol:
    /// temporary file beside the target, `fsync`, atomic rename, `validator`,
    /// `reload`, and a restoration of the previous content if either refuses
    /// (rules/rust.md "Config writes"). The one implementation delegates to
    /// `crate::safe_write::write_config` and adds nothing of its own.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ConfigRejected`] when the validator exited non-zero,
    /// [`DnsError::ServiceRefused`] when the reload did,
    /// [`DnsError::SpawnFailed`] when either could not be started,
    /// [`DnsError::Bound`] when either outran its deadline, and
    /// [`DnsError::ConfigWrite`] for the protocol's own mechanical failures. The
    /// previous content has been restored in every case.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), DnsError>;

    /// Removes the file at `path`. Idempotent: a file that is not there is
    /// success, because a rollback retried after a lost response must converge.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ConfigWrite`] when the file is there and cannot be
    /// removed.
    fn remove_file(&self, path: &Path) -> Result<(), DnsError>;

    /// Reports whether a TCP connection to `port` on the loopback address is
    /// accepted within a bounded timeout. A refused connection, a timeout and a
    /// host with no listener are the same answer: nothing is serving.
    fn tcp_port_accepts(&self, port: u16) -> bool;

    /// Sleeps the calling thread for `duration`.
    ///
    /// A seam and not a `thread::sleep` in the operation, so a test drives the
    /// wait for a daemon to bind as virtual time instead of really sleeping
    /// (rules/testing.md "Determinism").
    fn pause(&self, duration: Duration);

    /// Time elapsed since an arbitrary, fixed origin on a monotonic clock. Only
    /// differences between two readings mean anything; a deadline is checked
    /// against this and not against a count of pauses, because a probe can itself
    /// take seconds.
    fn monotonic_now(&self) -> Duration;
}
