//! SetAccountCronSuspended: the whole of one account's cron, stopped at once.

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::cron_lock::cron_lock;
use crate::cron::model::crontab_document::CrontabDocument;

/// Suppresses, or restores, every managed entry `account` owns.
///
/// # Why this is not a loop over `set_cron_entry_enabled`
///
/// Because `enabled` is the CUSTOMER's switch and this is the panel's. If
/// suspension wrote to `enabled`, the resume would have nothing to tell an
/// entry the customer had turned off from one suspension turned off, and every
/// job they had disabled would come back running. There is no second copy of
/// that choice to fall back on: the panel keeps no cron rows at all, so the
/// crontab is the only record of it there is.
///
/// So the two facts are two prefixes on the installed line and two fields on
/// [`CronEntry`](crate::cron::model::cron_entry::CronEntry). This operation
/// writes only its own.
///
/// # What it leaves alone
///
/// Everything else. A suspended entry keeps its id, its command file, its log,
/// its schedule and its own `enabled` value; it is merely a comment as far as
/// cron is concerned, which is what makes the resume give back the same
/// entries rather than new ones. FOREIGN lines — anything this agent did not
/// write — are carried across untouched as they are by every other operation
/// here, so a hand-added job keeps firing; `inspect_account_cron` counts them
/// so the panel can report what it did not silence.
///
/// # Idempotency
///
/// Suspending a suspended account installs a table saying exactly what the last
/// one said. An account with no crontab is a success, and it still installs a
/// table: the account-level marker has to be recorded somewhere, or the first
/// entry created while the account is suspended would start firing.
///
/// # Errors
///
/// Returns [`CronError::CrontabRefused`] when the crontab cannot be read or
/// the new table is refused. The account keeps the state it had.
pub fn set_account_cron_suspended(
    host: &dyn CronHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    suspended: bool,
) -> Result<(), CronError> {
    // Held from before the read to after the install, because the two are one
    // decision: a table rendered from a document read outside the lock would
    // still overwrite whatever landed in between. Per account, process-local —
    // see `cron_lock` for the whole argument.
    let _crontab = cron_lock(account);

    let existing = host.read_crontab(account)?.unwrap_or_default();
    let mut document = CrontabDocument::parse(&existing);

    document.set_suspended(suspended);

    host.install_crontab(account, &document.render(account, distro.sh_binary()))
}

#[cfg(test)]
#[path = "../tests/cron/set_account_cron_suspended_tests.rs"]
mod tests;
