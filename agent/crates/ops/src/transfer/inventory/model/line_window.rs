//! The lines of one command's output that a parser may read.

use super::super::prefix_within::prefix_within;
use super::inventory_limits::InventoryLimits;

/// The readable lines of a text, with a count of what was refused.
///
/// This is the one place the line bounds are applied, so every line parser
/// holds them the same way: at most [`InventoryLimits::MAX_LINES`] lines of the
/// first [`InventoryLimits::MAX_OUTPUT_BYTES`] bytes, and a line longer than
/// [`InventoryLimits::MAX_LINE_BYTES`] or holding a NUL byte is REFUSED (counted,
/// never cut and never read), because half a hostile line is still hostile.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LineWindow<'a> {
    /// The lines that passed, without their terminators.
    pub lines: Vec<&'a str>,
    /// How many lines were refused for their length or a NUL byte.
    pub rejected: usize,
    /// Whether the byte or line bound cut the text short.
    pub truncated: bool,
}

impl<'a> LineWindow<'a> {
    /// Applies the bounds to `text`.
    #[must_use]
    pub fn of(text: &'a str) -> Self {
        let bounded = prefix_within(text, InventoryLimits::MAX_OUTPUT_BYTES);
        let mut truncated = bounded.len() < text.len();
        let mut lines = Vec::new();
        let mut rejected = 0;
        for (number, line) in bounded.lines().enumerate() {
            if number >= InventoryLimits::MAX_LINES {
                truncated = true;
                break;
            }
            if line.len() > InventoryLimits::MAX_LINE_BYTES || line.as_bytes().contains(&0) {
                rejected += 1;
                continue;
            }
            lines.push(line);
        }
        Self {
            lines,
            rejected,
            truncated,
        }
    }
}

#[cfg(test)]
#[path = "../../../tests/transfer/inventory/model/line_window_tests.rs"]
mod tests;
