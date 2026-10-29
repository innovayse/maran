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
fn a_domain_of_exactly_the_dns_length_limit_is_accepted_and_one_character_more_is_not() {
    // The boundary itself, both sides of it. The test above uses a candidate
    // 62 characters past the limit, so every off-by-one lives inside its
    // margin: moving the check to `> MAX_LENGTH + 1` left it green while a
    // 254-character name parsed. A limit is only observed where it changes the
    // answer.
    let at_the_limit = [
        "a".repeat(63),
        "b".repeat(63),
        "c".repeat(63),
        "d".repeat(61),
    ]
    .join(".");
    assert_eq!(at_the_limit.len(), 253);
    assert_eq!(
        Domain::parse(&at_the_limit).map(|domain| domain.as_str().len()),
        Ok(253),
        "the longest name DNS allows must still be a site anyone can host"
    );

    let one_over = [
        "a".repeat(63),
        "b".repeat(63),
        "c".repeat(63),
        "d".repeat(62),
    ]
    .join(".");
    assert_eq!(one_over.len(), 254);
    assert!(
        matches!(Domain::parse(&one_over), Err(DomainError::TooLong)),
        "one character past the limit must be refused, or the limit is 254"
    );
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

#[test]
fn an_ipv4_literal_or_numeric_final_label_is_refused() {
    // The final label decides: every dotted IPv4 literal ends in digits, and so
    // do the short forms resolvers read as addresses.
    for candidate in [
        "203.0.113.7",
        "127.0.0.1",
        "10.0.0.1",
        "0.0.0.0",
        "1.2.3.4.5",
        "shop.123",
        "example.007",
        "1.1",
        "12345",
    ] {
        assert_eq!(
            Domain::parse(candidate),
            Err(DomainError::NumericFinalLabel),
            "{candidate} must be refused as a numeric final label"
        );
    }
}

#[test]
fn a_final_label_that_only_contains_some_digits_is_accepted() {
    for candidate in [
        "abc1.com",
        "shop.1a",
        "xn--80ak6aa92e.com",
        "web2.example.com",
        "a.b2",
        "123.example.com",
    ] {
        assert!(
            Domain::parse(candidate).is_ok(),
            "{candidate} has a non-numeric final label and must parse"
        );
    }
}

#[test]
fn localhost_and_its_subdomains_are_refused() {
    for candidate in ["localhost", "LOCALHOST", "foo.localhost", "a.b.localhost"] {
        assert_eq!(
            Domain::parse(candidate),
            Err(DomainError::LocalhostFamily),
            "{candidate} must be refused as a localhost name"
        );
    }
}

#[test]
fn a_trailing_dot_stays_an_invalid_label_not_a_reserved_name() {
    assert!(matches!(
        Domain::parse("localhost."),
        Err(DomainError::InvalidLabel { .. })
    ));
    assert!(matches!(
        Domain::parse("1.2.3.4."),
        Err(DomainError::InvalidLabel { .. })
    ));
}

#[test]
fn a_name_that_is_or_ends_in_a_special_use_suffix_is_refused() {
    // One row per suffix, so a suffix dropped from the set fails its own row.
    for candidate in [
        "local",
        "shop.local",
        "localdomain",
        "localhost.localdomain",
        "internal",
        "shop.internal",
        "lan",
        "shop.lan",
        "home.arpa",
        "printer.home.arpa",
        "invalid",
        "shop.invalid",
    ] {
        assert_eq!(
            Domain::parse(candidate),
            Err(DomainError::SpecialUseSuffix),
            "{candidate} must be refused as a special-use name"
        );
    }
}

#[test]
fn a_name_that_merely_contains_a_reserved_word_is_accepted() {
    for candidate in [
        "localhost.example.com",
        "notlocalhost.com",
        "mylocal.com",
        "shop.notlocal",
        "local.example.com",
        "internal.example.com",
        "lan.example.com",
        "arpa",
        "x.arpa",
        "shop.example.test",
        "shop.example",
    ] {
        assert!(
            Domain::parse(candidate).is_ok(),
            "{candidate} only contains a reserved word and must parse"
        );
    }
}

#[test]
fn reserved_names_are_refused_whatever_their_case() {
    assert_eq!(
        Domain::parse("LocalHost"),
        Err(DomainError::LocalhostFamily)
    );
    assert_eq!(
        Domain::parse("Foo.LOCAL"),
        Err(DomainError::SpecialUseSuffix)
    );
    assert_eq!(
        Domain::parse("Home.ARPA"),
        Err(DomainError::SpecialUseSuffix)
    );
}

#[test]
fn the_shape_checks_run_before_the_reserved_set() {
    // A control character after a reserved word is still an illegal character,
    // so the reserved variants never mask the injection error.
    assert!(matches!(
        Domain::parse("localhost\n"),
        Err(DomainError::IllegalCharacter { .. })
    ));
    assert!(matches!(
        Domain::parse("203.0.113.7\r"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}

#[test]
fn parse_existing_accepts_reserved_names_so_cleanup_still_works() {
    for candidate in [
        "203.0.113.7",
        "localhost",
        "a.localhost",
        "shop.local",
        "x.invalid",
    ] {
        assert_eq!(
            Domain::parse_existing(candidate).map(|domain| domain.as_str().to_owned()),
            Ok(candidate.to_owned()),
            "{candidate} may already exist and must stay reachable"
        );
    }
}

#[test]
fn parse_existing_lowercases_like_parse() {
    assert_eq!(
        Domain::parse_existing("Shop.LOCAL").unwrap().as_str(),
        "shop.local"
    );
}

#[test]
fn parse_existing_still_refuses_a_malformed_name() {
    assert!(matches!(
        Domain::parse_existing(""),
        Err(DomainError::Empty)
    ));
    assert!(matches!(
        Domain::parse_existing("example.com\n"),
        Err(DomainError::IllegalCharacter { .. })
    ));
    assert!(matches!(
        Domain::parse_existing("a..b"),
        Err(DomainError::InvalidLabel { .. })
    ));
    assert!(matches!(
        Domain::parse_existing(&"a".repeat(254)),
        Err(DomainError::TooLong)
    ));
}

#[test]
fn each_reserved_variant_has_a_message_that_names_no_matched_value() {
    for (error, expected) in [
        (
            DomainError::NumericFinalLabel,
            "a domain cannot end in a numeric label",
        ),
        (
            DomainError::LocalhostFamily,
            "a domain cannot be a localhost name",
        ),
        (
            DomainError::SpecialUseSuffix,
            "a domain cannot use a special-use suffix",
        ),
    ] {
        assert_eq!(error.to_string(), expected);
    }
}
