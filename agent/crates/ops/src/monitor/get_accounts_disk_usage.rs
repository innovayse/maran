//! GetAccountsDiskUsage: what every hosting account occupies on disk.

use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::system_accounts::system_accounts;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::monitor::model::account_disk_usage::AccountDiskUsage;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// Measures every hosting account's home directory tree.
///
/// # Which passwd rows are hosting accounts
///
/// Two conditions, and both are needed. A row's name must parse as an
/// [`AccountName`] — that alone rules out `root`, `daemon` and every service
/// account whose name this panel could not have chosen — and the row's home
/// must be exactly `<home root>/<name>`, the path this agent creates an account
/// with.
///
/// The home check is what settles the case a name check cannot. Every SFTP
/// login this agent creates is a system user whose name is its account's name,
/// an underscore, and a chosen suffix — and account names may contain
/// underscores, so the login `bob` of account `alice` is spelled exactly like
/// the ACCOUNT `alice_bob`. No inspection of the name tells those apart. Their
/// homes do: a login's home is its account's jail under `/var/lib`, and only a
/// hosting account lives under the home root. So a login is never billed as an
/// account of its own, and its bytes are counted once — inside the account's
/// home, where they actually are.
///
/// # Used bytes, and no quota
///
/// The quota an account is measured against is the panel's own data: the panel
/// sets it and stores it. Reading it back here would mean running the quota
/// tools on every dashboard refresh — on a host that may not have them
/// installed — to learn a number the caller already had.
///
/// # Errors
///
/// Returns [`MonitorError::AccountsUnavailable`] when the host's password
/// database cannot be read. An account whose home cannot be walked measures
/// zero rather than failing the call: one unreadable directory must not cost
/// the panel every other account's figure.
pub fn get_accounts_disk_usage(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<Vec<AccountDiskUsage>, MonitorError> {
    let passwd = host.read_password_database(distro.passwd_database())?;

    let mut usage: Vec<AccountDiskUsage> = system_accounts(&passwd)
        .into_iter()
        .filter_map(|row| {
            let account = AccountName::parse(&row.name).ok()?;
            if Path::new(&row.home) != home_of(&account) {
                return None;
            }

            Some(AccountDiskUsage {
                used_bytes: host.directory_size(Path::new(&row.home)),
                account,
            })
        })
        .collect();

    // Sorted so two calls against an unchanged host answer in the same order,
    // whatever order the password database happened to hold its rows in — and
    // then deduplicated, exactly as the SFTP area's own passwd reader does.
    // Nothing stops a passwd file from carrying the same name twice: the shadow
    // tools will not write one, but a hand edit or a half-finished restore
    // will, and every tool on the host then uses the FIRST row while this
    // function would have reported the account twice and the panel would have
    // charged its bytes twice.
    usage.sort_by(|left, right| left.account.as_str().cmp(right.account.as_str()));
    usage.dedup_by(|left, right| left.account == right.account);

    Ok(usage)
}

/// The home directory this agent would have created `account` with.
///
/// Built from [`AgentPaths::ACCOUNT_HOME_ROOT`] rather than compared as a
/// prefix, so that a row whose home merely starts with the home root — a
/// deliberate `/home/alice/../bob`, or a second account nested under the first
/// — is not accepted as this account's own.
fn home_of(account: &AccountName) -> PathBuf {
    PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str())
}

#[cfg(test)]
#[path = "../tests/monitor/get_accounts_disk_usage_tests.rs"]
mod tests;
