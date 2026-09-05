//! UpdateCronEntry: a new schedule, a new command, or both.

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::crontab_document::CrontabDocument;

/// Gives the entry `id` a new schedule and a new command.
///
/// # Why the crontab is installed before the command file is touched
///
/// The two halves of an entry live in two places, so an update is two writes
/// and one of them can fail after the other succeeded. The order is chosen so
/// that the failure that actually happens leaves the entry exactly as it was:
/// `crontab(1)` is the step that refuses tables — a foreign line an
/// administrator left half-written is enough — while writing a file inside a
/// home the account owns fails only when the host is broken. Taking the refusal
/// first means a rejected update changes nothing at all.
///
/// The reverse order was considered and is worse: it would rewrite the command
/// while leaving the old schedule installed, so an entry the customer believes
/// they failed to change would start running the new command at the old time.
///
/// Between the two writes cron may fire the entry at the new schedule with the
/// old command. That window is one write long and is the price of the entry
/// having two halves; the alternative window — the new command at the old
/// schedule — is not smaller, only differently shaped.
///
/// # No duplicate check
///
/// Deliberate, and not an oversight. An update that lands on another entry's
/// schedule and command produces a pair the customer made themselves out of two
/// entries they already owned, one edit at a time; refusing it would leave them
/// unable to correct an entry towards a shape they can already reach by
/// deleting and recreating. Creation refuses duplicates because the caller
/// there cannot tell a lost reply from a lost request — an update has an id, so
/// it never has that ambiguity.
///
/// # Errors
///
/// - [`CronError::NotFound`] when the account has no managed entry with that
///   id. Nothing is written.
/// - [`CronError::CrontabRefused`] when `crontab` refused the table. The
///   command file is not touched.
/// - [`CronError::EntryFileUnwritable`] when the command file could not be
///   written after the schedule was installed.
/// - [`CronError::Privilege`] when the account cannot be resolved or the
///   privilege drop for the home-side write fails.
pub fn update_cron_entry(
    host: &dyn CronHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    id: &CronEntryId,
    schedule: &CronSchedule,
    command: &CronCommand,
) -> Result<(), CronError> {
    let existing = host.read_crontab(account)?.unwrap_or_default();
    let mut document = CrontabDocument::parse(&existing);

    if !document.set_schedule(id, schedule) {
        return Err(CronError::NotFound);
    }

    host.install_crontab(account, &document.render(account, distro.sh_binary()))?;

    host.write_command_file(account, id, command)
}

#[cfg(test)]
#[path = "../tests/cron/update_cron_entry_tests.rs"]
mod tests;
