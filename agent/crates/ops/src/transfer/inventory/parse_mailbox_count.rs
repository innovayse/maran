//! Reading the number of mailboxes under one home.

use super::model::line_window::LineWindow;

/// The most mailboxes one home is believed to hold; a larger figure is a lie or
/// a mistake and is "not measured".
const MAX_MAILBOXES: u32 = 10_000_000;

/// Reads the output of `CountMailboxes` (one decimal count, possibly padded with
/// spaces) into a number.
///
/// Only the first non-blank line is read and it must be a run of ASCII digits
/// once trimmed. `None` for anything else, or for a figure above ten million.
#[must_use]
pub fn parse_mailbox_count(output: &str) -> Option<u32> {
    let window = LineWindow::of(output);
    let line = window.lines.iter().find(|line| !line.trim().is_empty())?;
    let figure = line.trim();
    if !figure.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }
    figure
        .parse::<u32>()
        .ok()
        .filter(|count| *count <= MAX_MAILBOXES)
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/parse_mailbox_count_tests.rs"]
mod tests;
