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

    /// The text of the OpenSSH server's main configuration file.
    ///
    /// `path` is [`maran_distro::DistroAdapter::sshd_config_path`], passed in
    /// for the same reason `read_password_database` takes a path rather than
    /// knowing one: this seam names no platform location of its own.
    ///
    /// **What reading this file can prove, and what it cannot.** The only
    /// question this area asks of it is whether the installer's own
    /// `Match Group` block — delimited by its own marker comments — is still
    /// present in THIS file, between them, verbatim. That answers "was this
    /// exact block removed or hand-edited", which is the drift the panel has
    /// no way to see today. It does NOT prove that the block is what sshd
    /// will actually enforce for a real login, and it does NOT resolve
    /// `Include` at all — this method reads exactly the one file
    /// `sshd_config_path` names and nothing it pulls in. Two consequences of
    /// that, in the two directions that matter:
    ///
    /// - If the block were ever relocated into an `Include`d drop-in instead
    ///   of the main file — not what `installer/lib/86-sftp.sh` does today,
    ///   which appends directly to the main file, but a hand edit could move
    ///   it there — this check would not find it and would report drift on a
    ///   host that may still be jailing logins correctly. A FALSE POSITIVE,
    ///   and the safe direction: it pages an operator to go look rather than
    ///   staying quiet.
    /// - An `Include`d drop-in can still change what governs a connection
    ///   without touching this file at all: both families load
    ///   `Include sshd_config.d/*.conf` from the TOP of `sshd_config`, and an
    ///   OpenSSH `Match` block that is not closed by another `Match` line
    ///   before its own file ends keeps applying to whatever text follows it
    ///   after the `Include` splices back in — which could be this file's
    ///   own appended block. This check reads only the block's own literal
    ///   text and would report it Intact in that case, because the text IS
    ///   unchanged; what actually governs a connection could differ. This is
    ///   the FALSE NEGATIVE direction, and it is the accepted gap named in
    ///   this feature's threat note — closing it means re-implementing
    ///   OpenSSH's own `Include` resolution and `Match` precedence, which was
    ///   judged out of proportion to the README's actual defect (a REMOVED
    ///   block, not a shadowed one).
    ///
    /// Asking sshd itself for its effective configuration (`sshd -T -C
    /// user=…`) was tried first and rejected — it evaluates a `Match Group`
    /// condition only for a connection naming a REAL system user who is
    /// already a member of the group, so the check would need either an
    /// existing SFTP login (none may exist on a freshly installed host with
    /// no accounts yet) or a login created solely to ask the question, which
    /// would be a permanent, unrequested change to the host's account
    /// inventory for a monitoring side effect — worse than the drift it
    /// detects. This was measured, not assumed: `sshd -T` without `-C` was
    /// run against a real `sshd` in a disposable container with the
    /// installer's exact block appended, and it printed the file's GLOBAL
    /// defaults only — zero occurrences of `match`, `chrootdirectory` or
    /// `forcecommand` — while `sshd -T -C user=<member>,host=…,addr=…,
    /// laddr=…,lport=22` for a real member of the group correctly showed the
    /// block's directives in effect. `-C` is therefore not a way out of
    /// needing a real group member.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::SshdConfigUnavailable`] when the file cannot be
    /// read.
    fn read_sshd_config(&self, path: &str) -> Result<String, MonitorError>;

    /// Reads `/proc/mounts`, verbatim — the kernel's own live list of mounted
    /// filesystems and the options each was mounted with.
    ///
    /// Used only by [`crate::monitor::get_quota_enforceability`] to classify
    /// whether the filesystem holding hosting accounts' homes can enforce a
    /// disk quota; nothing in this crate mutates a mount.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::MountsUnavailable`] when it cannot be read.
    fn read_mounts(&self) -> Result<String, MonitorError>;

    /// Bytes the tree at `path` occupies.
    ///
    /// Infallible by design: a path that is not there, or that cannot be read,
    /// measures zero. The number is shown to a person as "space used", and
    /// refusing to report an entire host's accounts because one directory could
    /// not be walked is worse than being low by that directory.
    fn directory_size(&self, path: &Path) -> u64;

    /// Reads `/etc/machine-id`, verbatim and unvalidated.
    ///
    /// Returns `Ok(None)` when the file does not exist — a legitimate host
    /// state (a container with no systemd), not a failure to observe. See
    /// [`crate::monitor::model::machine_identity::MachineIdentity::from_raw`]
    /// for how this raw reading becomes the typed answer, including how an
    /// empty file is folded into "not available" rather than reported as an
    /// empty machine-id.
    ///
    /// **Never logged.** The value this returns identifies one physical or
    /// virtual machine for as long as it exists (rules/security.md item 8);
    /// see [`crate::monitor::model::machine_identity::MachineIdentity`]'s doc
    /// comment for exactly what may be recorded about it.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::MachineIdUnavailable`] when the file exists
    /// but could not be read — never for the file simply being absent.
    fn read_machine_id(&self, path: &str) -> Result<Option<String>, MonitorError>;

    /// Reads the kernel's IPv4 routing table (`/proc/net/route`), verbatim.
    ///
    /// Used only by
    /// [`crate::monitor::model::primary_interface::PrimaryInterface::from_ipv4_routes`]
    /// to find the interface carrying the lowest-metric IPv4 default route —
    /// see that type's doc comment for the definition of "primary interface"
    /// this crate uses and for what makes the answer unstable.
    ///
    /// # Errors
    ///
    /// Returns [`MonitorError::Ipv4RoutesUnavailable`] when it cannot be
    /// read.
    fn read_ipv4_routes(&self) -> Result<String, MonitorError>;
}
