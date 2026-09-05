//! SetCronEntryEnabled: the one prefix that decides whether cron sees a line.

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::crontab_document::CrontabDocument;

/// Turns the entry `id` on or off.
///
/// Disabling comments the entry's line out and changes nothing else: the marker
/// stays, the command file stays, the log and exit files stay. That is what
/// makes turning it back on give the customer the same entry rather than a new
/// one — the id, the command and the history of the last run are all still
/// there, and cron simply cannot see the schedule.
///
/// It is a prefix on the line rather than a line taken out of the file because
/// the entry has to be findable while it is off. An entry removed from the
/// crontab would be an entry the panel could not list, could not re-enable and
/// could not delete, with three orphan files under a customer's home and
/// nothing anywhere naming them.
///
/// Idempotent: enabling an entry that is on, or disabling one that is off,
/// reinstalls a table that says exactly what the last one said.
///
/// # Errors
///
/// - [`CronError::NotFound`] when the account has no managed entry with that
///   id. Nothing is installed.
/// - [`CronError::CrontabRefused`] when `crontab` refused the table. The entry
///   keeps the state it had.
pub fn set_cron_entry_enabled(
    host: &dyn CronHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    id: &CronEntryId,
    enabled: bool,
) -> Result<(), CronError> {
    let existing = host.read_crontab(account)?.unwrap_or_default();
    let mut document = CrontabDocument::parse(&existing);

    if !document.set_enabled(id, enabled) {
        return Err(CronError::NotFound);
    }

    host.install_crontab(account, &document.render(account, distro.sh_binary()))
}

#[cfg(test)]
#[path = "../tests/cron/set_cron_entry_enabled_tests.rs"]
mod tests;
