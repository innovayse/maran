//! A site's per-address request limit, as the templates write it.

/// A request limit reduced to the three facts the templates need.
///
/// The numbers come from `agent-core`'s `RequestRate` and the zone name from
/// the domain the agent already validated (`Domain`: lowercase letters, digits,
/// `-` and `.`, so it cannot hold a space, a `;` or a `$`). A template escapes
/// nothing, and a character outside those alphabets never reaches this struct
/// (rules/security.md item 4).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RequestRateLine<'a> {
    /// The name of the `limit_req_zone` of this site, unique per site:
    /// nginx refuses one zone name declared twice, and two sites sharing a zone
    /// would share a budget.
    pub zone: &'a str,
    /// Sustained requests per second one visitor address may make.
    pub per_second: u32,
    /// Requests above the sustained rate that one address may send at once; 0
    /// allows none.
    pub burst: u32,
}
