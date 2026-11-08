//! Tests for the `redirect_path` module.
//!
//! The path is written into a `location` line, so every character that could
//! end the line or open a block has a refusal here.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_LENGTH, RedirectPath, RedirectPathError};

#[test]
fn ordinary_paths_are_accepted() {
    for path in [
        "/",
        "/old",
        "/old/",
        "/a-b/c_d/e.f~g",
        "/Blog/2024/Post-1.html",
    ] {
        assert_eq!(RedirectPath::parse(path).unwrap().as_str(), path);
    }
}

#[test]
fn an_empty_path_is_refused() {
    assert_eq!(RedirectPath::parse(""), Err(RedirectPathError::Empty));
}

#[test]
fn a_control_character_is_refused_before_anything_else() {
    for path in ["/a\nb", "/a\rb", "/a\0b", "/a\tb", "/old;\ndeny all"] {
        assert_eq!(
            RedirectPath::parse(path),
            Err(RedirectPathError::ControlCharacter),
            "{path:?}"
        );
    }
}

#[test]
fn a_path_over_the_limit_is_refused() {
    let path = format!("/{}", "a".repeat(MAXIMUM_LENGTH));

    assert_eq!(
        RedirectPath::parse(&path),
        Err(RedirectPathError::TooLong {
            length: MAXIMUM_LENGTH + 1,
            maximum: MAXIMUM_LENGTH,
        })
    );
    assert!(RedirectPath::parse(&format!("/{}", "a".repeat(MAXIMUM_LENGTH - 1))).is_ok());
}

#[test]
fn a_path_that_is_not_absolute_is_refused() {
    for path in ["old", "http://x.example/", "./old"] {
        assert_eq!(
            RedirectPath::parse(path),
            Err(RedirectPathError::NotAbsolute),
            "{path}"
        );
    }
}

#[test]
fn every_character_that_could_end_the_line_or_open_a_block_is_refused() {
    for path in [
        "/a;b", "/a{b", "/a}b", "/a\"b", "/a'b", "/a$b", "/a b", "/a\\b", "/a%2Fb", "/a?b", "/a#b",
        "/a*b", "/a(b", "/a|b", "/áb",
    ] {
        assert_eq!(
            RedirectPath::parse(path),
            Err(RedirectPathError::ForbiddenCharacter),
            "{path}"
        );
    }
}

#[test]
fn a_dot_segment_is_refused() {
    for path in ["/a/./b", "/a/../b", "/..", "/."] {
        assert_eq!(
            RedirectPath::parse(path),
            Err(RedirectPathError::DotSegment),
            "{path}"
        );
    }
    // A name that merely contains dots is fine.
    assert!(RedirectPath::parse("/a..b/.c/d.").is_ok());
}

#[test]
fn an_empty_segment_is_refused_but_a_trailing_slash_is_not() {
    for path in ["//", "/a//b", "//a"] {
        assert_eq!(
            RedirectPath::parse(path),
            Err(RedirectPathError::EmptySegment),
            "{path}"
        );
    }
    assert!(RedirectPath::parse("/a/").is_ok());
}

#[test]
fn the_certificate_challenge_directory_is_refused_at_and_under_it() {
    for path in [
        "/.well-known/acme-challenge",
        "/.well-known/acme-challenge/",
        "/.well-known/acme-challenge/token",
    ] {
        assert_eq!(
            RedirectPath::parse(path),
            Err(RedirectPathError::AcmeChallenge),
            "{path}"
        );
    }
}

#[test]
fn a_path_that_only_shares_the_challenge_directorys_start_is_accepted() {
    // The inverse control for the refusal above: the check is about the
    // directory, not about the first characters of its name.
    for path in [
        "/.well-known/",
        "/.well-known/acme",
        "/.well-known/acme-challenges",
        "/.well-known/security.txt",
    ] {
        assert!(RedirectPath::parse(path).is_ok(), "{path}");
    }
}
