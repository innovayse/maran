//! The seam between the cron operations and the machine they run on.

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::model::cron_run_record::CronRunRecord;

/// Everything the cron operations do to this machine.
///
/// A trait rather than direct calls to `crontab(1)` and `std::fs`, and not for
/// abstraction's sake: installing a table into a host's cron spool and writing
/// files into a real customer's home are precisely the operations a unit test
/// must never actually perform. Behind this seam every decision — what the
/// installed table says, which files an entry owns, what a duplicate is, what
/// happens when an install is refused halfway — is testable, and the one
/// implementation that really touches the machine stays small enough to read in
/// full.
///
/// **The split of privilege runs straight through this trait, and it is the
/// area's central rule.** The two crontab methods run as root, because
/// `crontab(1)` is the correct writer of the spool: where that spool lives,
/// what owns it and how the daemon learns it changed are the program's business
/// on each family. Every other method touches a file inside the account's home,
/// and those run as the account — see [`super::ProcessCronHost`] for what that
/// costs and what it buys.
///
/// **Every method of this trait MUST be called from
/// `tokio::task::spawn_blocking`, never from a runtime worker.** Two of them
/// fork and block in `waitpid` for as long as `fork_as_account`'s two-minute
/// ceiling allows; the rest wait on a spawned program or read a file. Any of
/// them on a runtime worker stalls every other in-flight command
/// (rules/rust.md "Async and blocking"). The obligation is restated on each
/// method below, because a caller reads the method they are calling.
pub trait CronHost: Send + Sync {
    /// Reads the account's current crontab.
    ///
    /// `Ok(None)` for an account that has no crontab at all, which is not a
    /// failure: it is what every account looks like before the panel installs
    /// its first entry, and treating it as one would make listing an untouched
    /// account an error rather than an empty list.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: they
    /// spawn a program and wait for it, which on a runtime worker stalls every
    /// other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] when the program refuses for any
    /// other reason, or cannot be run at all.
    fn read_crontab(&self, account: &AccountName) -> Result<Option<String>, CronError>;

    /// Replaces the account's crontab with `contents`, whole.
    ///
    /// There is no partial install: the table replaces what was there or it does
    /// not, which is what lets every operation in this area render the WHOLE
    /// document and hand it over, instead of editing lines in place on a file
    /// two writers could be holding.
    ///
    /// Implementations MUST NOT write the table anywhere an account can reach.
    /// A root process writing a temporary file into a directory a customer owns
    /// is a symlink the customer plants once and root follows forever;
    /// `AgentPaths::agent_scratch_dir` is root-owned and `0700` for exactly this
    /// call.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: they
    /// spawn a program and wait for it, which on a runtime worker stalls every
    /// other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::CrontabRefused`] when the program refuses the table
    /// or cannot be given one at all.
    fn install_crontab(&self, account: &AccountName, contents: &str) -> Result<(), CronError>;

    /// Mints an id for a new entry.
    ///
    /// Behind the seam and not a call in the operation, for the reason every
    /// other method here is behind it: it reads the host's randomness source,
    /// and an operation that minted its own id would be untestable — every
    /// assertion about which file an entry owns would have to be written
    /// against a value that changes on every run.
    ///
    /// The id is the agent's own value and never a field of a request. It names
    /// three files under the account's home, and [`CronEntryId`]'s grammar is
    /// the only thing standing between an id and a path — which is why the
    /// answer is a validated type rather than a `String`.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::EntryIdUnavailable`] when the host's randomness
    /// source cannot be read, or answers something that is not a usable id.
    fn new_entry_id(&self) -> Result<CronEntryId, CronError>;

    /// Writes `command` to the entry's command file, creating the account's
    /// cron directory if it is not there.
    ///
    /// The file holds the command VERBATIM plus one newline
    /// ([`CronEntry::file_contents`](crate::cron::model::cron_entry::CronEntry::file_contents)),
    /// because the installed crontab line names this file and carries no byte
    /// of the command. Everything cron would otherwise have rewritten — a `%`
    /// turned into a newline, a `#` starting a comment — is ordinary shell text
    /// once it is here.
    ///
    /// Implementations MUST do this as the account (rules/rust.md
    /// "Privileges"). Overwriting is correct: an entry has one command, and an
    /// update replaces it.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: the
    /// underlying `fork_as_account` forks and blocks in `waitpid`, which on a
    /// runtime worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::EntryFileUnwritable`] when the directory or the file
    /// cannot be written, and [`CronError::Privilege`] when the account cannot
    /// be resolved or the privilege drop fails.
    fn write_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        command: &CronCommand,
    ) -> Result<(), CronError>;

    /// Reads an entry's command file back, verbatim.
    ///
    /// **The read side of the whole design.** The crontab carries no commands,
    /// so this is how a listing knows what an entry runs and how a creation
    /// knows whether the account already has that entry. Without it the central
    /// mechanism would be write-only.
    ///
    /// `Ok(None)` for a file that is not there — an entry whose command file
    /// was removed out from under it, which is a state to report rather than an
    /// error to raise.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: they
    /// resolve an account and read a file, which on a runtime worker stalls
    /// every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::EntryFileUnreadable`] when something IS there and
    /// cannot be read as the entry's own file, and [`CronError::Privilege`] when
    /// the account cannot be resolved.
    fn read_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<String>, CronError>;

    /// Removes every file the entry owns: its command, its log and its exit
    /// status.
    ///
    /// Idempotent, file by file: a file that is not there is success, because a
    /// deletion retried after a lost response must converge rather than fail on
    /// its own previous work.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: the
    /// underlying `fork_as_account` forks and blocks in `waitpid`, which on a
    /// runtime worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::EntryFileUnremovable`] when a file is there and
    /// cannot be removed, and [`CronError::Privilege`] when the account cannot
    /// be resolved or the privilege drop fails.
    fn remove_entry_files(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<(), CronError>;

    /// Reads what the entry's last run reported: its exit status and when it
    /// finished.
    ///
    /// Both come from the one exit file — its content and its modification
    /// time. `Ok(None)` when there is no such file, which is what an entry that
    /// has never run looks like.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`, as
    /// above.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::EntryFileUnreadable`] and
    /// [`CronError::Privilege`] as [`Self::read_command_file`] does.
    fn read_run_record(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<CronRunRecord>, CronError>;

    /// Reads at most `max_bytes` from the END of the entry's output file.
    ///
    /// From the end, and bounded, because the file is written by a command the
    /// customer chose: an entry that prints a megabyte a minute would otherwise
    /// put its whole output into the root daemon's memory every time somebody
    /// opened the panel. Decoded lossily — the bytes are whatever the command
    /// wrote, and a program that emits one invalid sequence must not make its
    /// own output unreadable.
    ///
    /// `Ok(None)` when there is no output file: the entry has never run. An
    /// empty string is the different answer — it ran and said nothing.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`, as
    /// above.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::EntryFileUnreadable`] and
    /// [`CronError::Privilege`] as [`Self::read_command_file`] does.
    fn read_output_tail(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        max_bytes: usize,
    ) -> Result<Option<String>, CronError>;
}
