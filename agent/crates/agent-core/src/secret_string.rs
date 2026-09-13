//! A string that prints itself as `«redacted»`, wherever it is printed from.

use core::fmt;

/// What a secret prints instead of itself, in every formatter.
///
/// One constant rather than two literals, so `Debug` and `Display` cannot drift
/// into disagreeing — and so a test can state the redaction without restating
/// the word.
const REDACTION: &str = "«redacted»";

/// A credential the agent carries and hands on, and never writes down.
///
/// The wrapped string is private and the only reader is
/// [`SecretString::expose`].
///
/// The leak this type exists to prevent is not the deliberate one. Nobody
/// writes `tracing::info!("{key:?}")` about an access key on purpose; what
/// happens is that a REQUEST STRUCT holding the key is `#[derive(Debug)]`, and
/// something logs the struct. The derived implementation prints each field
/// through that field's own `Debug` — so the field's `Debug` is the only place
/// where the outcome can be decided, and here it prints `«redacted»`.
/// [`fmt::Display`] does the same, because `{credentials}` and `{credentials:?}`
/// are one keystroke apart and only one of them being safe is not a property
/// anybody can rely on.
///
/// There is deliberately no `serde` implementation and no `Deref`: a secret
/// that can be reached by coercion is one whose readers cannot be enumerated.
///
/// **`expose` is the only way out, and it is named to be greppable.** One word,
/// so a review answers "where does this credential leave its wrapper?" with a
/// single search rather than with a reading of the module. That is the whole
/// reason it is not called `as_str`, and it is why no second reader may be
/// added — a convenience accessor would make the grep incomplete without making
/// anything else true.
///
/// This type **hides**; it does not validate. A value that will be interpolated
/// into something a root process runs — a MySQL `IDENTIFIED BY`, a `chpasswd`
/// line — must be a validated
/// [`crate::validation::secrets::password::Password`] instead, because hiding a
/// value from a log says nothing about what it does when it is interpolated.
/// `SecretString` is for a credential the agent receives per call, uses to
/// authenticate to something, and forgets: an object store's access key and
/// secret key.
#[derive(Clone, PartialEq, Eq)]
pub struct SecretString(String);

impl SecretString {
    /// Wraps `value`. Nothing is validated — see the type's note on hiding
    /// versus validating.
    #[must_use]
    pub fn new(value: String) -> Self {
        Self(value)
    }

    /// The wrapped value.
    ///
    /// The only reader, and named `expose` rather than `as_str` so that every
    /// place a secret leaves its wrapper is visible in a diff and findable with
    /// one grep.
    #[must_use]
    pub fn expose(&self) -> &str {
        &self.0
    }
}

impl fmt::Debug for SecretString {
    /// Writes the redaction and never the value — including when a derived
    /// `Debug` on a struct that holds this calls it.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(REDACTION)
    }
}

impl fmt::Display for SecretString {
    /// Writes the redaction and never the value.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(REDACTION)
    }
}

#[cfg(test)]
#[path = "tests/secret_string_tests.rs"]
mod tests;
