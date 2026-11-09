//! Tests for the `mail_alias_targets` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_TARGETS, MailAliasTargets};
use crate::validation::mail::mail_address_error::MailAddressError;
use crate::validation::mail::mail_alias_targets_error::MailAliasTargetsError;

#[test]
fn targets_are_kept_in_the_order_given() {
    let targets = MailAliasTargets::parse(&["b@example.com", "a@example.org"]).unwrap();
    let addresses: Vec<String> = targets.as_slice().iter().map(|a| a.to_address()).collect();

    assert_eq!(addresses, ["b@example.com", "a@example.org"]);
}

#[test]
fn an_empty_list_is_refused() {
    let none: [&str; 0] = [];

    assert_eq!(
        MailAliasTargets::parse(&none),
        Err(MailAliasTargetsError::Empty)
    );
}

#[test]
fn the_ceiling_is_accepted_and_one_more_is_refused_before_any_address_is_read() {
    let ok: Vec<String> = (0..MAXIMUM_TARGETS)
        .map(|i| format!("u{i}@example.com"))
        .collect();
    assert!(MailAliasTargets::parse(&ok).is_ok());

    // The extra entry is not even an address: the count is checked first.
    let mut too_many = ok;
    too_many.push("not an address".to_owned());
    assert_eq!(
        MailAliasTargets::parse(&too_many),
        Err(MailAliasTargetsError::TooMany {
            maximum: MAXIMUM_TARGETS
        })
    );
}

#[test]
fn the_first_invalid_target_is_named_by_position() {
    assert_eq!(
        MailAliasTargets::parse(&["a@example.com", "oops"]),
        Err(MailAliasTargetsError::InvalidTarget {
            index: 1,
            source: MailAddressError::MissingAtSign
        })
    );
}

#[test]
fn a_repeat_is_refused_even_when_spelled_in_another_case() {
    assert_eq!(
        MailAliasTargets::parse(&["a@example.com", "b@example.com", "A@Example.COM"]),
        Err(MailAliasTargetsError::Duplicate { index: 2 })
    );
}
