//! A string that does not print itself.

use core::fmt;

/// A value that must never reach a log, wrapped so that printing it is a
/// deliberate act.
///
/// [`fmt::Debug`] prints `<secret>`. There is no [`fmt::Display`] and no `serde`
/// implementation, so the only way to obtain the value is [`Secret::expose`],
/// which is greppable and reviewable. The leak this prevents is not the
/// deliberate `{secret:?}` — it is a request or config struct with
/// `#[derive(Debug)]` reaching a `tracing` macro, because the derived
/// implementation prints each field through the field's own `Debug`, which here
/// prints nothing.
///
/// `Secret` **hides**; [`super::password::Password`] **validates**. They are not
/// interchangeable: a value that will be interpolated into a statement run by
/// root — a MySQL `IDENTIFIED BY`, a `chpasswd` line — must be a `Password`,
/// because hiding a value from the log does nothing about what it does when it
/// is interpolated. Use `Secret` for something the agent only carries and hands
/// on, such as a token it was given.
#[derive(Clone)]
pub struct Secret(String);

impl Secret {
    /// Wraps `value`. Nothing is validated — see the type's note on the
    /// difference from [`super::password::Password`].
    #[must_use]
    pub fn new(value: String) -> Self {
        Self(value)
    }

    /// The wrapped value.
    ///
    /// Named `expose` rather than `as_str` so that every place the secret leaves
    /// its wrapper is visible in a diff and findable with one grep.
    #[must_use]
    pub fn expose(&self) -> &str {
        &self.0
    }
}

impl fmt::Debug for Secret {
    /// Prints `<secret>` and never the value.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str("<secret>")
    }
}

#[cfg(test)]
#[path = "../../tests/validation/secrets/secret_tests.rs"]
mod tests;
