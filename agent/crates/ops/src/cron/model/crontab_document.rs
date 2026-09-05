//! A whole crontab, split into what is ours and what we found there.

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::env_var_name::EnvVarName;
use maran_agent_core::validation::system::env_var_value::EnvVarValue;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::installed_line::{DISABLED_PREFIX, installed_line};
use crate::cron::model::cron_entry::CronEntry;
use crate::cron::model::cron_environment::CronEnvironment;

/// The line that opens the region this agent owns.
///
/// Everything below it is rewritten on every install, and everything above it
/// is carried across untouched. It is compared for exact equality rather than
/// matched by a prefix: a marker that a near-miss also satisfies is a marker
/// that starts absorbing a customer's own comments.
const BANNER: &str = "# maran: managed section - every line below is rewritten by the panel";

/// What precedes an entry's id on its marker line.
const MARKER_PREFIX: &str = "# maran-entry: ";

/// The mail policy the agent writes for every account, above its own entries.
///
/// Empty, always. Output already goes to the entry's own log file, so anything
/// cron mailed would be a second copy sent through the host's mail transport —
/// an outbound relay a customer would be able to aim by writing to standard
/// output. The two `"` are what cron reads as "empty": it strips a matching
/// pair of quotes, so this sets the variable rather than setting it to two
/// characters.
const MAILTO_LINE: &str = "MAILTO=\"\"";

/// The name of the interpreter assignment the agent writes for every account.
const SHELL_NAME: &str = "SHELL";

/// How many fields a cron schedule has.
const SCHEDULE_FIELDS: usize = 5;

/// One account's crontab, parsed into the part this agent owns and the part it
/// found.
///
/// # What "parse" means here, and why it never fails
///
/// [`Self::parse`] is infallible by construction. A crontab is not this
/// agent's file — an account with shell access can edit it, a host's packaging
/// can seed it, and an administrator can add a line by hand at any time. A
/// parser that could refuse would turn any of those into an account whose cron
/// entries the panel can no longer list, enable, or delete. So there is no
/// refusal: a line this type does not recognise is a line it carries across
/// unchanged.
///
/// # The layout it renders, and why it is that order
///
/// 1. **The foreign region**, byte for byte and in its original order.
///    Position matters as much as bytes do: a cron environment assignment
///    applies to the lines BELOW it, so moving a foreign `PATH=` would change
///    which foreign entries it governs. Nothing is sorted, deduplicated or
///    normalised here.
/// 2. **The banner**, which is where this agent's region starts.
/// 3. **`MAILTO=""` and `SHELL=<the platform's sh>`**, always, whatever the
///    account asked for. They sit BELOW every foreign line on purpose:
///    whatever a hand-edited preamble set, ours re-sets it for the region
///    beneath, so a managed entry runs under the interpreter this agent chose
///    and mails nowhere regardless of what came before it.
/// 4. **The customer's own environment assignments**, in the order the panel
///    gave them.
/// 5. **The managed entries**, each a marker line naming its id followed by the
///    line cron actually reads.
///
/// # What is dropped
///
/// A line below the banner that is neither a managed block nor a valid
/// environment assignment is dropped rather than preserved. The banner says the
/// region is rewritten, and the alternative is worse than losing a hand-added
/// line: carrying it into the foreign region would move it ABOVE our
/// environment block, which for an assignment silently changes what it governs.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CrontabDocument {
    /// Every line found above the banner that is not part of a managed block,
    /// verbatim and in order.
    foreign: Vec<String>,
    /// The environment assignments the panel owns.
    environment: Vec<CronEnvironment>,
    /// The entries the panel owns, in the order they are rendered.
    entries: Vec<CronEntry>,
}

impl CrontabDocument {
    /// Reads `text` as a crontab. Never fails; see the note on the type.
    ///
    /// A managed block is recognised anywhere in the file, not only below the
    /// banner: the marker line carries the id, so it identifies itself, and an
    /// account whose banner was deleted by hand would otherwise have every one
    /// of its entries orphaned into foreign text the panel could no longer
    /// touch. Recognising the marker is what makes that recoverable.
    ///
    /// A marker is only believed when the line after it really parses as a
    /// schedule. A hand-written marker followed by prose is two ordinary lines,
    /// and they are carried across as such — the safe direction, because
    /// adopting them would mean rewriting a line whose meaning we never
    /// established.
    ///
    /// What follows a believed schedule on its line is read and discarded. The
    /// render rebuilds it from the id, so a line whose tail was tampered with
    /// is repaired by the next install rather than preserved.
    #[must_use]
    pub fn parse(text: &str) -> Self {
        let lines = split_lines(text);
        let mut document = Self {
            foreign: Vec::new(),
            environment: Vec::new(),
            entries: Vec::new(),
        };
        let mut in_managed_region = false;
        let mut index = 0;

        while let Some(line) = lines.get(index) {
            index += 1;

            if recognised(line) == BANNER {
                in_managed_region = true;
                continue;
            }

            if let Some(entry) = read_entry(&lines, index - 1) {
                document.entries.push(entry);
                // The marker line and the line it names.
                index += 1;
                continue;
            }

            if in_managed_region {
                if let Some(assignment) = read_environment(recognised(line)) {
                    document.environment.push(assignment);
                }
                continue;
            }

            document.foreign.push((*line).to_owned());
        }

        document
    }

