//! Tests for the `local_submission` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{LocalSubmission, MAXIMUM_DOMAINS, MAXIMUM_MAILBOX_LOGINS, MAXIMUM_UID, MINIMUM_UID};
use crate::validation::mail::local_submission_error::LocalSubmissionError;
use crate::validation::mail::send_limit_per_hour_error::SendLimitPerHourError;

const NONE: [&str; 0] = [];

#[test]
fn a_well_formed_binding_parses() {
    let binding = LocalSubmission::parse(
        "alice",
        1001,
        &["Example.test", "shop.example.test"],
        200,
        &["info@example.test"],
    )
    .unwrap();

    assert_eq!(binding.account.as_str(), "alice");
    assert_eq!(binding.uid, 1001);
    assert_eq!(binding.domains[0].as_str(), "example.test");
    assert_eq!(binding.domains.len(), 2);
    assert_eq!(binding.send_limit_per_hour.value(), 200);
    assert_eq!(binding.mailbox_logins[0].to_address(), "info@example.test");
}

#[test]
fn an_account_without_domains_or_mailboxes_is_accepted() {
    let binding = LocalSubmission::parse("alice", 1001, &NONE, 1, &NONE).unwrap();

    assert!(binding.domains.is_empty());
    assert!(binding.mailbox_logins.is_empty());
}

#[test]
fn the_uid_bounds_are_inclusive_and_root_is_refused() {
    assert!(LocalSubmission::parse("alice", MINIMUM_UID, &NONE, 1, &NONE).is_ok());
    assert!(LocalSubmission::parse("alice", MAXIMUM_UID, &NONE, 1, &NONE).is_ok());

    for uid in [0, 33, MINIMUM_UID - 1, MAXIMUM_UID + 1, 65_534, u32::MAX] {
        assert_eq!(
            LocalSubmission::parse("alice", uid, &NONE, 1, &NONE),
            Err(LocalSubmissionError::UidOutOfRange {
                minimum: MINIMUM_UID,
                maximum: MAXIMUM_UID,
            }),
            "uid {uid}"
        );
    }
}

#[test]
fn an_invalid_account_name_is_refused() {
    assert!(matches!(
        LocalSubmission::parse("Al ice\n", 1001, &NONE, 1, &NONE),
        Err(LocalSubmissionError::Account(_))
    ));
}

#[test]
fn a_zero_send_limit_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(
        LocalSubmission::parse("alice", 1001, &NONE, 0, &NONE),
        Err(LocalSubmissionError::SendLimit(SendLimitPerHourError::Zero))
    );
}

#[test]
fn a_domain_that_could_split_a_map_line_is_refused() {
    let error = LocalSubmission::parse(
        "alice",
        1001,
        &["example.test", "evil.test\nbob example.test"],
        10,
        &NONE,
    )
    .unwrap_err();

    assert!(matches!(
        error,
        LocalSubmissionError::InvalidDomain { index: 1, .. }
    ));
}

#[test]
fn a_domain_repeated_in_another_case_is_refused() {
    assert_eq!(
        LocalSubmission::parse("alice", 1001, &["example.test", "EXAMPLE.test"], 10, &NONE),
        Err(LocalSubmissionError::DuplicateDomain { index: 1 })
    );
}

#[test]
fn a_mailbox_login_that_is_not_an_address_is_refused() {
    let error = LocalSubmission::parse(
        "alice",
        1001,
        &NONE,
        10,
        &["info@example.test", "no-at-sign"],
    )
    .unwrap_err();

    assert!(matches!(
        error,
        LocalSubmissionError::InvalidMailboxLogin { index: 1, .. }
    ));
}

#[test]
fn a_mailbox_login_repeated_is_refused() {
    assert_eq!(
        LocalSubmission::parse(
            "alice",
            1001,
            &NONE,
            10,
            &["info@example.test", "INFO@example.test"]
        ),
        Err(LocalSubmissionError::DuplicateMailboxLogin { index: 1 })
    );
}

#[test]
fn the_list_ceilings_hold_before_any_element_is_read() {
    // Elements that would each be refused: the ceiling must answer first.
    let domains = vec!["not a domain"; MAXIMUM_DOMAINS + 1];
    assert_eq!(
        LocalSubmission::parse("alice", 1001, &domains, 10, &NONE),
        Err(LocalSubmissionError::TooManyDomains {
            maximum: MAXIMUM_DOMAINS
        })
    );

    let logins = vec!["not an address"; MAXIMUM_MAILBOX_LOGINS + 1];
    assert_eq!(
        LocalSubmission::parse("alice", 1001, &NONE, 10, &logins),
        Err(LocalSubmissionError::TooManyMailboxLogins {
            maximum: MAXIMUM_MAILBOX_LOGINS
        })
    );
}
