//! Whether a character reorders or hides text.

/// Whether `c` is a character that reorders, isolates, hides or re-breaks text:
/// the bidirectional embeddings, overrides, isolates and the Arabic letter
/// mark, the zero-width characters, the invisible operators, the byte-order
/// mark, the Unicode line and paragraph separators (which are not
/// `char::is_control` and still start a new line on screen), and the tag
/// block, whose characters render as nothing and are used to smuggle text.
///
/// None of them has a use in a line of a customer's out-of-office reply, and
/// each has a use in making one thing read as another, so the reply's text
/// refuses them. They are not `char::is_control`, which is why the question is
/// its own function: a check for control characters alone lets them through.
pub(crate) fn is_text_reordering(c: char) -> bool {
    matches!(
        c,
        '\u{061C}'
            | '\u{200B}'..='\u{200F}'
            | '\u{2028}'..='\u{202E}'
            | '\u{2060}'..='\u{2064}'
            | '\u{2066}'..='\u{2069}'
            | '\u{FEFF}'
            | '\u{E0000}'..='\u{E007F}'
    )
}

#[cfg(test)]
#[path = "../../tests/validation/mail/is_text_reordering_tests.rs"]
mod tests;
