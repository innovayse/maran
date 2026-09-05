//! Why a cron environment variable value was refused.

/// Reasons [`super::env_var_value::EnvVarValue::parse`] refuses a candidate.
///
/// This alphabet is one refusal longer than the command's, and the difference
/// is the point: an environment assignment DOES live on a line of the crontab,
/// where the command does not. Two types that look alike therefore refuse
/// different things, and [`EnvVarValueError::PercentSign`] is where that
/// difference is written down.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum EnvVarValueError {
    /// The candidate was longer than the composed crontab line leaves room for.
    ///
    /// The ceiling is derived from cron's own `MAX_ENVSTR` of 1000 rather than
    /// chosen, because a longer value is not refused by cron — it is TRUNCATED
    /// by it, silently, so the host would run with a shorter `PATH` than the
    /// panel shows and nothing anywhere would say so.
    ///
    /// Note that an EMPTY value is legal and has no variant here: `TZ=` is a
    /// meaningful assignment, and a customer clearing a value they set should
    /// not have to delete the row instead.
    #[error("an environment variable value cannot exceed {maximum} bytes")]
    TooLong {
        /// The ceiling that was exceeded.
        maximum: usize,
    },

    /// The candidate began or ended with whitespace.
    ///
    /// Cron trims it, so ` x ` and `x` set one variable to one value while the
    /// panel would store and display two. Refused rather than trimmed for the
    /// same reason
    /// [`super::cron_command_error::CronCommandError::SurroundingWhitespace`] is:
    /// trimming silently hides from the operator that what they see is not what
    /// they typed.
    #[error("an environment variable value cannot begin or end with whitespace")]
    SurroundingWhitespace,

    /// The candidate was wrapped in a matching pair of quotes.
    ///
    /// Cron strips them, so `"x"` and `x` are one assignment with two
    /// spellings. Refused rather than unquoted, because an operator who wrapped
    /// a value in quotes may have meant the quotes to be part of it — and this
    /// way they say which.
    #[error("an environment variable value cannot be wrapped in `{quote}`, which cron strips")]
    Quoted {
        /// The quote character found at both ends.
        quote: char,
    },

    /// A control character — a newline, a carriage return, a NUL — was found.
    ///
    /// The value is written into a `KEY=value` line of a root-installed
    /// crontab, so a newline here ends the assignment and starts a line of the
    /// caller's choosing — an extra entry, or the `MAILTO` the name denylist
    /// exists to refuse (rules/security.md §4).
    #[error("an environment variable value cannot contain `{character:?}`")]
    ControlCharacter {
        /// The first offending character.
        character: char,
    },

    /// A `%` was found.
    ///
    /// Cron rewrites the first unescaped `%` on a crontab line into a newline
    /// and feeds what follows to the command on stdin. This value lives on such
    /// a line, so a `%` in it is a newline in it, which is the injection above
    /// wearing a character the control-character check cannot see.
    ///
    /// The sibling [`super::cron_command::CronCommand`] permits `%` for exactly
    /// the reason this type refuses it: a command lives in its own file and
    /// never reaches a crontab line at all. The two alphabets differ on purpose,
    /// and this is the difference.
    #[error("an environment variable value cannot contain `%`, which cron rewrites into a newline")]
    PercentSign,
}
