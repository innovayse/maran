//! What this host can be observed to be serving for one of an account's sites.

/// One vhost of an account, and whether it is serving the suspended stub.
///
/// An OBSERVATION, not a setting. Nothing in the agent stores it: it is
/// recomputed from the file on disk every time it is asked for, which is what
/// makes it evidence rather than a second copy of the panel's belief.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SiteSuspensionFact {
    /// The site's primary domain, as its vhost file is named.
    pub domain: String,

    /// True when the vhost on disk is BYTE FOR BYTE the suspended render for
    /// this site.
    ///
    /// False also when the agent could not produce the text to compare
    /// against — an unresolvable document root, a vhost whose `server_name`
    /// line it cannot read, a file name that is not a domain. Unobservable is
    /// reported as NOT stubbed, because the two failures cost different
    /// things: a site wrongly reported as serving costs a refused suspension
    /// an operator can look at, and a site wrongly reported as stubbed costs a
    /// suspended customer's site staying up with the panel certifying that it
    /// is not.
    pub serving_stub: bool,
}
