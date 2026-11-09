//! Failures of the DNS daemon operations.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::dns::model::zone_content_fault::ZoneContentFault;
use crate::safe_write::SafeWriteError;

/// The `code` reported when a program could not be started at all.
///
/// Negative so it can never collide with an exit status, every one of which is
/// between 0 and 255 — an operator reading `SpawnFailed { code: -1 }` knows the
/// tool never ran.
pub(crate) const PROGRAM_UNAVAILABLE: i32 = -1;

/// What can go wrong while enabling, disabling or reading the DNS daemon.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"). The text is
/// for operators and logs; the panel maps the variant to a localized message
/// and never shows it to a hosting customer.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum DnsError {
    /// The daemon's own checker refused the rendered configuration. The previous
    /// configuration was restored. `output` is what the checker printed, which
    /// is operator text and can name a file and a line of OUR rendering, never a
    /// caller's value, since the rendering holds none.
    #[error("the dns configuration was refused by the daemon's checker: {output}")]
    ConfigRejected {
        /// Everything the checker printed while refusing.
        output: String,
    },

    /// The service manager would not bring the unit up, or would not stop it.
    #[error("the service manager refused {unit}")]
    ServiceRefused {
        /// Name of the systemd unit.
        unit: String,
    },

    /// The unit is active and the daemon does not answer its control channel.
    #[error("the dns daemon is not answering on its control channel")]
    NotListening,

    /// A program could not be started at all, or exited with a status the
    /// caller cannot continue past.
    #[error("a dns tool failed with status {code}")]
    SpawnFailed {
        /// The exit status, or a negative sentinel when the tool never ran.
        code: i32,
    },

    /// A deadline or an output ceiling ended a program and its process group was
    /// killed. The work may or may not have finished.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),

    /// The template and its render type have drifted apart.
    #[error("the dns configuration could not be rendered")]
    Render,

    /// The live configuration exists and cannot be read.
    #[error("the dns configuration could not be read")]
    ConfigUnreadable,

    /// The config-write protocol failed mechanically.
    #[error("the dns configuration could not be written: {reason}")]
    ConfigWrite {
        /// What the protocol reported.
        reason: String,
    },

    /// The complete record set broke a rule that concerns a record's neighbours
    /// (a CNAME that shares its name, a repeated record, a ceiling). Nothing was
    /// written.
    #[error(transparent)]
    ZoneContent(#[from] ZoneContentFault),

    /// The daemon's zone checker refused the rendered zone. The previous file was
    /// restored. `output` is what the checker printed, operator text that can
    /// quote a line of a record.
    #[error("the zone was refused by the daemon's zone checker: {output}")]
    ZoneRejected {
        /// Everything the checker printed while refusing.
        output: String,
    },

    /// The daemon's control channel could not be asked, or refused a zone command.
    /// A daemon that is not running is this.
    #[error("the dns daemon refused a zone command: {output}")]
    ControlRefused {
        /// What `rndc` printed.
        output: String,
    },

    /// The daemon answered success but its answer did not state a serial, or it
    /// did not report a zone it had just loaded.
    #[error("the dns daemon's answer about a zone could not be read")]
    ZoneStateUnreadable,

    /// The zone directory exists and cannot be listed.
    #[error("the zone directory could not be read")]
    ZoneDirectoryUnreadable,

    /// The filesystem the zone is written on has less room than the free-space
    /// floor. Nothing was written.
    #[error("{available} bytes available, below the free-space floor of {floor} bytes")]
    ZoneBelowFloor {
        /// Bytes an unprivileged writer could still add.
        available: u64,
        /// The configured floor, in bytes.
        floor: u64,
    },

    /// The filesystem the zone is written on could not be measured, so the write
    /// was not admitted.
    #[error("the filesystem the zone is written on could not be measured")]
    ZoneUnmeasurable,
}

impl From<SpawnError> for DnsError {
    /// A bound keeps its identity; every other spawn failure is "the tool never
    /// ran", with the negative sentinel.
    fn from(error: SpawnError) -> Self {
        match error {
            SpawnError::Bound(bound) => Self::Bound(bound),
            _ => Self::program_unavailable(),
        }
    }
}

impl DnsError {
    /// Maps the config-write protocol's failure onto this area's list.
    ///
    /// The validator slot holds the daemon's checker and the reload slot the
    /// unit restart, so a validation failure is the checker refusing the text
    /// and a reload failure is the service manager refusing the unit — two
    /// different answers for the operator, unlike FTPS where both slots are the
    /// service manager.
    #[must_use]
    pub(crate) fn from_config_write(error: SafeWriteError) -> Self {
        match error {
            SafeWriteError::ValidationFailed { stderr } => Self::ConfigRejected { output: stderr },
            SafeWriteError::ReloadFailed { .. } => Self::ServiceRefused {
                unit: AgentPaths::DNS_UNIT.to_owned(),
            },
            SafeWriteError::SpawnFailed { .. } => Self::program_unavailable(),
            SafeWriteError::Bound(bound) => Self::Bound(bound),
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }

    /// The error for a program that could not be started at all.
    #[must_use]
    pub fn program_unavailable() -> Self {
        Self::SpawnFailed {
            code: PROGRAM_UNAVAILABLE,
        }
    }
}

#[cfg(test)]
#[path = "../tests/dns/dns_error_tests.rs"]
mod tests;
