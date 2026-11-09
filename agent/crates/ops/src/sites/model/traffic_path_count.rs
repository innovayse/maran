//! Requests for one path.

/// How many requests of one day asked for `path`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrafficPathCount {
    /// The request path, without its query string.
    pub path: String,
    /// Requests for it.
    pub requests: u64,
}
