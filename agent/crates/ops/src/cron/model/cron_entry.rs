//! One scheduled entry the panel manages, and the format of its command file.

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;

/// One entry this agent installed into an account's crontab.
///
/// The three fields the crontab itself carries — the id, the schedule and
/// whether the line is commented out — plus the one it deliberately does not.
///
/// **`command` is an `Option` because the crontab does not hold commands.**
/// That is the whole design of this area, not a gap in this type: the
/// customer's command is written verbatim to `~/.maran/cron/<id>.cmd` and the
/// installed line only NAMES that file, so an entry parsed out of a crontab
/// knows its schedule and knows nothing about what it runs until the file is
/// read back. A listing fills the field in; the parser leaves it `None`; and an
/// entry whose file has been removed out from under it stays `None`, which is
/// the honest answer rather than an empty command that reads like a working
/// one.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CronEntry {
    /// The agent-minted id that names this entry's three files.
    pub id: CronEntryId,
    /// When cron runs it.
    pub schedule: CronSchedule,
    /// Whether cron can run it at all.
    ///
    /// A disabled entry keeps every one of its files and keeps its line in the
    /// crontab; the line is prefixed so that cron reads it as a comment. That
    /// is what makes re-enabling it give back the same entry rather than a new
    /// one with the same text.
    pub enabled: bool,
    /// What it runs, read back from its own command file.
    ///
    /// `None` when the file has not been read, or when there is no file to
    /// read. See the note on the type.
    pub command: Option<String>,
}

impl CronEntry {
    /// The exact bytes an entry's `.cmd` file holds for `command`.
    ///
    /// The command verbatim plus one trailing newline, and nothing else — no
    /// shebang, no `set -e`, no wrapper. `/bin/sh <file>` runs a file of shell
    /// text, so the file IS the command; anything this function added would be
    /// a byte the customer did not write running under their account.
    ///
    /// The newline is there because a text file ends with one, and because a
    /// shell reading a final line with no terminator is a difference between
    /// implementations that nobody should have to know about.
    ///
    /// Paired with [`Self::command_from_file`] in this one place so that the
    /// side that writes the file and the side that reads it back cannot drift:
    /// a listing compares what it read against what a caller wants to create,
    /// and one extra newline on either side would turn every duplicate check
    /// into a silent miss.
    #[must_use]
    pub fn file_contents(command: &CronCommand) -> String {
        format!("{}\n", command.as_str())
    }

    /// The command that `contents` — a `.cmd` file's bytes — holds.
    ///
    /// Strips exactly ONE trailing newline, the one
    /// [`Self::file_contents`] added, and leaves everything else alone. Not
    /// `trim_end`: a command the customer wrote cannot end in whitespace
    /// ([`CronCommand`] refuses it), but a file a customer edited by hand can,
    /// and trimming would report a command that is not the one in the file.
    #[must_use]
    pub fn command_from_file(contents: &str) -> String {
        contents.strip_suffix('\n').unwrap_or(contents).to_owned()
    }
}

#[cfg(test)]
#[path = "../../tests/cron/cron_entry_tests.rs"]
mod tests;
