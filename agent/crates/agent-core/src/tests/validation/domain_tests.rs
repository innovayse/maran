//! Tests for the `domain` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `domain.rs` declares this file with `#[path]`, which keeps it a child module
//! and therefore able to reach private items — a crate-level `tests/` directory
//! sees only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::domain::Domain;
use super::super::domain_error::DomainError;

#[test]
fn an_ordinary_domain_parses() {
    assert_eq!(
        Domain::parse("example.com").unwrap().as_str(),
        "example.com"
    );
}

#[test]
fn a_domain_is_lowercased_so_two_cases_are_not_two_sites() {
    assert_eq!(
        Domain::parse("Example.COM").unwrap().as_str(),
        "example.com"
    );
}

#[test]
fn a_domain_containing_a_newline_is_rejected() {
    // The attack this type exists for: written into `server_name example.com;`
    // verbatim, the rest of the value becomes directives of the caller's
    // choosing. rules/security.md calls this the panel's SQL injection.
    let refused = Domain::parse("example.com;\n}\nserver {\n  listen 80");

    assert!(matches!(refused, Err(DomainError::IllegalCharacter { .. })));
}

#[test]
fn a_domain_containing_a_carriage_return_is_rejected() {
    assert!(matches!(
        Domain::parse("example.com\r"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn a_domain_containing_a_null_byte_is_rejected() {
    assert!(matches!(
        Domain::parse("example.com\0"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn a_path_traversal_dressed_as_a_domain_is_rejected() {
    // The document root is built from the domain, so `..` must never reach it.
    assert!(matches!(
        Domain::parse("../../etc/nginx"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn an_empty_label_is_rejected() {
    assert!(matches!(
        Domain::parse("example..com"),
        Err(DomainError::InvalidLabel { .. })
    ));
}

#[test]
fn a_label_starting_with_a_hyphen_is_rejected() {
    assert!(matches!(
        Domain::parse("-example.com"),
        Err(DomainError::InvalidLabel { .. })
    ));
}

#[test]
fn an_empty_domain_is_rejected() {
    assert!(matches!(Domain::parse(""), Err(DomainError::Empty)));
}

#[test]
fn a_domain_over_the_dns_length_limit_is_rejected() {
    let label = "a".repeat(63);
    let candidate = std::iter::repeat_n(label, 5).collect::<Vec<_>>().join(".");
    assert!(candidate.len() > 253);

    assert!(matches!(
        Domain::parse(&candidate),
        Err(DomainError::TooLong)
    ));
}

#[test]
fn a_domain_containing_a_null_terminated_shell_metacharacter_is_rejected() {
    // `$()`, backticks and `;` have no place in a hostname either — a domain
    // this permissive would be one template change away from command
    // injection if it were ever interpolated into a shell string.
    assert!(matches!(
        Domain::parse("example.com$(rm -rf /)"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn a_domain_containing_a_space_is_rejected() {
    assert!(matches!(
        Domain::parse("example .com"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn a_label_over_the_limit_is_rejected() {
    let label = "a".repeat(64);
    let candidate = format!("{label}.com");

    assert!(matches!(
        Domain::parse(&candidate),
        Err(DomainError::InvalidLabel { .. })
    ));
}

#[test]
fn a_label_ending_with_a_hyphen_is_rejected() {
    assert!(matches!(
        Domain::parse("example-.com"),
        Err(DomainError::InvalidLabel { .. })
    ));
}
