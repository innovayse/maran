//! Why a cron command was refused.

/// Reasons [`super::cron_command::CronCommand::parse`] refuses a candidate.
///
/// The list is short, and it is short for a reason worth stating here rather
/// than in the plan that decided it, because the obvious reading of a short
/// list is that something was forgotten.
///
/// The customer's command never appears in the crontab. It is written verbatim
/// to a per-entry file under the account's home, and the installed crontab line
/// is agent constants plus an agent-minted entry id — `<schedule> /bin/sh
/// <id>.cmd > <id>.log 2>&1; echo $? > <id>.exit`. So the two characters a
/// crontab line genuinely cannot carry are legal here:
///
/// - `%` is legal. Cron rewrites the first unescaped `%` on a crontab line into
///   a newline and feeds the rest to the command on stdin. That rewrite happens
///   to the LINE, and the command is not on the line. An earlier design put it
///   there and was disproved on a real host: its own `date +%s` suffix broke.
/// - `#` is legal. A `#` starts a comment in a crontab and it does not in a
///   shell script, and the same earlier design broke on `echo hi # comment`,
///   which parses standalone and not inside the `( … )` wrapper it used.
///
/// What stays refused is what a FILE cannot carry either: control characters
/// and an unbounded length. A `.cmd` holds one command line — the writer
/// appends exactly one trailing newline — so a newline inside the value would
/// make "one entry, one command" false, and rules/security.md §4 refuses
/// newlines and control characters in any value written into a config file
/// regardless of which file it is this month.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CronCommandError {
    /// The candidate was empty.
    ///
    /// An entry whose command is nothing is an entry that exists to do nothing,
    /// and cron would still wake up for it every time the schedule fires.
    #[error("a cron command cannot be empty")]
    Empty,

    /// The candidate was longer than one command line has any business being.
    #[error("a cron command cannot exceed {maximum} bytes")]
    TooLong {
        /// The ceiling that was exceeded.
        maximum: usize,
    },

    /// A control character — a newline, a carriage return, a tab, a NUL — was
    /// found.
    ///
    /// Tab included, deliberately: `char::is_control` is the whole class, and
    /// carving an exception for the one control character that looks harmless
    /// is how the class stops being a class. The value is one command line and
    /// a tab is not part of one.
    #[error("a cron command cannot contain `{character:?}`")]
    ControlCharacter {
        /// The first offending character.
        character: char,
    },

    /// The candidate began or ended with whitespace.
    ///
    /// Refused rather than trimmed: the command is stored verbatim and compared
    /// verbatim when a duplicate entry is detected, so ` ls` and `ls ` must not
    /// be two spellings of one command. Trimming silently would also hide from
    /// the operator that what they see is not what they typed.
    #[error("a cron command cannot begin or end with whitespace")]
    SurroundingWhitespace,
}