    /// Renders the whole crontab for `account`, with `sh_binary` as the
    /// interpreter every managed entry runs under.
    ///
    /// `sh_binary` is the `DistroAdapter`'s answer, passed in rather than known
    /// here: `ops` writes no platform path of its own (rules/rust.md "Distro
    /// adapter"). It is used twice — once for the `SHELL` assignment and once
    /// in every entry line — from this one parameter, so the interpreter cron
    /// is told about and the interpreter the line names can never be two
    /// different programs.
    ///
    /// The result always ends in a newline, including when the text it was
    /// parsed from did not. A crontab is a line-oriented file and its last line
    /// needs a terminator; that is the one byte of the foreign region this
    /// method may add, and it adds nothing else.
    #[must_use]
    pub fn render(&self, account: &AccountName, sh_binary: &str) -> String {
        let mut lines: Vec<String> = self.foreign.clone();

        lines.push(BANNER.to_owned());
        lines.push(MAILTO_LINE.to_owned());
        lines.push(format!("{SHELL_NAME}={sh_binary}"));

        for assignment in &self.environment {
            lines.push(format!(
                "{}={}",
                assignment.name.as_str(),
                assignment.value.as_str()
            ));
        }

        for entry in &self.entries {
            lines.push(format!("{MARKER_PREFIX}{}", entry.id.as_str()));
            lines.push(installed_line(entry, account, sh_binary));
        }

        let mut text = lines.join("\n");
        text.push('\n');
        text
    }

    /// The managed entries, in the order they are rendered.
    #[must_use]
    pub fn entries(&self) -> &[CronEntry] {
        &self.entries
    }

    /// The customer's environment assignments, in the order they are rendered.
    #[must_use]
    pub fn environment(&self) -> &[CronEnvironment] {
        &self.environment
    }

    /// Replaces every customer environment assignment with `environment`.
    ///
    /// Wholesale rather than merged: the panel holds the list and sends the one
    /// it wants, so a merge here would make removing an assignment impossible
    /// to express.
    pub fn set_environment(&mut self, environment: Vec<CronEnvironment>) {
        self.environment = environment;
    }

    /// Adds `entry` after every entry already there.
    ///
    /// Appended and never sorted, so an account's entries keep the order they
    /// were created in — the order the panel lists them in, and the order cron
    /// reads them in.
    pub fn append(&mut self, entry: CronEntry) {
        self.entries.push(entry);
    }

    /// The entry with `id`, if this account has one.
    #[must_use]
    pub fn entry(&self, id: &CronEntryId) -> Option<&CronEntry> {
        self.entries.iter().find(|entry| entry.id == *id)
    }

    /// Sets whether the entry with `id` may run, reporting whether it is there.
    ///
    /// `false` for an id this account does not own, which is what the operation
    /// above turns into [`CronError::NotFound`](crate::cron::CronError::NotFound).
    ///
    /// **EVERY block carrying that id is changed, not the first.** A crontab is
    /// not this agent's file: an account with shell access can read its own id
    /// out of `crontab -l` and paste a second copy of the block. Changing only
    /// the first would leave the panel reporting an entry disabled while a copy
    /// of it kept running every five minutes — the panel asserting a state the
    /// host does not have, on exactly the operation an operator reaches for
    /// when a customer's job is misbehaving. [`Self::remove`] has always taken
    /// every match; the other two now agree with it.
    pub fn set_enabled(&mut self, id: &CronEntryId, enabled: bool) -> bool {
        let mut found = false;
        for entry in self.entries.iter_mut().filter(|entry| entry.id == *id) {
            entry.enabled = enabled;
            found = true;
        }

        found
    }

    /// Sets the schedule of the entry with `id`, reporting whether it is there.
    ///
    /// Every block carrying that id is changed, for the reason
    /// [`Self::set_enabled`] gives: a duplicate left at the old schedule is a
    /// job the panel has stopped describing correctly.
    pub fn set_schedule(&mut self, id: &CronEntryId, schedule: &CronSchedule) -> bool {
        let mut found = false;
        for entry in self.entries.iter_mut().filter(|entry| entry.id == *id) {
            entry.schedule = schedule.clone();
            found = true;
        }

        found
    }

