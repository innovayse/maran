//! Failures of the mail host lifecycle.

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::mail::model::local_submission_hazard::LocalSubmissionHazard;
use crate::mail::model::local_submission_key::LocalSubmissionKey;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::safe_write::SafeWriteError;

/// The status given to a tool that could not be started at all, distinct from any
/// exit status a started one can have.
pub(crate) const PROGRAM_UNAVAILABLE: i32 = -1;

/// Everything the mail lifecycle can fail on.
///
/// Error text is for operators and logs; the panel words what a customer or an
/// administrator reads from the variant (rules/rust.md "Errors").
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum MailError {
    /// No certificate material is installed for the hostname.
    ///
    /// The mail stack never writes into the certificate store: that store is
    /// `ops::ssl`'s, and a daemon that quietly generated a placeholder would put a
    /// certificate every mail client warns about behind a feature whose promise is
    /// forced TLS.
    #[error("no certificate material for {domain} at {expected_path}")]
    CertificateMissing {
        /// The hostname asked about.
        domain: String,
        /// Where the certificate was expected.
        expected_path: String,
    },

    /// A daemon's program is not on the host, so nothing was changed.
    #[error("the {daemon:?} daemon is not installed")]
    NotInstalled {
        /// The daemon that is missing.
        daemon: MailDaemon,
    },

    /// The adapter has no answer for this release (its Dovecot generation or its
    /// key-value server), so nothing was rendered.
    #[error("this release is not one the mail stack knows how to configure")]
    UnsupportedRelease,

    /// A daemon's own check refused the rendered files; they were put back.
    #[error("the {daemon:?} daemon refused the configuration: {output}")]
    ConfigRejected {
        /// The daemon whose check refused.
        daemon: MailDaemon,
        /// What the tool printed, for the operator log.
        output: String,
    },

    /// The service manager refused a unit, or the unit is not active afterwards.
    #[error("the service manager refused {unit}")]
    ServiceRefused {
        /// The unit.
        unit: String,
    },

    /// The units are up and a port that should be bound is not.
    #[error("port {port} is not listening")]
    NotListening {
        /// The first expected port found unbound.
        port: u16,
    },

    /// A tool could not be started or waited for.
    #[error("a mail tool failed with status {code}")]
    SpawnFailed {
        /// A negative code for a tool that never ran, else its exit status.
        code: i32,
    },

    /// A tool outran its deadline or its output ceiling and was killed.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),

    /// The template and its render type have drifted apart.
    #[error("the mail configuration could not be rendered")]
    Render,

    /// A live file exists and cannot be read.
    #[error("a mail configuration file could not be read")]
    ConfigUnreadable,

    /// The config-write protocol failed mechanically.
    #[error("a mail configuration file could not be written: {reason}")]
    ConfigWrite {
        /// The protocol's own message, for the operator log.
        reason: String,
    },

    /// The kernel's table of listening sockets could not be read.
    #[error("the listening sockets could not be read")]
    SocketsUnreadable,

    /// The host is in a state where binding an account's local mail submission
    /// would not hold, so nothing was changed (docs/mail/local-submission.md).
    #[error("local mail submission cannot be bound on this host: {hazard}")]
    UnsafeLocalSubmission {
        /// What is wrong.
        hazard: LocalSubmissionHazard,
    },

    /// A mailbox login or sender domain the change would bind is already bound to
    /// ANOTHER account, so nothing was changed. The key moves only after its
    /// previous owner's push has removed it (docs/mail/local-submission.md,
    /// "One owner per key").
    #[error("the {key} {value} is already bound to another account")]
    LocalSubmissionConflict {
        /// Which kind of key collided.
        key: LocalSubmissionKey,
        /// The key's value.
        value: String,
    },

    /// The server hostname is a valid mail name but not a name the certificate store
    /// files material under, so there is no certificate to look for.
    #[error("the mail hostname cannot name certificate material")]
    HostnameNotUsable,
}

impl From<SpawnError> for MailError {
    /// A bound keeps its meaning; any other failure is a tool that did not run.
    fn from(error: SpawnError) -> Self {
        match error {
            SpawnError::Bound(bound) => Self::Bound(bound),
            _ => Self::program_unavailable(),
        }
    }
}

impl MailError {
    /// The error for a tool that could not be started at all.
    #[must_use]
    pub fn program_unavailable() -> Self {
        Self::SpawnFailed {
            code: PROGRAM_UNAVAILABLE,
        }
    }

    /// Translates the config-write protocol's failure, naming the daemon whose
    /// check or reload was the step that refused.
    #[must_use]
    pub(crate) fn from_config_write(error: SafeWriteError, daemon: MailDaemon, unit: &str) -> Self {
        match error {
            SafeWriteError::ValidationFailed { stderr } => Self::ConfigRejected {
                daemon,
                output: stderr,
            },
            SafeWriteError::ReloadFailed { .. } => Self::ServiceRefused {
                unit: unit.to_owned(),
            },
            SafeWriteError::SpawnFailed { .. } => Self::program_unavailable(),
            SafeWriteError::Bound(bound) => Self::Bound(bound),
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }
}

#[cfg(test)]
#[path = "../tests/mail/mail_error_tests.rs"]
mod tests;
