//! Tests for the `mail_local_part` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MailLocalPart;
use crate::validation::mail::mail_local_part_error::MailLocalPartError;

#[test]
fn a_local_part_is_lowercased_so_two_cases_are_not_two_mailboxes() {
    assert_eq!(MailLocalPart::parse("Info").unwrap().as_str(), "info");
}

#[test]
fn a_plus_is_refused_because_the_mta_would_split_the_address_there() {
    assert_eq!(
        MailLocalPart::parse("a+b"),
        Err(MailLocalPartError::IllegalCharacter { character: '+' })
    );
}

#[test]
fn a_colon_that_would_start_a_new_login_database_field_is_refused() {
    assert_eq!(
        MailLocalPart::parse("a:b"),
        Err(MailLocalPartError::IllegalCharacter { character: ':' })
    );
}

#[test]
fn a_path_traversal_attempt_is_refused() {
    assert!(MailLocalPart::parse("../etc").is_err());
    assert!(MailLocalPart::parse("a/b").is_err());
}
