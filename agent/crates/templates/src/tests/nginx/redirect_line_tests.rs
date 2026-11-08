//! Tests for the `redirect_line` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::RedirectLine;

/// A prefix rule for `path`.
fn prefix(path: &str) -> RedirectLine<'_> {
    RedirectLine {
        path,
        prefix: true,
        target: "/new/",
        permanent: true,
    }
}

#[test]
fn a_prefix_pattern_anchors_the_path_and_captures_the_rest() {
    assert_eq!(prefix("/blog/").rewrite_pattern(), "^/blog/(.*)$");
    assert_eq!(prefix("/").rewrite_pattern(), "^/(.*)$");
}

#[test]
fn the_dots_of_a_path_are_escaped_so_they_match_only_a_dot() {
    // An unescaped `.` would let `/v1x2/` match the rule written for `/v1.2/`.
    assert_eq!(prefix("/v1.2/").rewrite_pattern(), "^/v1\\.2/(.*)$");
    assert_eq!(prefix("/a.b.c/").rewrite_pattern(), "^/a\\.b\\.c/(.*)$");
}
