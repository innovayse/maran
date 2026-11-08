//! Why a candidate PHP extension name was refused.

use thiserror::Error;

/// Reasons [`super::php_extension_name::PhpExtensionName::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum PhpExtensionNameError {
    /// The candidate was empty.
    #[error("a PHP extension name cannot be empty")]
    Empty,

    /// A control character — a newline, a carriage return, a NUL — was found.
    ///
    /// Refused by name and before the catalogue lookup, rather than as a
    /// consequence of it, because the name ends up in an `argv` and in the
    /// instance of a root-run install unit: a refusal that only happens
    /// implicitly is one a later loosening of the lookup silently removes
    /// (rules/security.md §4).
    #[error("a PHP extension name cannot contain a control character")]
    ControlCharacter,

    /// The candidate is not one of the catalogue's names.
    ///
    /// The catalogue is closed: the agent never takes a package name or a
    /// module name from a caller, only one of the names it lists itself.
    #[error("`{candidate}` is not a PHP extension this panel manages")]
    NotInCatalogue {
        /// What was offered.
        candidate: String,
    },
}
