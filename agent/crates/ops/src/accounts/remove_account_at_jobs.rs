//! Removing an account's queued `at` jobs before its uid is freed.

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_distro::{AdapterBinary, DistroAdapter};

use crate::accounts::model::at_job::AtJob;
use crate::accounts::parse_atq_listing::parse_atq_listing;
use crate::accounts::{AccountError, SystemHost};

/// Removes every queued `at` job of `account` and verifies none is left, so a
/// reused uid does not inherit a command scheduled by the previous tenant.
///
/// # How
///
/// If the `atq` binary is absent (`lstat`) and the `at` spool directory is
/// absent too, `at` was never installed and there is nothing to remove. If the
/// binary is absent but the spool exists, the queue cannot be inspected and the
/// deletion is refused. Otherwise: `atq`, an exact match of the last column
/// against the account name (`acme` never matches `acme2`), `atrm <id>` per job
/// (argv only, the id is digits by construction), then `atq` again.
///
/// # Errors
///
/// - [`AccountError::CommandUnavailable`] when `atq` is missing but the spool exists.
/// - [`AccountError::UnreadableOutput`] when a listing has an unparseable line.
/// - [`AccountError::CommandFailed`] when `atq` or `atrm` exits non-zero.
/// - [`AccountError::Bound`] when a spawn outran its deadline.
/// - [`AccountError::AtJobsRemain`] when the second listing still shows a job.
///
/// Must be called with the account's lock held; that lock is process-local.
pub(crate) fn remove_account_at_jobs(
    host: &dyn SystemHost,
    distro: &dyn DistroAdapter,
    account: &ManagedAccount,
) -> Result<(), AccountError> {
    let atq = distro.atq_binary();
    if host.home_metadata(atq.as_str())?.is_none() {
        let spool = distro.at_spool_directory();
        if host.home_metadata(spool)?.is_none() {
            return Ok(());
        }
        return Err(AccountError::CommandUnavailable {
            program: atq.to_string(),
            reason: "the at spool exists but atq is not installed".to_owned(),
        });
    }

    let atrm = distro.atrm_binary();
    let name = account.name().as_str();
    for job in owned_jobs(host, &atq, name)? {
        let removed = host.run(&atrm, &[job.id.as_str()], CallClass::Mutation)?;
        if removed.status != 0 {
            return Err(AccountError::command_failed(atrm.as_str(), &removed));
        }
    }

    if !owned_jobs(host, &atq, name)?.is_empty() {
        tracing::warn!(username = name, "at jobs of the account survived atrm");
        return Err(AccountError::AtJobsRemain {
            username: name.to_owned(),
        });
    }
    Ok(())
}

/// Lists the queue with `atq` and returns the jobs whose user is exactly `name`.
///
/// # Errors
///
/// As [`remove_account_at_jobs`]: spawn failures, a non-zero `atq`, or a listing
/// that does not parse.
fn owned_jobs(
    host: &dyn SystemHost,
    atq: &AdapterBinary,
    name: &str,
) -> Result<Vec<AtJob>, AccountError> {
    let listed = host.run(atq, &[], CallClass::Query)?;
    if listed.status != 0 {
        return Err(AccountError::command_failed(atq.as_str(), &listed));
    }
    let jobs = parse_atq_listing(&listed.stdout).ok_or_else(|| AccountError::UnreadableOutput {
        program: atq.to_string(),
    })?;
    Ok(jobs.into_iter().filter(|job| job.user == name).collect())
}

#[cfg(test)]
#[path = "../tests/accounts/remove_account_at_jobs_tests.rs"]
mod tests;
