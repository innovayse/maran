//! One account's home the repair deliberately did not touch.

use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::model::home_group_repair_refusal::HomeGroupRepairRefusal;

/// An account whose home the repair left exactly as it found it, and why.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RefusedHome {
    /// The account examined. Its name parsed and it is on this host — that is
    /// as much as a refused row is known to have in common with an account
    /// this agent created.
    pub account: AccountName,

    /// The path examined — the recorded home when it did not match the
    /// expected one, or the expected home otherwise. Carried as a plain
    /// string, not [`maran_agent_core::validation::fs::path`], because a
    /// refused row is precisely a path this agent does not treat as safe to
    /// act on.
    pub home: String,

    /// Why the repair refused this account.
    pub reason: HomeGroupRepairRefusal,
}