    /// Removes every entry with `id`, reporting whether there was one.
    ///
    /// Every match, for the reason [`Self::set_enabled`] gives — this method is
    /// the one the other two were made to agree with.
    pub fn remove(&mut self, id: &CronEntryId) -> bool {
        let before = self.entries.len();
        self.entries.retain(|entry| entry.id != *id);

        self.entries.len() != before
    }
}

/// The part of `line` this agent's own markers are recognised in.
///
/// One trailing carriage return, dropped — and only for RECOGNITION. A crontab
/// written on a machine with `\r\n` endings is a real thing an administrator
/// arrives with, and without this every `# maran-entry: <id>\r` fails
/// [`CronEntryId::parse`], the banner fails its comparison, and the whole
/// managed region lands in the foreign text: the panel then lists no entries
/// for an account whose entries are still firing, `delete` answers "not found",
/// and the next install writes a SECOND banner below the first.
///
/// The foreign region is unaffected, and deliberately so. Foreign lines are
/// pushed with their own bytes, carriage return included, so the law that a
/// line this agent did not write comes back byte for byte still holds exactly.
/// What this changes is only which lines this agent recognises as its own.
fn recognised(line: &str) -> &str {
    line.strip_suffix('\r').unwrap_or(line)
}

/// Splits `text` into lines without inventing a final empty one.
///
/// `str::split('\n')` yields an empty trailing element for text that ends in a
/// newline, and rendering that back would grow the file by one blank line on
/// every install. Every other empty line is kept: a blank line in a customer's
/// crontab is part of the foreign region and comes back where it was.
fn split_lines(text: &str) -> Vec<&str> {
    let mut lines: Vec<&str> = text.split('\n').collect();
    if lines.last().is_some_and(|last| last.is_empty()) {
        lines.pop();
    }

    lines
}

/// Reads the managed block that starts at `index`, if one does.
///
/// Two lines are consumed by a block: the marker naming the id, and the line
/// cron reads. Either half failing to parse means this is not a block, and the
/// caller carries the lines across as ordinary text.
fn read_entry(lines: &[&str], index: usize) -> Option<CronEntry> {
    let marker = recognised(lines.get(index)?);
    let id = CronEntryId::parse(marker.strip_prefix(MARKER_PREFIX)?).ok()?;
    let (schedule, enabled) = read_schedule(recognised(lines.get(index + 1)?))?;

    Some(CronEntry {
        id,
        schedule,
        enabled,
        // The crontab carries no command. See [`CronEntry`].
        command: None,
    })
}

/// Reads a managed entry's own line: its schedule, and whether cron can see it.
///
/// The five fields are re-validated through [`CronSchedule::parse`] rather than
/// taken as text, so a schedule that came back off a disk cannot be rendered
/// again unless it is one this agent would have accepted in the first place
/// (rules/rust.md "Validation first"). Everything after the fifth field is what
/// this agent wrote and is rebuilt by the render, so it is not read.
fn read_schedule(line: &str) -> Option<(CronSchedule, bool)> {
    let (text, enabled) = match line.strip_prefix(DISABLED_PREFIX) {
        Some(rest) => (rest, false),
        None => (line, true),
    };

    let mut fields = text.split_whitespace();
    let mut schedule = [""; SCHEDULE_FIELDS];
    for field in &mut schedule {
        *field = fields.next()?;
    }
    let [minute, hour, day_of_month, month, day_of_week] = schedule;

    let schedule = CronSchedule::parse(minute, hour, day_of_month, month, day_of_week).ok()?;

    Some((schedule, enabled))
}

/// Reads a `NAME=VALUE` line as a customer assignment, if it is one.
///
/// Both halves go through their validators, which is what keeps the agent's own
/// two lines out of the customer's list: [`EnvVarName`] refuses `MAILTO` and
/// `SHELL`, so the pair this agent writes on every install parses as nothing,
/// is dropped with the rest of the unrecognised region, and is written afresh
/// by the next render. One assignment in, one assignment out — never two.
fn read_environment(line: &str) -> Option<CronEnvironment> {
    let (name, value) = line.split_once('=')?;

    Some(CronEnvironment {
        name: EnvVarName::parse(name).ok()?,
        value: EnvVarValue::parse(value).ok()?,
    })
}

#[cfg(test)]
#[path = "../../tests/cron/crontab_document_tests.rs"]
mod tests;
