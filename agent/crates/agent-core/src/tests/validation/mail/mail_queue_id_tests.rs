//! Tests for the `mail_queue_id` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MailQueueId;
use crate::validation::mail::mail_queue_id_error::MailQueueIdError;

#[test]
fn the_ids_the_mta_generates_are_accepted() {
    // A short id (ten hexadecimal digits) and a long one.
    assert_eq!(
        MailQueueId::parse("4F3A1B2C3D").unwrap().as_str(),
        "4F3A1B2C3D"
    );
    assert!(MailQueueId::parse("4bCd5R2xYqz1H8mQr").is_ok());
}

#[test]
fn the_all_selector_and_other_short_words_are_not_ids() {
    for word in ["ALL", "", "abc", "123456789"] {
        assert_eq!(
            MailQueueId::parse(word),
            Err(MailQueueIdError::WrongLength {
                minimum: 10,
                maximum: 25
            }),
            "{word}"
        );
    }
}

#[test]
fn an_id_beyond_the_ceiling_is_refused() {
    assert!(MailQueueId::parse(&"a".repeat(25)).is_ok());
    assert!(MailQueueId::parse(&"a".repeat(26)).is_err());
}

#[test]
fn an_option_a_separator_and_a_newline_are_refused() {
    for (candidate, character) in [
        ("-d0123456789", '-'),
        ("0123456789 ALL", ' '),
        ("0123456789\nALL", '\n'),
        ("0123456789/", '/'),
    ] {
        assert_eq!(
            MailQueueId::parse(candidate),
            Err(MailQueueIdError::IllegalCharacter { character }),
            "{candidate:?}"
        );
    }
}
