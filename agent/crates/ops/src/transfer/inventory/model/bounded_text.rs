//! The text of one command's output, held within the output bound.

use super::super::prefix_within::prefix_within;
use super::inventory_limits::InventoryLimits;

/// One command's standard output after the bound was applied.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BoundedText {
    /// The text; when it was cut, only whole lines of the first
    /// [`InventoryLimits::MAX_OUTPUT_BYTES`] bytes.
    pub text: String,
    /// Whether anything was cut.
    pub truncated: bool,
}

impl BoundedText {
    /// Applies the output bound to `stdout`.
    ///
    /// Output within the bound is kept whole. Output beyond it is cut back to
    /// the last complete line inside the bound, so a parser never reads half a
    /// record, and `truncated` says so.
    #[must_use]
    pub fn of(stdout: String) -> Self {
        if stdout.len() <= InventoryLimits::MAX_OUTPUT_BYTES {
            return Self {
                text: stdout,
                truncated: false,
            };
        }
        let head = prefix_within(&stdout, InventoryLimits::MAX_OUTPUT_BYTES);
        let end = head.rfind('\n').map_or(head.len(), |index| index + 1);
        let text = head.get(..end).unwrap_or("").to_owned();
        Self {
            text,
            truncated: true,
        }
    }
}

#[cfg(test)]
#[path = "../../../tests/transfer/inventory/model/bounded_text_tests.rs"]
mod tests;
