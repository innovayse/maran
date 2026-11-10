//! Reading the size of one home.

use super::model::line_window::LineWindow;

/// Bytes in the kibibyte `du -sk` counts in.
const BYTES_PER_KIBIBYTE: u64 = 1024;

/// Reads the output of `HomeDiskUsage` (`du -skx`: `<kibibytes>\t<path>`) into
/// bytes.
///
/// Only the first non-blank line is read, and only its first word, which must be
/// a run of ASCII digits; the path after it is not read. The product is checked:
/// a figure that does not fit 64 bits is "not measured", never a wrapped value.
/// `None` also for an empty, malformed, NUL-carrying or over-long answer.
#[must_use]
pub fn parse_home_usage(output: &str) -> Option<u64> {
    let window = LineWindow::of(output);
    let line = window.lines.iter().find(|line| !line.trim().is_empty())?;
    let figure = line.split_whitespace().next()?;
    if !figure.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }
    figure.parse::<u64>().ok()?.checked_mul(BYTES_PER_KIBIBYTE)
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_home_usage_tests.rs"]
mod tests;
