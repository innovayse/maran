//! Which spelling of a site's host name visitors end up on.

/// The canonical host of a site that has one.
///
/// A site with no preference holds no value of this type (an `Option` around
/// it says so), because "no preference" is not a third spelling.
///
/// The two names of a pair are derived from the site's domain by
/// [`super::site_options::SiteOptions::canonical_split`]: the apex is the
/// domain with one leading `www.` removed, the `www` name is `www.` followed
/// by the apex.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CanonicalHost {
    /// `www.<apex>` answers with the site and the apex redirects to it.
    Www,
    /// `<apex>` answers with the site and `www.<apex>` redirects to it.
    Apex,
}
