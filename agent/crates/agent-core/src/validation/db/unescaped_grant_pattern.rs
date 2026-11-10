//! A stored `GRANT` pattern read back as the database name it was built from.

use crate::validation::db::grant_pattern_for::{
    GRANT_ANY_SEQUENCE_WILDCARD, GRANT_ESCAPE, GRANT_SINGLE_CHARACTER_WILDCARD,
};

/// Reads a stored `GRANT` pattern back as the database name it was built from.
///
/// The exact inverse of [`grant_pattern_for`](crate::validation::db::grant_pattern_for::grant_pattern_for), and it lives beside it for the
/// reason every decoder in this workspace lives beside its constructor: a repair
/// that recognises an already-escaped row has to agree, character for character,
/// with the function that wrote it.
///
/// It is a partial inverse and the caller must treat it as one. Applied to a
/// pattern this panel never wrote - one escaped in part, or escaping some other
/// character - it returns a string with a backslash still in it, which is how
/// `repair_grants` tells the two apart: it re-escapes the result and refuses the
/// row unless it gets the stored bytes back.
#[must_use]
pub fn unescaped_grant_pattern(pattern: &str) -> String {
    let mut name = String::with_capacity(pattern.len());
    let mut characters = pattern.chars().peekable();
    while let Some(character) = characters.next() {
        let escapes_a_wildcard = character == GRANT_ESCAPE
            && matches!(
                characters.peek(),
                Some(&next)
                    if next == GRANT_SINGLE_CHARACTER_WILDCARD
                        || next == GRANT_ANY_SEQUENCE_WILDCARD
            );
        if escapes_a_wildcard {
            continue;
        }
        name.push(character);
    }

    name
}

#[cfg(test)]
#[path = "../../tests/validation/db/unescaped_grant_pattern_tests.rs"]
mod tests;
