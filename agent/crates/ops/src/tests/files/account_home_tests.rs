//! The home of an account is under the account home root and nowhere else.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

use super::account_home;

#[test]
fn the_home_is_the_account_name_under_the_account_home_root() {
    let account = AccountName::parse("alice").unwrap();

    assert_eq!(
        account_home(&account),
        std::path::Path::new(AgentPaths::ACCOUNT_HOME_ROOT).join("alice")
    );
}
