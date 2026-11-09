//! Requests answered with one status code.

/// How many requests of one day were answered with `code`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TrafficStatusCount {
    /// The HTTP status code, three digits.
    pub code: u16,
    /// Requests answered with it.
    pub requests: u64,
}
