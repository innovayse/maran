//! Failures of the read-only monitoring operations.

/// The `code` reported when a program could not be started at all.
///
/// Negative so it can never collide with an exit status, every one of which is
/// between 0 and 255 — an operator reading `code: -1` knows the tool never ran
/// rather than looking up status -1 in its manual.
pub(crate) const PROGRAM_UNAVAILABLE: i32 = -1;

/// What can go wrong while READING the host's state.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"), and a short
/// one, because this area changes nothing and therefore has no half-applied
/// state to describe. Every variant here means the same kind of thing: the
/// agent could not find out.
///
/// **A service that is down is not in this list.** It is an answer —
/// `ServiceState::Stopped` — and returning an error for it would invert the
/// purpose of a monitor: the caller would see a failed rpc where it asked for
/// exactly the fact the rpc was refusing to report. Only a failure to REACH the
/// service manager is an error here.
///
/// No variant carries a program's output, for the reason the SFTP area's does
/// not: a shape with no string field cannot leak one.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum MonitorError {
    /// The kernel's own statistics could not be read, or could not be
    /// understood once read.
    ///
    /// Both conditions in one variant on purpose. To every caller they mean
    /// the identical thing — this host's numbers are not available this time
    /// round — and the panel's answer to both is to leave the sample out rather
    /// than to draw a zero, which would be indistinguishable from an idle
    /// machine.
    #[error("the host's kernel statistics could not be read")]
    HostStatisticsUnavailable,

    /// The filesystem the operating system is installed on could not be
    /// measured.
    ///
    /// Its own variant rather than part of [`Self::HostStatisticsUnavailable`]
    /// because it comes from a different place — a filesystem query, not a file
    /// under the kernel's statistics tree — and an operator chasing it looks at
    /// mounts rather than at `/proc`.
    #[error("the root filesystem could not be measured")]
    FilesystemUnavailable,

    /// The service manager could not be asked about a unit.
    ///
    /// Not "the unit is down": this is the tool itself refusing or being
    /// unreachable, which on a host whose service manager is not running is
    /// every unit at once. Reporting it as an error is what keeps the panel
    /// from showing four simultaneous outages when the truth is that nobody
    /// asked.
    #[error("the service manager could not be asked about a unit (status {code})")]
    ServiceManagerUnavailable {
        /// The tool's exit status, or `-1` when it could not be started at all.
        code: i32,
    },

    /// The host's local password database could not be read.
    ///
    /// Without it there is no list of accounts to measure, and reporting an
    /// empty list would read to the panel as "every account was deleted".
    #[error("the password database could not be read")]
    AccountsUnavailable,

    /// The OpenSSH server's main configuration file could not be read.
    ///
    /// Its own variant rather than [`Self::AccountsUnavailable`], even though
    /// both come from a file read: the panel has to tell "no accounts could be
    /// listed" from "the sftp jail's configuration could not be inspected", and
    /// an operator chasing either looks in a different place — the password
    /// database against `/etc/ssh/sshd_config` and whatever replaced it.
    #[error("the sshd configuration could not be read")]
    SshdConfigUnavailable,

    /// `/proc/mounts` could not be read.
    ///
    /// Its own variant rather than [`Self::HostStatisticsUnavailable`] even
    /// though both are a read under `/proc`: an operator chasing a quota
    /// finding looks at mount state, not at processor or memory statistics,
    /// and the two failures have unrelated remedies.
    #[error("the mounted filesystems could not be read")]
    MountsUnavailable,

    /// `/etc/machine-id` exists but could not be read (permission denied, an
    /// I/O error, or similar).
    ///
    /// **Deliberately distinct from the file simply not existing.** A missing
    /// `/etc/machine-id` is a legitimate host state — a container with no
    /// systemd — and is reported as
    /// [`crate::monitor::model::machine_identity::MachineIdentity::NotAvailable`],
    /// not as this error: this variant means the agent could not find out,
    /// the file not existing means the agent found out and the answer is
    /// "there is none". Conflating the two would turn an ordinary container
    /// into an alarm.
    #[error("the host's machine-id could not be read")]
    MachineIdUnavailable,

    /// The kernel's IPv4 routing table (`/proc/net/route`) could not be
    /// read.
    ///
    /// Its own variant rather than [`Self::HostStatisticsUnavailable`], even
    /// though both are `/proc` reads: an operator chasing a fingerprint
    /// finding looks at routing state, not at processor or memory
    /// statistics, and the remedies do not overlap.
    #[error("the host's IPv4 routing table could not be read")]
    Ipv4RoutesUnavailable,
}

impl MonitorError {
    /// The error for a tool that could not be started at all.
    pub(crate) fn program_unavailable() -> Self {
        Self::ServiceManagerUnavailable {
            code: PROGRAM_UNAVAILABLE,
        }
    }
}
