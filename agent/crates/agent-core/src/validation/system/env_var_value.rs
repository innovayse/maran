//! The value half of a cron environment assignment.

use super::env_var_name;
use super::env_var_value_error::EnvVarValueError;

/// The most bytes cron stores from one line of a crontab.
///
/// Both the vixie-cron and the cronie lineage read an environment line with
/// `get_string(envstr, MAX_ENVSTR, …)`, where `MAX_ENVSTR` is 1000 and
/// `get_string` keeps what fits and discards the rest SILENTLY — no error, no
/// warning, just a shorter value than the one on disk. One byte of the buffer
/// is the terminator, so 999 is what actually survives.
const MAX_CRON_LINE: usize = 999;

/// The most bytes a value may be.
///
/// Derived rather than chosen. The line is `<name>=<value>`, a name is at most
/// [`env_var_name::MAX_LENGTH`] bytes and the `=` is one more, so this is what
/// is left of the line cron will actually read. A larger ceiling would let the
/// panel store and display a `PATH` that the host runs truncated — the worst
/// shape a limit can have, because nothing anywhere reports it.
const MAX_LENGTH: usize = MAX_CRON_LINE - env_var_name::MAX_LENGTH - 1;

/// A validated cron environment variable value.
///
/// The inner string is private and the only constructor is
/// [`EnvVarValue::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// The value is written into a `KEY=value` line of the crontab, which is what
/// makes it stricter than the command it sits above: besides control characters
/// it refuses `%`, because cron rewrites the first unescaped `%` on a line into
/// a newline. That rewrite is the reason the customer's command was moved out
/// of the crontab into a file of its own, and it is the reason this value —
/// which could not be moved, an assignment being the line itself — pays for
/// staying there with one more refused character.
///
/// An empty value is accepted: `TZ=` is a real assignment.
///
/// What is NOT accepted is a value cron would silently alter on its way in.
/// Cron trims whitespace around a value and strips a matching pair of quotes
/// from around it, so `x`, ` x ` and `"x"` all set one variable to one thing —
/// while a panel that stored all three would show three different values and
/// call two of them wrong when comparing. The same "must not be two spellings
/// of one value" argument the sibling
/// [`super::cron_command::CronCommand`] makes about surrounding whitespace.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct EnvVarValue(String);

impl EnvVarValue {
    /// Validates `candidate` as an environment variable value and wraps it.
    ///
    /// Accepts UTF-8 up to the length the composed crontab line leaves, with no
    /// control character, no `%`, no whitespace at either end and no wrapping
    /// quotes. The last two are refused because cron strips them: it would
    /// apply a value that is not the one the panel stores and shows.
    ///
    /// # Errors
    ///
    /// - [`EnvVarValueError::TooLong`] when `candidate` is longer than what the
    ///   `<name>=<value>` line leaves inside cron's own buffer.
    /// - [`EnvVarValueError::ControlCharacter`] for a newline, a carriage
    ///   return or any other control character.
    /// - [`EnvVarValueError::PercentSign`] for a `%`, which cron turns into a
    ///   newline on the line this value is written to.
    /// - [`EnvVarValueError::SurroundingWhitespace`] when the value begins or
    ///   ends with whitespace, which cron trims away.
    /// - [`EnvVarValueError::Quoted`] when the value is wrapped in matching
    ///   single or double quotes, which cron strips.
    pub fn parse(candidate: &str) -> Result<Self, EnvVarValueError> {
        if candidate.len() > MAX_LENGTH {
            return Err(EnvVarValueError::TooLong {
                maximum: MAX_LENGTH,
            });
        }

        if let Some(character) = candidate.chars().find(|c| c.is_control()) {
            return Err(EnvVarValueError::ControlCharacter { character });
        }

        if candidate.contains('%') {
            return Err(EnvVarValueError::PercentSign);
        }

        if candidate.starts_with(char::is_whitespace) || candidate.ends_with(char::is_whitespace) {
            return Err(EnvVarValueError::SurroundingWhitespace);
        }

        // `next_back` is `None` for a one-character value, which is what keeps
        // a lone `"` from reading as a pair of quotes around nothing.
        let mut characters = candidate.chars();
        if let (Some(first), Some(last)) = (characters.next(), characters.next_back())
            && (first == '"' || first == '\'')
            && first == last
        {
            return Err(EnvVarValueError::Quoted { quote: first });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated value, exactly as it was written.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/env_var_value_tests.rs"]
mod tests;
