//! SetCronEnvironment: replace the panel's assignments, whole.

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::model::cron_environment::CronEnvironment;
use crate::cron::model::crontab_document::CrontabDocument;

/// Replaces `account`'s environment assignments with `environment`.
///
/// Wholesale, and not a merge: the panel holds the list and sends the one it
/// wants, so anything else would make removing an assignment inexpressible.
///
/// The entries are untouched. So is the foreign region — an assignment an
/// administrator wrote above the banner keeps its bytes and its position, which
/// for an assignment is its meaning. What this rewrites is the block below the
/// banner, where the agent's own `MAILTO` and `SHELL` are re-emitted first, so
/// a managed entry runs under the interpreter this agent chose whatever the new
/// list says.
///
/// `MAILTO` and `SHELL` cannot appear in `environment` at all: an
/// [`EnvVarName`](maran_agent_core::validation::system::env_var_name::EnvVarName)
/// refuses both, so this operation needs no check for them and could not be
/// reached with one.
///
/// A duplicated name is written as it was given. Cron applies the last
/// assignment of a name, and rendering the list faithfully is what keeps what
/// the panel stores and what the host applies the same text; silently dropping
/// one would make the crontab disagree with the list the customer is looking
/// at.
///
/// # Errors
///
/// - [`CronError::CrontabRefused`] when the crontab could not be read, or when
///   `crontab` refused the new table. The assignments stay as they were.
pub fn set_cron_environment(
    host: &dyn CronHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    environment: Vec<CronEnvironment>,
) -> Result<(), CronError> {
    let existing = host.read_crontab(account)?.unwrap_or_default();
    let mut document = CrontabDocument::parse(&existing);

    document.set_environment(environment);

    host.install_crontab(account, &document.render(account, distro.sh_binary()))
}

#[cfg(test)]
#[path = "../tests/cron/set_cron_environment_tests.rs"]
mod tests;
