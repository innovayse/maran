//! Why an account name was refused.

/// Rejection reasons for [`super::name::AccountName::parse`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum NameError {
    /// The candidate does not match the allowed pattern.
    ///
    /// Deliberately one variant carrying no detail: naming which rule was broken
    /// describes the shape of the rules to an attacker, and every rejection has
    /// the same remedy — send a name that matches the documented pattern.
    #[error("invalid account name")]
    Invalid,
}
