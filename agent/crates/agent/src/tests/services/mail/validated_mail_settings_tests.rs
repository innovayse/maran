//! Revalidating what an `EnableMail` request carries.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::proto::{EnableMailRequest, ErrorCode};
use crate::services::mail::validated_mail_settings::validated_mail_settings;

/// A request the agent accepts, to be spoiled one field at a time.
fn request() -> EnableMailRequest {
    EnableMailRequest {
        hostname: "mail.example.test".to_owned(),
        pop3_enabled: true,
        antivirus_enabled: false,
        message_size_mib: 50,
        spam_greylist_score: 4,
        spam_add_header_score: 6,
        spam_reject_score: 15,
        allow_external_forwarding: false,
        webmail_master_hash: String::new(),
    }
}

#[test]
fn a_well_formed_request_becomes_the_settings_it_asks_for() {
    let settings = validated_mail_settings(&request()).unwrap();

    assert_eq!(settings.hostname.as_str(), "mail.example.test");
    assert!(settings.pop3_enabled);
    assert_eq!(settings.message_size.value(), 50);
    assert_eq!(settings.spam.greylist(), 4);
    assert_eq!(settings.spam.add_header(), 6);
    assert_eq!(settings.spam.reject(), 15);
}

#[test]
fn a_hostname_with_a_newline_is_refused_before_it_can_reach_a_file() {
    let mut spoiled = request();
    spoiled.hostname = "mail.example.test\nmydestination = evil".to_owned();

    let error = validated_mail_settings(&spoiled).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(error.message.starts_with("hostname"));
}

#[test]
fn a_hostname_of_one_label_is_refused() {
    let mut spoiled = request();
    spoiled.hostname = "localhost".to_owned();

    assert_eq!(
        validated_mail_settings(&spoiled).unwrap_err().code,
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_message_size_of_zero_is_refused_and_not_read_as_no_limit() {
    let mut spoiled = request();
    spoiled.message_size_mib = 0;

    let error = validated_mail_settings(&spoiled).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(error.message.starts_with("message size"));
}

#[test]
fn spam_scores_out_of_order_are_refused() {
    let mut spoiled = request();
    spoiled.spam_add_header_score = 3;

    let error = validated_mail_settings(&spoiled).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(error.message.starts_with("spam scores"));
}

#[test]
fn each_option_this_build_cannot_carry_out_is_refused_when_switched_on() {
    let antivirus = EnableMailRequest {
        antivirus_enabled: true,
        ..request()
    };
    let forwarding = EnableMailRequest {
        allow_external_forwarding: true,
        ..request()
    };
    let webmail = EnableMailRequest {
        webmail_master_hash: "$argon2id$secret".to_owned(),
        ..request()
    };

    for spoiled in [antivirus, forwarding, webmail] {
        let error = validated_mail_settings(&spoiled).unwrap_err();
        assert_eq!(error.code, ErrorCode::NotImplemented as i32);
    }
}

#[test]
fn a_refusal_never_quotes_the_webmail_secret() {
    let webmail = EnableMailRequest {
        webmail_master_hash: "$argon2id$v=19$very-secret-hash".to_owned(),
        ..request()
    };

    let error = validated_mail_settings(&webmail).unwrap_err();

    assert!(!error.message.contains("very-secret-hash"));
}

#[test]
fn an_invalid_field_is_reported_ahead_of_an_unsupported_option() {
    let mut spoiled = request();
    spoiled.antivirus_enabled = true;
    spoiled.message_size_mib = 0;

    assert_eq!(
        validated_mail_settings(&spoiled).unwrap_err().code,
        ErrorCode::InvalidInput as i32
    );
}
