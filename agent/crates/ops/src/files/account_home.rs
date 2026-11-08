//! Where an account's home directory is.

use std::path::PathBuf;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

/// The absolute home directory of `account`: `<account home root>/<account>`.
///
/// Built from [`AgentPaths::ACCOUNT_HOME_ROOT`] and a validated [`AccountName`],
/// which holds no separator, so the join cannot leave the root. It is the one
/// name in a walk a customer cannot rewrite (`/home` is root-owned), which is why
/// `open_home` can take it by path.
#[must_use]
pub(crate) fn account_home(account: &AccountName) -> PathBuf {
    PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str())
}

#[cfg(test)]
#[path = "../tests/files/account_home_tests.rs"]
mod tests;
