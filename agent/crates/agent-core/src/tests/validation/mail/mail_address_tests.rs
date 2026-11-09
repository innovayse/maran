//! Tests for the `mail_address` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MailAddress;
use crate::validation::mail::mail_address_error::MailAddressError;
use crate::validation::mail::mail_domain_error::MailDomainError;
use crate::validation::mail::mail_local_part_error::MailLocalPartError;
use crate::validation::web::domain_error::DomainError;

#[test]
fn an_address_is_accepted_and_both_halves_are_lowercased() {
    let address = MailAddress::parse("Alice@Example.COM").unwrap();

    assert_eq!(address.local_part(), "alice");
    assert_eq!(address.domain().as_str(), "example.com");
    assert_eq!(address.to_address(), "alice@example.com");
}

#[test]
fn the_extension_delimiter_is_accepted_in_a_target() {
    assert_eq!(
        MailAddress::parse("name+tag@example.org")
            .unwrap()
            .to_address(),
        "name+tag@example.org"
    );
}

#[test]
fn an_address_without_an_at_sign_is_refused() {
    assert_eq!(
        MailAddress::parse("alice"),
        Err(MailAddressError::MissingAtSign)
    );
}

#[test]
fn a_second_at_sign_is_refused_rather_than_read_as_a_quoted_local_part() {
    assert_eq!(
        MailAddress::parse("a@b@example.com"),
        Err(MailAddressError::MultipleAtSigns)
    );
}

#[test]
fn a_quoted_local_part_is_refused() {
    assert!(matches!(
        MailAddress::parse("\"a b\"@example.com"),
        Err(MailAddressError::LocalPart(
            MailLocalPartError::IllegalCharacter { .. }
        ))
    ));
}

#[test]
fn an_address_literal_domain_is_refused() {
    assert!(matches!(
        MailAddress::parse("user@[127.0.0.1]"),
        Err(MailAddressError::Domain(_))
    ));
    // `localhost` is a reserved host name, which the domain check refuses before it
    // counts labels; a name that is merely one label is the single-label refusal.
    assert_eq!(
        MailAddress::parse("user@localhost"),
        Err(MailAddressError::Domain(MailDomainError::Invalid(
            DomainError::LocalhostFamily
        )))
    );
    assert_eq!(
        MailAddress::parse("user@intranet"),
        Err(MailAddressError::Domain(MailDomainError::SingleLabel))
    );
}

#[test]
fn an_address_longer_than_the_rfc_ceiling_is_refused_before_it_is_split() {
    let candidate = format!("{}@example.com", "a".repeat(300));

    assert_eq!(
        MailAddress::parse(&candidate),
        Err(MailAddressError::TooLong { maximum: 254 })
    );
}

#[test]
fn a_newline_that_would_start_another_map_line_is_refused() {
    assert!(MailAddress::parse("a@example.com\nb@example.com").is_err());
}
