//! Cutting a text to a byte bound without cutting a character.

/// The longest prefix of `text` that is at most `limit` bytes and ends on a
/// character boundary. A text within the bound is returned whole.
#[must_use]
pub fn prefix_within(text: &str, limit: usize) -> &str {
    if text.len() <= limit {
        return text;
    }
    let mut end = limit;
    while end > 0 && !text.is_char_boundary(end) {
        end -= 1;
    }
    text.get(..end).unwrap_or("")
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/prefix_within_tests.rs"]
mod tests;
