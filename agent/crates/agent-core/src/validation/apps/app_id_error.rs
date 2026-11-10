//! Why a candidate app id was refused.

use thiserror::Error;

/// Reasons [`super::validated_app_id::ValidatedAppId::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum AppIdError {
    /// The candidate was empty.
    #[error("an app id cannot be empty")]
    Empty,

    /// The candidate was longer than any id of the catalogue.
    #[error("an app id cannot exceed {max} characters")]
    TooLong {
        /// The longest id the catalogue holds.
        max: usize,
    },

    /// A control character — a newline, a carriage return, a NUL — was found.
    ///
    /// Refused by name and before anything else: the id becomes part of a package
    /// name and a systemd unit instance, and a refusal that only happens as a
    /// consequence of the catalogue lookup is one a later loosening of it silently
    /// removes (rules/security.md item 4).
    #[error("an app id cannot contain a control character")]
    ControlCharacter,

    /// The candidate is not an app of the closed catalogue.
    ///
    /// Closed on purpose: what the agent installs is not the caller's choice, so a
    /// spelling the catalogue does not list never becomes a package name.
    #[error("`{candidate}` is not an app of the catalogue")]
    Unknown {
        /// What was offered.
        candidate: String,
    },
}
