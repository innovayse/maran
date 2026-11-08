//! The five facts one access-log line contributes to a traffic summary.

use super::traffic_date::TrafficDate;

/// What the tokenizer keeps of one `combined`-format access-log line.
///
/// Deliberately small: the visitor's address, the referer and the user agent are
/// not here, so nothing downstream can store or return them.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParsedAccessLine {
    /// The UTC day the request was logged on.
    pub date: TrafficDate,
    /// The response status, `100..=599`.
    pub status: u16,
    /// Response body bytes sent; `0` for the log's `-`.
    pub body_bytes: u64,
    /// The request path without its query string, control characters replaced,
    /// at most 512 bytes (`MAXIMUM_PATH_BYTES` in the summary's budget).
    /// `None` when the request carried no usable path (a malformed request, an
    /// absolute-form target, or one longer than the cap), which the tally counts
    /// under "other".
    pub path: Option<String>,
}
