//! Tests for the `redirect_rule` module: the same neutral line, spelled for `RedirectMatch`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::RedirectRule;
use crate::nginx::redirect_line::RedirectLine;

/// A line with the given shape.
fn line(path: &str, prefix: bool, permanent: bool) -> RedirectLine<'_> {
    RedirectLine {
        path,
        prefix,
        target: "/new/",
        permanent,
    }
}

#[test]
fn an_exact_rule_is_anchored_at_both_ends_and_escapes_the_dot() {
    let rule = RedirectRule::from_line(&line("/a.html", false, true));

    assert_eq!(rule.pattern, "^/a\\.html$");
    assert_eq!(rule.replacement, "/new/");
    assert_eq!(rule.status, 301);
}

#[test]
fn a_prefix_rule_carries_the_rest_of_the_path_into_the_replacement() {
    let rule = RedirectRule::from_line(&line("/old/", true, false));

    assert_eq!(rule.pattern, "^/old/(.*)$");
    assert_eq!(rule.replacement, "/new/$1");
    assert_eq!(rule.status, 302);
}

#[test]
fn an_exact_rule_does_not_carry_a_backreference() {
    let rule = RedirectRule::from_line(&line("/old", false, true));

    assert!(!rule.replacement.contains("$1"));
}
