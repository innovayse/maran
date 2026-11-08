//! Tests for the `mailbox_quota_mib` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM, MailboxQuotaMib};
use crate::validation::mail::mailbox_quota_mib_error::MailboxQuotaMibError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(MailboxQuotaMib::parse(1).unwrap().value(), 1);
    assert_eq!(MailboxQuotaMib::parse(MAXIMUM).unwrap().value(), MAXIMUM);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(MailboxQuotaMib::parse(0), Err(MailboxQuotaMibError::Zero));
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    assert_eq!(
        MailboxQuotaMib::parse(MAXIMUM + 1),
        Err(MailboxQuotaMibError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM,
        })
    );
}
