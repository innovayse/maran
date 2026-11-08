//! What one traffic summary call read.

use super::traffic_cursor::TrafficCursor;
use super::traffic_day::TrafficDay;

/// The answer of one summary call.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TrafficSummary {
    /// Deltas per UTC day, oldest first.
    pub days: Vec<TrafficDay>,
    /// Where the next call starts; `None` when the site has no access log yet.
    pub cursor: Option<TrafficCursor>,
    /// `false` when the byte budget stopped the read before the end of the log.
    pub caught_up: bool,
    /// `true` when the file the request's cursor named is gone, so the lines
    /// between it and what was read are lost.
    pub gap: bool,
    /// Lines the tokenizer refused.
    pub unparsed_lines: u32,
}
