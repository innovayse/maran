//! DeleteCronEntry: the line first, then the files it named.

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::crontab_document::CrontabDocument;

/// Removes the entry `id` and every file it owns.
///
/// # Why the crontab goes first
///
/// The installed line names three files inside the account's home. Removing
/// those files first would leave a schedule cron still fires, running
/// `/bin/sh` against a path that is no longer there — a failing job every
/// minute, and an exit file being written again by the very entry that was
/// supposed to be gone. Installing the crontab without the entry first means
/// the worst outcome of a failure afterwards is three files nothing reads.
///
/// # Idempotency
///
/// An id the account does not own is [`CronError::NotFound`], which is the
/// answer a repeated deletion gets: the first one succeeded and took the entry
/// out of the crontab, so the second finds nothing to remove. The file removal
/// itself is idempotent file by file, so a deletion interrupted between the
/// install and the removal is completed by running it again — except that the
/// entry is now gone from the crontab, which is why the leftover files are
/// litter rather than a broken state.
///
/// # Errors
///
/// - [`CronError::NotFound`] when the account has no managed entry with that
///   id.
/// - [`CronError::CrontabRefused`] when `crontab` refused the table. Nothing is
///   removed and the entry still runs.
/// - [`CronError::EntryFileUnremovable`] when a file is there and cannot be
///   removed. The entry is already out of the crontab by then.
/// - [`CronError::Privilege`] when the account cannot be resolved or the
///   privilege drop for the home-side removal fails.
pub fn delete_cron_entry(
    host: &dyn CronHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    id: &CronEntryId,
) -> Result<(), CronError> {
    let existing = host.read_crontab(account)?.unwrap_or_default();
    let mut document = CrontabDocument::parse(&existing);

    if !document.remove(id) {
        return Err(CronError::NotFound);
    }

    host.install_crontab(account, &document.render(account, distro.sh_binary()))?;

    host.remove_entry_files(account, id)
}

#[cfg(test)]
#[path = "../tests/cron/delete_cron_entry_tests.rs"]
mod tests;
