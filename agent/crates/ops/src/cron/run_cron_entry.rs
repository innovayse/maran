//! RunCronEntry: start one of the account's entries now, as the account.

use std::sync::LazyLock;

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_run::CronRun;
use crate::cron::model::crontab_document::CrontabDocument;
use crate::cron::run_now_slots::{ACCOUNT_RUN_NOW_SLOTS, HOST_RUN_NOW_SLOTS, RunNowSlots};

/// The runs by hand this process has admitted and not yet seen end (issue #333).
static RUN_NOW_SLOTS: LazyLock<RunNowSlots> =
    LazyLock::new(|| RunNowSlots::new(HOST_RUN_NOW_SLOTS, ACCOUNT_RUN_NOW_SLOTS));

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
/// **Bounded, and refused rather than queued** (issue #333). A run holds a
/// thread of the agent's blocking pool until the job ends, so the agent admits
/// a fixed number at once for the host and a smaller one for each account
/// (`run_now_slots`). The slot is taken as the first statement, before the
/// crontab is read, and given back by every path out of the call, so a refused
/// call has read nothing and a finished one holds nothing.
///
/// Not idempotent, and it cannot be: every call is a new run of the customer's
/// program.
///
/// # Errors
///
/// Returns [`CronError::RunCapacityReached`] when the account or the host
/// already has as many runs going as it is admitted, [`CronError::NotFound`] when the account has no such entry or no
/// such account exists, [`CronError::Account`] when it is not a hosting account,
/// [`CronError::Suspended`] when the account is suspended,
/// [`CronError::RunUnavailable`] when the run could not be started,
/// [`CronError::RunNotStopped`] when a ceiling ended the run and its unit could
/// not be confirmed stopped, and any failure reading the crontab.
pub fn run_cron_entry(
    host: &dyn CronHost,
    account: &AccountName,
    id: &CronEntryId,
) -> Result<CronRun, CronError> {
    run_within(&RUN_NOW_SLOTS, host, account, id)
}

/// [`run_cron_entry`] against the slots it is given, so a test can state the
/// ceilings instead of sharing the process-wide count with every other test.
///
/// # Errors
///
/// Every variant [`run_cron_entry`] documents.
pub(crate) fn run_within(
    slots: &RunNowSlots,
    host: &dyn CronHost,
    account: &AccountName,
    id: &CronEntryId,
) -> Result<CronRun, CronError> {
    // Held to the end of the call and given back by every path out of it.
    let _slot = slots.try_acquire(account)?;

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
