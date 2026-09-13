//! GetCronEntryOutput: what the last run printed, and what it reported.

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_entry_output::CronEntryOutput;
use crate::cron::model::crontab_document::CrontabDocument;

/// The most output one read returns.
///
/// Sixty-four kibibytes, from the END of the file. The file is written by a
/// command the customer chose and truncated on every run, so its size is theirs
/// to decide: an entry that prints a megabyte a minute would otherwise put its
/// whole last run into the root daemon's memory every time somebody opened the
/// panel. The tail rather than the head because the interesting part of a
/// failed run's output is the error it ended with.
const MAXIMUM_OUTPUT: usize = 64 * 1024;

/// Reads the tail of the entry `id`'s last output, and what that run reported.
///
/// The crontab is read first, and not only for the output's sake: it is what
/// makes an id the account does not own a [`CronError::NotFound`] rather than
/// an empty answer. Without it, asking for any id at all would report "this
/// entry has never run", which reads to an operator exactly like a real entry
/// that has not fired yet.
///
/// An entry that has genuinely never run answers with both halves absent. That
/// is not an error: an entry created a minute ago and scheduled for Sunday has
/// nothing to show, and so has one whose customer deleted the files by hand.
///
/// **What comes back is the account's own report.** The output file and the
/// exit file live inside the account's home and the account can write both, so
/// a customer who wants to can claim any output and any status at any time.
/// Nothing above this may treat either as evidence of what ran.
///
/// # Errors
///
/// - [`CronError::NotFound`] when the account has no managed entry with that
///   id.
/// - [`CronError::CrontabRefused`] when the crontab could not be read.
/// - [`CronError::EntryFileUnreadable`] when a file is there and cannot be read
///   as the entry's own.
/// - [`CronError::Privilege`] when the account cannot be resolved.
pub fn get_cron_entry_output(
    host: &dyn CronHost,
    account: &AccountName,
    id: &CronEntryId,
) -> Result<CronEntryOutput, CronError> {
    let existing = host.read_crontab(account)?.unwrap_or_default();
    let document = CrontabDocument::parse(&existing);

    if document.entry(id).is_none() {
        return Err(CronError::NotFound);
    }

    Ok(CronEntryOutput {
        output: host.read_output_tail(account, id, MAXIMUM_OUTPUT)?,
        last_run: host.read_run_record(account, id)?,
    })
}

#[cfg(test)]
#[path = "../tests/cron/get_cron_entry_output_tests.rs"]
mod tests;
