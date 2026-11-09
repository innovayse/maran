//! Tests for the `mail_domain` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_LENGTH, MailDomain};
use crate::validation::mail::mail_domain_error::MailDomainError;
use crate::validation::web::domain_error::DomainError;

#[test]
fn an_ordinary_domain_is_accepted_and_lowercased() {
    assert_eq!(
        MailDomain::parse("Example.COM").unwrap().as_str(),
        "example.com"
    );
}

#[test]
fn a_single_label_is_refused_because_no_other_host_can_mail_it() {
    assert_eq!(MailDomain::parse("shop"), Err(MailDomainError::SingleLabel));
}

#[test]
fn what_the_web_domain_refuses_is_refused_with_its_reason() {
    assert_eq!(
        MailDomain::parse("a b.example.com"),
        Err(MailDomainError::Invalid(DomainError::IllegalCharacter {
            character: ' '
        }))
    );
    assert_eq!(
        MailDomain::parse("shop.localhost"),
        Err(MailDomainError::Invalid(DomainError::LocalhostFamily))
    );
    assert_eq!(
        MailDomain::parse("203.0.113.7"),
        Err(MailDomainError::Invalid(DomainError::NumericFinalLabel))
    );
}

#[test]
fn a_newline_that_would_end_a_map_line_is_refused() {
    assert!(MailDomain::parse("example.com\nevil.com").is_err());
}

#[test]
fn the_longest_domain_that_still_fits_an_address_is_accepted_and_a_longer_one_is_not() {
    let label = "a".repeat(63);
    let longest = format!(
        "{label}.{label}.{}",
        "b".repeat(MAXIMUM_LENGTH - 2 * 63 - 2)
    );
    assert_eq!(longest.len(), MAXIMUM_LENGTH);
    assert!(MailDomain::parse(&longest).is_ok());

    let too_long = format!("{longest}b");
    assert_eq!(
        MailDomain::parse(&too_long),
        Err(MailDomainError::TooLong {
            maximum: MAXIMUM_LENGTH
        })
    );
}
