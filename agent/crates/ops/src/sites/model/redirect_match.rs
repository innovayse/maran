//! How a redirect rule's path is compared with the request path.

/// How a redirect rule matches.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum RedirectMatch {
    /// Only the exact path.
    Exact,
    /// The path and everything under it. The path and the target both end with
    /// `/`, because the rest of the request path is appended to the target.
    Prefix,
}
