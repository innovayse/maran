//! How a site's host names divide between the canonical one and the rest.

/// The result of applying a canonical-host option to a site's names.
///
/// The names are `Domain` spellings (lowercase letters, digits, `-` and `.`),
/// kept as text because this is where they are only written out.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CanonicalSplit {
    /// The host visitors end up on.
    pub canonical: String,
    /// The names that serve the site: the canonical one first, then every other
    /// name that is not redirected, in the order the site lists them.
    pub served: Vec<String>,
    /// The names that redirect to the canonical one. Never empty in a value
    /// the split returns: when there is nothing to redirect it returns none.
    pub redirected: Vec<String>,
}
