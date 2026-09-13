//! The name half of a cron environment assignment.

use super::env_var_name_error::EnvVarNameError;

/// The most characters a name may be.
///
/// Long enough for every conventional name and short enough that a crontab
/// preamble stays readable. A bound exists at all because the value is written
/// into a root-installed file.
///
/// Visible to the crate, not private, because the sibling
/// [`super::env_var_value::EnvVarValue`] derives ITS ceiling from this one: the
/// two are written to one line as `<name>=<value>`, and cron reads that line
/// into a fixed buffer. Two constants chosen independently would let the pair
/// grow past what cron reads while each looked reasonable on its own.
pub(crate) const MAX_LENGTH: usize = 64;

/// Names the agent writes itself and no customer may set.
///
/// Kept as a private list rather than as two comparisons so that adding a third
/// reserved name is one edit in one place, and so a test can read the list
/// instead of restating it. [`EnvVarNameError::ReservedName`] carries the
/// reason each one is here.
const RESERVED_NAMES: [&str; 2] = ["MAILTO", "SHELL"];

/// A validated cron environment variable name.
///
/// The inner string is private and the only constructor is
/// [`EnvVarName::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// Unlike the command, this value really does end up on a line of the crontab —
/// `KEY=value`, in the preamble cron reads before any entry — so the alphabet
/// is a permitted set rather than a list of refusals, and it is the shell's
/// own: an uppercase letter or an underscore, then uppercase letters, digits
/// and underscores.
///
/// On top of the grammar sits a denylist of two names the agent writes itself.
/// A customer who could set `MAILTO` would have an outbound mail relay, and one
/// who could set `SHELL` would choose the interpreter every entry runs under —
/// including entries created before they changed it.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct EnvVarName(String);

impl EnvVarName {
    /// Validates `candidate` as an environment variable name and wraps it.
    ///
    /// Accepts an uppercase ASCII letter or an underscore followed by uppercase
    /// ASCII letters, digits or underscores, up to 64 characters, and refuses
    /// the reserved names.
    ///
    /// Written as explicit character checks rather than a regex, like every
    /// other validator here: the rule is short enough to read directly, and
    /// this way the crate needs neither a regex dependency nor a lazily
    /// compiled pattern.
    ///
    /// # Errors
    ///
    /// - [`EnvVarNameError::Empty`] when `candidate` is empty.
    /// - [`EnvVarNameError::TooLong`] when it exceeds 64 characters.
    /// - [`EnvVarNameError::IllegalCharacter`] for anything outside `A-Z`,
    ///   `0-9` and `_` — lowercase included.
    /// - [`EnvVarNameError::LeadingDigit`] when the first character is a digit.
    /// - [`EnvVarNameError::ReservedName`] for `MAILTO` and `SHELL`.
    pub fn parse(candidate: &str) -> Result<Self, EnvVarNameError> {
        if candidate.is_empty() {
            return Err(EnvVarNameError::Empty);
        }

        if candidate.len() > MAX_LENGTH {
            return Err(EnvVarNameError::TooLong {
                maximum: MAX_LENGTH,
            });
        }

        let illegal = candidate
            .chars()
            .find(|c| !(c.is_ascii_uppercase() || c.is_ascii_digit() || *c == '_'));
        if let Some(character) = illegal {
            return Err(EnvVarNameError::IllegalCharacter { character });
        }

        // The alphabet check above already refused everything but `A-Z`, `0-9`
        // and `_`, so a first character that is not a letter or an underscore
        // is a digit.
        if candidate.starts_with(|c: char| c.is_ascii_digit()) {
            return Err(EnvVarNameError::LeadingDigit);
        }

        if RESERVED_NAMES.contains(&candidate) {
            return Err(EnvVarNameError::ReservedName {
                name: candidate.to_owned(),
            });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated name, as it is written into the crontab.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/env_var_name_tests.rs"]
mod tests;
