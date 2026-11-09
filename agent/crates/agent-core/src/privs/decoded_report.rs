//! What a complete report from a forked job said.

use super::child_report::ChildReport;

/// What a complete report said.
#[derive(Debug, PartialEq, Eq)]
pub(super) enum DecodedReport {
    /// Status `0`: the figures.
    Succeeded(ChildReport),
    /// A refusal number in `1..=63`.
    Refused(u8),
    /// A status above 63.
    Malformed,
}
