//! The command a cron entry runs, checked before it is written to its own file.

use super::cron_command_error::CronCommandError;

/// The most bytes a command may be.
///
/// A `.cmd` file holds ONE command line, not a script: a customer who needs a
/// script writes one and schedules a call to it. Four kilobytes is far more
/// than any such line needs and is small enough that a thousand entries cost
/// nothing to read back.
const MAX_LENGTH: usize = 4096;

/// A validated cron command — one shell command line, stored verbatim.
///
/// The inner string is private and the only constructor is
/// [`CronCommand::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// This value is written to a per-entry file under the account's home and run
/// by `/bin/sh <file>` from a crontab line that contains no byte of it. That
/// placement is the whole security design and it is why the alphabet here is a
/// short list of refusals rather than a permitted character set: the characters
/// a crontab line cannot carry — `%`, which cron rewrites into a newline, and
/// `#`, which starts a comment — are ordinary shell text in a file, and
/// refusing them would refuse working commands for a danger that no longer
/// exists. [`CronCommandError`] carries the full reasoning, including the two
/// commands that disproved the earlier in-line design on a real host.
///
/// What the type still guarantees is that the value is one line: no control
/// characters, no surrounding whitespace, and a bounded length.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct CronCommand(String);

impl CronCommand {
    /// Validates `candidate` as one cron command line and wraps it.
    ///
    /// Accepts 1 to 4096 bytes of UTF-8 — the parameter is a `&str`, so
    /// well-formed UTF-8 is the caller's problem and already solved — with no
    /// control character anywhere and no leading or trailing whitespace.
    /// Everything else is allowed, `%` and `#` included.
    ///
    /// # Errors
    ///
    /// - [`CronCommandError::Empty`] when `candidate` is empty.
    /// - [`CronCommandError::TooLong`] when it exceeds 4096 bytes.
    /// - [`CronCommandError::ControlCharacter`] for a newline, a carriage
    ///   return, a tab, a NUL or any other control character.
    /// - [`CronCommandError::SurroundingWhitespace`] when it begins or ends
    ///   with whitespace.
    pub fn parse(candidate: &str) -> Result<Self, CronCommandError> {
        if candidate.is_empty() {
            return Err(CronCommandError::Empty);
        }

        if candidate.len() > MAX_LENGTH {
            return Err(CronCommandError::TooLong {
                maximum: MAX_LENGTH,
            });
        }

        // Before the whitespace check, so that a newline is reported as the
        // control character it is rather than as a stray blank at an end.
        if let Some(character) = candidate.chars().find(|c| c.is_control()) {
            return Err(CronCommandError::ControlCharacter { character });
        }

        if candidate.starts_with(char::is_whitespace) || candidate.ends_with(char::is_whitespace) {
            return Err(CronCommandError::SurroundingWhitespace);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated command, exactly as it was written.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/cron_command_tests.rs"]
mod tests;
