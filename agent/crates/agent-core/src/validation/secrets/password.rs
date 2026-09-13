//! A password whose alphabet makes interpolation into root SQL and into
//! `chpasswd` injection-free.

use core::fmt;

use crate::validation::secrets::password_error::PasswordError;

/// The non-alphanumeric characters a password may contain.
///
/// Chosen by what each of them cannot do rather than by what looks strong.
/// Absent, and absent on purpose: `'` and `"` and `` ` `` (they would close the
/// quoting of `IDENTIFIED BY '<value>'`), `\` (MySQL's string escape, which
/// could re-open it), `:` (the field separator of a `chpasswd` `user:pass`
/// line), a space, and every control character including newline (a newline
/// turns one `chpasswd` line into two, which is how a customer sets a password
/// for a user that is not theirs — rules/security.md §4).
const SAFE_SYMBOLS: &str = "-_.=+";

/// The longest accepted password.
///
/// A ceiling exists because this value is interpolated into a statement sent to
/// a root MySQL session and into a line fed to `chpasswd`; neither should be
/// unbounded on a caller's say-so. 128 is far above anything a generator emits.
const MAXIMUM_LENGTH: usize = 128;

/// A validated password, safe to interpolate into a root-run SQL statement and
/// into a `chpasswd` line.
///
/// This type exists because neither of those two places can bind a parameter.
/// MySQL's `CREATE USER … IDENTIFIED BY '<value>'` is DDL, which takes no
/// placeholders, and `chpasswd` reads `user:pass` lines from stdin. So the value
/// is interpolated — and what makes that safe is that a `Password` **cannot
/// hold** a quote, a backtick, a backslash, a colon, a space or a newline. It is
/// validated, not escaped (rules/rust.md "Validation first").
///
/// Read that before widening `SAFE_SYMBOLS`. The alphabet is the security
/// control; the next reader who sees this value next to a SQL string and decides
/// to "fix" it by accepting more characters and escaping them has removed the
/// control and replaced it with an escaping routine nobody reviewed.
///
/// [`fmt::Debug`] is written by hand and prints `<password>`. There is no
/// [`fmt::Display`] and no `serde` implementation, so the value cannot reach a
/// log line, a tracing field or a serialised message without a deliberate call
/// to [`Password::as_str`].
#[derive(Clone)]
pub struct Password(String);

impl Password {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`PasswordError::Empty`] when `candidate` is empty.
    /// - [`PasswordError::UnexpectedCharacter`] for anything that is neither an
    ///   ASCII letter, an ASCII digit, nor one of `SAFE_SYMBOLS` — which is
    ///   where quotes, backslashes, colons, spaces and newlines are refused.
    /// - [`PasswordError::TooLong`] when `candidate` exceeds 128 bytes.
    pub fn parse(candidate: &str) -> Result<Self, PasswordError> {
        if candidate.is_empty() {
            return Err(PasswordError::Empty);
        }

        if candidate.len() > MAXIMUM_LENGTH {
            return Err(PasswordError::TooLong {
                length: candidate.len(),
            });
        }

        if let Some(character) = candidate
            .chars()
            .find(|c| !(c.is_ascii_alphanumeric() || SAFE_SYMBOLS.contains(*c)))
        {
            return Err(PasswordError::UnexpectedCharacter { character });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The password itself, for the one caller that has to send it to MySQL or
    /// to `chpasswd`.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

impl fmt::Debug for Password {
    /// Prints `<password>` and never the value.
    ///
    /// Hand-written rather than derived because the realistic leak is not a
    /// deliberate `{password:?}` — it is a request struct with
    /// `#[derive(Debug)]` reaching a `tracing` macro, which prints its fields
    /// through exactly this implementation.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str("<password>")
    }
}

#[cfg(test)]
#[path = "../../tests/validation/secrets/password_tests.rs"]
mod tests;
