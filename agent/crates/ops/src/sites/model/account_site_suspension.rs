//! Everything this host can say about an account's sites while it is being
//! suspended, or checked for suspension.

use crate::sites::model::site_suspension_fact::SiteSuspensionFact;

/// The answer to "what is this host serving for the account right now?".
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct AccountSiteSuspension {
    /// True when the vhost directory could actually be listed.
    ///
    /// Carried beside the facts rather than folded into an error because
    /// [`Self::sites`] going empty is the one way this answer can silently
    /// stop observing anything: a directory that cannot be read and an
    /// account with no sites at all produce the same empty list, and the
    /// empty list is the one that reads as "everything is suspended". A
    /// caller MUST refuse to conclude a suspension while this is false
    /// (rules/testing.md: state a check's blind spot in the check's own
    /// output).
    pub directory_readable: bool,

    /// One entry per vhost the host serves for the account.
    pub sites: Vec<SiteSuspensionFact>,
}
