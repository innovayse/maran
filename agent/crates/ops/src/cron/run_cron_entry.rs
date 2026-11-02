//! RunCronEntry: start one of the account's entries now, as the account.

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_run::CronRun;
use crate::cron::model::crontab_document::CrontabDocument;

/// Runs entry `id` of `account` now and waits for it (issue #158).
///
/// The entry must be in the account's crontab, and what runs is that entry's
/// own command file with the crontab's managed environment — nothing the caller
/// sent. The host starts it as the account, inside the account's slice, under
/// a runtime and an output ceiling (`ProcessCronHost`; threat note
/// `docs/superpowers/notes/2026-11-01-cron-run-now-threat-note.md`).
///
/// A DISABLED entry runs: disabling is the customer's switch for "not on a
/// schedule", and this run is one they asked for. A SUSPENDED entry does not:
/// the suspension is the panel's decision, written into the crontab, and a run
/// by hand must not be a way around it.
///
/// **No cron lock is held**, and that is deliberate. The lock waits
/// (rules/rust.md "What this agent serialises"), and a run lasts up to its
/// runtime ceiling, so holding it would stall every write to this account's
/// crontab for minutes. The crontab is only READ here, as
/// `get_cron_entry_output` reads it; an entry deleted while it runs finishes
/// the run it was already on, which is what cron does too.
///
/// Not idempotent, and it cannot be: every call is a new run of the customer's
/// program.
///
/// # Errors
///
/// Returns [`CronError::NotFound`] when the account has no such entry or no
/// such account exists, [`CronError::Account`] when it is not a hosting account,
/// [`CronError::Suspended`] when the account is suspended,
/// [`CronError::RunUnavailable`] when the run could not be started, and any
/// failure reading the crontab.
pub fn run_cron_entry(
    host: &dyn CronHost,
    account: &AccountName,
    id: &CronEntryId,
) -> Result<CronRun, CronError> {
    let managed = host.managed_account(account)?;
    let existing = host.read_crontab(&managed)?.unwrap_or_default();
    let document = CrontabDocument::parse(&existing);

    let entry = document.entry(id).ok_or(CronError::NotFound)?;
    if entry.suspended {
        return Err(CronError::Suspended);
    }

    host.run_entry(&managed, id, document.environment())
}

#[cfg(test)]
#[path = "../tests/cron/run_cron_entry_tests.rs"]
mod tests;
