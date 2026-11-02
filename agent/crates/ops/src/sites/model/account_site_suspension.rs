//! Everything this host can say about an account's sites while it is being
//! suspended, or checked for suspension.

use crate::sites::model::site_suspension_fact::SiteSuspensionFact;

/// The answer to "what is this host serving for the account right now?".
///
/// Two states and not a readable flag beside a list: an unreadable vhost
/// directory and an account with no sites would both be an empty list, and the
/// empty list is the one that reads as "everything is suspended". Here the
/// blind answer carries no list at all, so a caller cannot read an empty one
/// out of it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum AccountSiteSuspension {
    /// The vhost directory could not be listed, so nothing is known about the
    /// account's sites.
    ///
    /// Carried as an answer rather than folded into an error because a caller
    /// MUST refuse to conclude a suspension from it (rules/testing.md: state a
    /// check's blind spot in the check's own output).
    Unreadable,
    /// The vhost directory was listed: one entry per vhost the host serves for
    /// the account, possibly none.
    Listed(Vec<SiteSuspensionFact>),
}

impl AccountSiteSuspension {
    /// Whether the vhost directory could actually be listed.
    #[must_use]
    pub fn is_readable(&self) -> bool {
        matches!(self, Self::Listed(_))
    }

    /// The vhosts the listing found, which is none for an unreadable directory.
    ///
    /// An empty slice here does NOT say the account has no sites: ask
    /// [`Self::is_readable`] first, or match on the state, before concluding
    /// anything from emptiness.
    #[must_use]
    pub fn sites(&self) -> &[SiteSuspensionFact] {
        match self {
            Self::Unreadable => &[],
            Self::Listed(sites) => sites,
        }
    }
}
