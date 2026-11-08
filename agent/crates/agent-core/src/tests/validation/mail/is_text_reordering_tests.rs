//! Tests for the `is_text_reordering` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::is_text_reordering;

#[test]
fn the_bidirectional_overrides_and_zero_width_characters_are_text_reordering() {
    for c in [
        '\u{202E}', '\u{202A}', '\u{2066}', '\u{2069}', '\u{200B}', '\u{200F}', '\u{2060}',
        '\u{FEFF}',
    ] {
        assert!(is_text_reordering(c), "{c:?}");
    }
}

#[test]
fn the_line_separators_the_arabic_letter_mark_and_the_tag_block_are_text_reordering() {
    for c in [
        '\u{2028}',
        '\u{2029}',
        '\u{061C}',
        '\u{E0000}',
        '\u{E0041}',
        '\u{E007F}',
    ] {
        assert!(is_text_reordering(c), "{c:?}");
    }
}

#[test]
fn the_characters_just_outside_each_range_are_not() {
    for c in [
        '\u{061B}',
        '\u{061D}',
        '\u{200A}',
        '\u{2010}',
        '\u{2027}',
        '\u{202F}',
        '\u{205F}',
        '\u{2065}',
        '\u{206A}',
        '\u{FEFE}',
        '\u{DFFFF}',
        '\u{E0080}',
    ] {
        assert!(!is_text_reordering(c), "{c:?}");
    }
}

#[test]
fn ordinary_letters_in_several_scripts_are_not() {
    for c in [
        'a', 'Z', '7', ' ', '\u{e9}', '\u{44f}', '\u{561}', '\u{4e2d}',
    ] {
        assert!(!is_text_reordering(c), "{c:?}");
    }
}
