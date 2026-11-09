//! Tests for which names the panel can be told.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::os::unix::ffi::OsStrExt;

use super::representable_name;

#[test]
fn an_ordinary_utf8_name_is_representable() {
    assert_eq!(
        representable_name(OsStr::new("caf\u{e9}.txt")),
        Some("caf\u{e9}.txt")
    );
}

#[test]
fn a_name_that_is_not_utf8_is_not_representable() {
    assert_eq!(representable_name(OsStr::from_bytes(b"bad\xff.txt")), None);
}

#[test]
fn a_name_with_a_newline_is_not_representable() {
    assert_eq!(representable_name(OsStr::new("two\nlines")), None);
}

#[test]
fn a_name_with_a_tab_is_not_representable() {
    assert_eq!(representable_name(OsStr::new("a\tb")), None);
}
