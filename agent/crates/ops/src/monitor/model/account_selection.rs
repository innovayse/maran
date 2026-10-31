//! Which hosting accounts one disk-usage reading measures.

use std::collections::HashSet;

use maran_agent_core::validation::system::name::AccountName;

/// The accounts a disk-usage reading is asked about.
///
/// Two cases and no third, because the wire has exactly two meanings: an
/// empty list is "every hosting account" (what a caller that predates the
/// list asks for), and a non-empty one is "these and no others". An empty
/// [`AccountSelection::Only`] cannot be built from the wire — the service maps
/// an empty list to [`AccountSelection::Every`] — and if built by hand it
/// selects nothing, which is the literal meaning of the set it holds.
#[derive(Debug, Clone, PartialEq, Eq)]
#[non_exhaustive]
pub enum AccountSelection {
    /// Every hosting account on the host.
    Every,
    /// Only these accounts. A name that is not a hosting account on this host
    /// is simply never matched.
    Only(HashSet<AccountName>),
}

impl AccountSelection {
    /// Whether `account` is one this reading must measure.
    #[must_use]
    pub fn includes(&self, account: &AccountName) -> bool {
        match self {
            Self::Every => true,
            Self::Only(accounts) => accounts.contains(account),
        }
    }
}
