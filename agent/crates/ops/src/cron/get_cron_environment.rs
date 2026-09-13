//! GetCronEnvironment: the assignments the panel owns, and only those.

use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_environment::CronEnvironment;
use crate::cron::model::crontab_document::CrontabDocument;

/// Lists the environment assignments the panel set for `account`.
///
/// Only the ones below the agent's own banner are reported. An assignment an
/// administrator wrote by hand above it belongs to the foreign region: it is
/// carried across every install untouched, and reporting it here would invite
/// the panel to offer an edit that would in fact MOVE it — and an assignment's
/// position is its meaning, because it applies to the lines beneath it.
///
/// `MAILTO` and `SHELL` are never reported either, on the same principle from
/// the other side: the agent writes those two itself on every install, so they
/// are not the account's to see or to change.
///
/// An account with no crontab has no assignments, which is an empty list rather
/// than an error.
///
/// # Errors
///
/// - [`CronError::CrontabRefused`] when the crontab could not be read.
pub fn get_cron_environment(
    host: &dyn CronHost,
    account: &AccountName,
) -> Result<Vec<CronEnvironment>, CronError> {
    let Some(text) = host.read_crontab(account)? else {
        return Ok(Vec::new());
    };

    Ok(CrontabDocument::parse(&text).environment().to_vec())
}

#[cfg(test)]
#[path = "../tests/cron/get_cron_environment_tests.rs"]
mod tests;
