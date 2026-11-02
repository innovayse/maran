//! What one vhost was observed to be serving.

/// What the vhost on disk was observed to serve: the suspension page, anything
/// else, or nothing the agent could judge.
///
/// Three states and not a bool, because "this site is not stubbed" and "this
/// site could not be compared" call for the same refusal by the panel but are
/// different facts about the host: the first is a site that is really serving
/// its own content, the second is a vhost the agent cannot read well enough to
/// say what it serves.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SiteServing {
    /// The vhost on disk is BYTE FOR BYTE the suspended render for this site.
    Stub,
    /// The text to compare against was produced and the vhost differs from it:
    /// the site serves something other than the suspension page.
    NotStub,
    /// The text to compare against could not be produced — an unresolvable
    /// document root, a vhost whose `server_name` line cannot be read, a file
    /// name that is not a domain. Nothing is known about what the site serves,
    /// and a suspension attestation must not certify it.
    Unobservable,
}
