//! Tests for the `redirect_target` module.
//!
//! The target becomes a `Location` header, so its scheme, its host and the
//! characters that could end the directive each have a refusal here.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_LENGTH, RedirectTarget, RedirectTargetError};

#[test]
fn site_paths_and_http_urls_are_accepted() {
    for target in [
        "/",
        "/new/",
        "/new/page.html?ref=a&b=c,d",
        "https://example.org",
        "https://example.org/",
        "http://www.example.org/a/b?x=1",
        "https://example.org:8443/path",
        "https://sub.example.org/p%20q",
    ] {
        assert_eq!(RedirectTarget::parse(target).unwrap().as_str(), target);
    }
}

#[test]
fn an_empty_target_is_refused() {
    assert_eq!(RedirectTarget::parse(""), Err(RedirectTargetError::Empty));
}

#[test]
fn a_control_character_is_refused_before_anything_else() {
    for target in [
        "/a\nb",
        "https://example.org/\r\nSet-Cookie: x",
        "/a\0",
        "/\t",
    ] {
        assert_eq!(
            RedirectTarget::parse(target),
            Err(RedirectTargetError::ControlCharacter),
            "{target:?}"
        );
    }
}

#[test]
fn a_target_over_the_limit_is_refused() {
    let target = format!("/{}", "a".repeat(MAXIMUM_LENGTH));

    assert_eq!(
        RedirectTarget::parse(&target),
        Err(RedirectTargetError::TooLong {
            length: MAXIMUM_LENGTH + 1,
            maximum: MAXIMUM_LENGTH,
        })
    );
}

#[test]
fn every_character_that_could_end_the_line_or_open_a_block_is_refused() {
    for target in [
        "/a;b",
        "/a{b",
        "/a}b",
        "/a\"b",
        "/a'b",
        "/a$b",
        "/a b",
        "/a\\b",
        "/a#b",
        "https://example.org/$uri",
        "https://example.org/;",
        "/a<b",
        "/a|b",
        "javascript:alert(1)",
    ] {
        assert_eq!(
            RedirectTarget::parse(target),
            Err(RedirectTargetError::ForbiddenCharacter),
            "{target}"
        );
    }
}

#[test]
fn a_protocol_relative_target_is_refused() {
    for target in ["//evil.example", "//evil.example/path", "///"] {
        assert_eq!(
            RedirectTarget::parse(target),
            Err(RedirectTargetError::ProtocolRelative),
            "{target}"
        );
    }
}

#[test]
fn a_scheme_other_than_http_and_https_is_refused() {
    for target in [
        "javascript:alert",
        "data:text/html,x",
        "ftp://example.org/",
        "HTTP://example.org/",
        "example.org/path",
        "mailto:a@example.org",
        "relative/path",
    ] {
        assert_eq!(
            RedirectTarget::parse(target),
            Err(RedirectTargetError::UnsupportedScheme),
            "{target}"
        );
    }
}

#[test]
fn user_information_in_the_url_is_refused() {
    for target in [
        "https://trusted.example@evil.example/",
        "https://user:pw@example.org/",
        "https://@example.org/",
    ] {
        assert_eq!(
            RedirectTarget::parse(target),
            Err(RedirectTargetError::UserInformation),
            "{target}"
        );
    }
    // An `@` after the authority is just a character of the path.
    assert!(RedirectTarget::parse("https://example.org/@me").is_ok());
}

#[test]
fn a_host_that_is_not_a_valid_name_is_refused() {
    for target in [
        "https:///path",
        "https://-bad.example/",
        "https://exa_mple.org/",
        "https://localhost/",
        "https://203.0.113.7/",
        "https://printer.local/",
    ] {
        assert!(
            matches!(
                RedirectTarget::parse(target),
                Err(RedirectTargetError::InvalidHost(_))
            ),
            "{target}"
        );
    }
}

#[test]
fn a_port_outside_one_to_65535_is_refused() {
    for target in [
        "https://example.org:0/",
        "https://example.org:65536/",
        "https://example.org:/",
        "https://example.org:80a/",
        "https://example.org:123456/",
    ] {
        assert_eq!(
            RedirectTarget::parse(target),
            Err(RedirectTargetError::InvalidPort),
            "{target}"
        );
    }
}
