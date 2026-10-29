//! What the read-only status hostname check accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_hostname;
use crate::proto::ErrorCode;

#[test]
fn an_empty_hostname_is_an_answer_and_not_a_refusal() {
    assert!(validated_hostname("").expect("accepted").is_none());
}

#[test]
fn a_hostname_that_is_reserved_today_is_still_accepted_for_the_read_only_status() {
    for reserved in ["203.0.113.7", "localhost", "ftp.local"] {
        let domain = validated_hostname(reserved)
            .expect("a legacy name stays readable")
            .expect("present");

        assert_eq!(domain.as_str(), reserved);
    }
}

#[test]
fn a_malformed_hostname_is_still_refused_as_invalid_input() {
    let error = validated_hostname("bad_name.example.com")
        .map(|_| ())
        .expect_err("the shape check still applies");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}
