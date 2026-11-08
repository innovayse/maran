//! Which kind of redirect a rule answers with.

/// The HTTP status of a redirect rule: the two a site owner chooses between.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RedirectStatus {
    /// 301: the move is permanent and browsers and search engines may remember it.
    Permanent,
    /// 302: the move is temporary.
    Temporary,
}
