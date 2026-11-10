//! Test support for the properties that plant one character in a valid value.
//!
//! Mounted once from `validation/mod.rs` under `#[cfg(test)]`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

/// `text` with `character` inserted before the `position`-th character, or at the
/// end when `position` is past it.
///
/// Counted in characters and not bytes, so the insertion never lands inside a
/// multi-byte character and the result is always valid UTF-8.
pub(crate) fn inserted_at(text: &str, position: usize, character: char) -> String {
    let mut characters: Vec<char> = text.chars().collect();
    let index = position.min(characters.len());
    characters.insert(index, character);

    characters.into_iter().collect()
}
