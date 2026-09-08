//! Reading an account's crontab back to find out what it is still doing.

use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::account_cron_suspension::AccountCronSuspension;
use crate::cron::model::crontab_document::CrontabDocument;

/// Counts what `account`'s crontab holds and how much of it is suppressed.
///
/// Read-only. It changes nothing and may be asked of an account in any state.
///
/// # Why an account with no crontab is a suspended one
///
/// `Ok` with three zeros, and that is an OBSERVATION rather than a failure to
/// look: an account that has no crontab has no entry that can fire, which is
/// exactly the state a suspension is trying to reach. The answer that would be
/// dishonest is a crontab this agent could not read reported as an empty one,
/// and that cannot happen here — a read that fails returns the error, so the
/// caller refuses the suspension instead of certifying it.
///
/// # Errors
///
/// Returns [`CronError::CrontabRefused`] when the account's crontab exists and
/// cannot be read.
pub fn inspect_account_cron(
    host: &dyn CronHost,
    account: &AccountName,
) -> Result<AccountCronSuspension, CronError> {
    let text = host.read_crontab(account)?.unwrap_or_default();
    let document = CrontabDocument::parse(&text);

    let entries_suspended = document
        .entries()
        .iter()
        .filter(|entry| entry.suspended)
        .count();

    Ok(AccountCronSuspension {
        entries_total: as_count(document.entries().len()),
        entries_suspended: as_count(entries_suspended),
        foreign_lines: as_count(document.foreign().len()),
    })
}

/// Narrows a count for the wire, saturating rather than wrapping.
///
/// The proto field is a `uint32` and these are `usize`. Saturating is the only
/// direction that cannot lie in the dangerous direction: a wrap would turn a
/// preposterous number of entries into a small one, and a small one is what
/// reads as "nearly everything is suspended".
fn as_count(value: usize) -> u32 {
    u32::try_from(value).unwrap_or(u32::MAX)
}

#[cfg(test)]
#[path = "../tests/cron/inspect_account_cron_tests.rs"]
mod tests;
