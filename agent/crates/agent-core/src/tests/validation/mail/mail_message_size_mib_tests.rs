//! Tests for the `mail_message_size_mib` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM, MailMessageSizeMib};
use crate::validation::mail::mail_message_size_mib_error::MailMessageSizeMibError;

#[test]
fn a_figure_inside_the_range_parses() {
    assert_eq!(MailMessageSizeMib::parse(1).unwrap().value(), 1);
    assert_eq!(MailMessageSizeMib::parse(MAXIMUM).unwrap().value(), MAXIMUM);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    assert_eq!(
        MailMessageSizeMib::parse(0),
        Err(MailMessageSizeMibError::Zero)
    );
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    assert_eq!(
        MailMessageSizeMib::parse(MAXIMUM + 1),
        Err(MailMessageSizeMibError::TooLarge {
            value: MAXIMUM + 1,
            maximum: MAXIMUM,
        })
    );
}
