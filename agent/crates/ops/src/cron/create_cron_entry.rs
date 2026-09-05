//! CreateCronEntry: the command into a file, then a line that only names it.

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_entry::CronEntry;
use crate::cron::model::crontab_document::CrontabDocument;

/// Adds one scheduled entry to `account`'s crontab.
///
/// # The order, and what each step protects
///
/// 1. **Read and parse the crontab.** An absent one parses as an empty
///    document, so an account's first entry needs no special case.
/// 2. **Refuse a duplicate, before anything is written.** Two entries with the
///    same schedule and the same command are one entry the customer can see and
///    one they cannot explain, and a retry after a lost response would create
///    exactly that. The comparison reads each candidate's command back from its
///    own `.cmd` file, because the crontab does not carry commands — the
///    schedule narrows the candidates first, so an account with fifty entries
///    at fifty different times reads no files at all.
/// 3. **Mint the id and write the command file.** The customer's command goes
///    into that file verbatim and never anywhere else.
/// 4. **Render the whole document and install it.** Not an append to the file
///    on disk: the render rebuilds every managed line from validated values, so
///    a line somebody tampered with is repaired rather than carried forward.
/// 5. **On a refused install, take the command file away again.** The entry is
///    not in the crontab, so a file left behind is litter inside the customer's
///    home that nothing will ever run and nothing will ever clean up.
///
/// # What reaches the crontab
///
/// Nothing the caller wrote, except the five schedule fields — and those are a
/// [`CronSchedule`], which cannot hold a space, a control character or a `%`.
/// The command is in a file; the line names the file. See
/// [`CrontabDocument`] for the render and for the two designs a real host
/// disproved before this one.
///
/// # Errors
///
/// - [`CronError::AlreadyExists`] when the account already has an entry with
///   this schedule and command, enabled or not. Nothing is written.
/// - [`CronError::EntryIdUnavailable`] when no id could be minted.
/// - [`CronError::EntryFileUnwritable`] when the command file could not be
///   written. Nothing is installed.
/// - [`CronError::CrontabRefused`] when `crontab` refused the table. The
///   command file is removed and the live crontab is what it was.
/// - [`CronError::EntryFileUnreadable`] when an existing entry's command file
///   is there and could not be read, which is checked before anything is
///   written.
/// - [`CronError::Privilege`] when the account cannot be resolved or the
///   privilege drop for the home-side write fails.
pub fn create_cron_entry(
    host: &dyn CronHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    schedule: &CronSchedule,
    command: &CronCommand,
) -> Result<CronEntryId, CronError> {
    let existing = host.read_crontab(account)?.unwrap_or_default();
    let mut document = CrontabDocument::parse(&existing);

    if is_duplicate(host, account, &document, schedule, command)? {
        return Err(CronError::AlreadyExists);
    }

    let id = host.new_entry_id()?;
    host.write_command_file(account, &id, command)?;

    document.append(CronEntry {
        id: id.clone(),
        schedule: schedule.clone(),
        enabled: true,
        command: None,
    });

    if let Err(refusal) =
        host.install_crontab(account, &document.render(account, distro.sh_binary()))
    {
        // Best effort, and its failure is deliberately not reported: the
        // operation already failed for a reason worth reporting, and replacing
        // that reason with "cleanup failed" would hide it.
        let _ = host.remove_entry_files(account, &id);

        return Err(refusal);
    }

    Ok(id)
}

/// Reports whether `account` already has an entry with this schedule and
/// command.
///
/// The schedule is compared first because it is already in memory, and only an
/// entry that matches it has its command file read. That is not only cheaper —
/// it is what keeps a listing-sized amount of privileged file reading out of
/// the common case where a customer adds one more entry at a new time.
///
/// # Errors
///
/// Returns [`CronError::EntryFileUnreadable`] when a candidate's file is there
/// and cannot be read, and [`CronError::Privilege`] when the account cannot be
/// resolved. An entry whose file is simply absent is not a duplicate: there is
/// no command there to be the same as this one.
fn is_duplicate(
    host: &dyn CronHost,
    account: &AccountName,
    document: &CrontabDocument,
    schedule: &CronSchedule,
    command: &CronCommand,
) -> Result<bool, CronError> {
    for entry in document.entries() {
        if entry.schedule != *schedule {
            continue;
        }

        let Some(contents) = host.read_command_file(account, &entry.id)? else {
            continue;
        };

        if CronEntry::command_from_file(&contents) == command.as_str() {
            return Ok(true);
        }
    }

    Ok(false)
}

#[cfg(test)]
#[path = "../tests/cron/create_cron_entry_tests.rs"]
mod tests;
