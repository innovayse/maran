//! Why a candidate PHP version was refused.

use thiserror::Error;

/// Reasons [`super::php_version::PhpVersion::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum PhpVersionError {
    /// The candidate was empty.
    #[error("a PHP version cannot be empty")]
    Empty,

    /// A control character — a newline, a carriage return, a NUL — was found.
    ///
    /// Refused by name and before anything else, rather than as a consequence
    /// of the shape check below, because this is the character class the type
    /// exists for: the value is interpolated into `fastcgi_pass unix:…;` and a
    /// newline there ends the directive and starts one of the caller's
    /// choosing, in a config file written by root (rules/security.md §4).
    /// A refusal that only happens implicitly is one a later loosening of the
    /// shape check silently removes.
    #[error("a PHP version cannot contain a control character")]
    ControlCharacter,

    /// The candidate was not two numeric components separated by a dot.
    ///
    /// Deliberately narrow: `8.3`, never `8.3.2`, never `8.3-rc1`, never
    /// `../../etc`. The value names a package, a service unit, a pool
    /// directory and a socket, so anything the agent cannot fully account for
    /// is refused rather than passed on.
    #[error("`{candidate}` is not a two-component PHP version such as `8.3`")]
    Malformed {
        /// What was offered.
        candidate: String,
    },

    /// A component was longer than the two digits any real PHP version uses.
    #[error("a PHP version component cannot exceed two digits")]
    ComponentTooLong,
}
