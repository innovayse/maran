//! Tests for the wire code every certificate failure travels as.
//!
//! One assertion per variant of `SslOpError`, because the codes are a
//! CONTRACT — `ssl.proto` names them to callers — and because
//! `rules/testing.md` requires every typed error variant to appear in at least
//! one test. Before this file four of the thirteen variants appeared nowhere
//! in the suite, `ReloadFailed` among them.
//!
//! The last test is about the claim `ssl_status.rs` makes that nothing it
//! produces can carry private key material. It pins the half of that claim
//! this file can see — the mapping adds no text of its own — and says in the
//! test which half it cannot (rules/security.md item 8).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::ssl::SslOpError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// A key-shaped string, used to show the mapping neither adds to nor moves
/// whatever a variant was built holding.
const SECRET: &str = "-----BEGIN PRIVATE KEY-----MIIEvQIBADANBg-----END PRIVATE KEY-----";

/// The code `error` is reported as.
fn code_of(error: &SslOpError) -> i32 {
    to_agent_error(error).code
}

/// Every variant, each built with the secret in whatever field it has.
fn every_variant_carrying(text: &str) -> Vec<SslOpError> {
    vec![
        SslOpError::KeyDoesNotMatchCertificate,
        SslOpError::MalformedCertificate {
            reason: text.to_owned(),
        },
        SslOpError::MalformedPrivateKey,
        SslOpError::ExpiryUnreadable {
            reason: text.to_owned(),
        },
        SslOpError::ToolUnavailable {
            reason: text.to_owned(),
        },
        SslOpError::SiteNotFound {
            domain: text.to_owned(),
        },
        SslOpError::NotFound {
            domain: text.to_owned(),
        },
        SslOpError::AlreadyExists {
            domain: text.to_owned(),
        },
        SslOpError::MaterialWrite {
            reason: text.to_owned(),
        },
        SslOpError::NginxValidation {
            stderr: text.to_owned(),
        },
        SslOpError::ReloadFailed {
            stderr: text.to_owned(),
        },
        SslOpError::Render {
            reason: text.to_owned(),
        },
        SslOpError::ConfigWrite {
            reason: text.to_owned(),
        },
    ]
}

#[test]
fn material_the_caller_sent_that_does_not_hold_together_is_invalid_input() {
    // Nothing was written and no retry of the same bytes will do better, so
    // this is the request being wrong rather than the machine.
    for error in [
        SslOpError::KeyDoesNotMatchCertificate,
        SslOpError::MalformedCertificate {
            reason: "no PEM header".to_owned(),
        },
        SslOpError::MalformedPrivateKey,
    ] {
        assert_eq!(code_of(&error), ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn a_missing_certificate_and_a_missing_site_are_both_not_found_so_a_retry_reads_as_done() {
    for error in [
        SslOpError::NotFound {
            domain: "example.com".to_owned(),
        },
        SslOpError::SiteNotFound {
            domain: "example.com".to_owned(),
        },
    ] {
        assert_eq!(code_of(&error), ErrorCode::NotFound as i32);
    }
}

#[test]
fn a_certificate_already_in_place_is_already_exists_and_never_a_silent_replacement() {
    assert_eq!(
        code_of(&SslOpError::AlreadyExists {
            domain: "example.com".to_owned(),
        }),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn a_refused_vhost_is_a_validation_failure_because_the_site_is_back_on_its_old_config() {
    assert_eq!(
        code_of(&SslOpError::NginxValidation {
            stderr: "nginx: [emerg] cannot load certificate".to_owned(),
        }),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn every_fault_of_this_machine_is_a_system_failure_and_none_of_them_is_reported_as_not_found() {
    for error in [
        SslOpError::ReloadFailed {
            stderr: "job for nginx.service failed".to_owned(),
        },
        SslOpError::ExpiryUnreadable {
            reason: "openssl printed nothing".to_owned(),
        },
        SslOpError::ToolUnavailable {
            reason: "openssl is not on this host".to_owned(),
        },
        SslOpError::MaterialWrite {
            reason: "no space left on device".to_owned(),
        },
        SslOpError::Render {
            reason: "template not found".to_owned(),
        },
        SslOpError::ConfigWrite {
            reason: "no space left on device".to_owned(),
        },
    ] {
        assert_eq!(code_of(&error), ErrorCode::SystemFailure as i32);
    }
}

#[test]
fn only_the_two_failures_that_ran_a_tool_carry_that_tools_output() {
    assert_eq!(
        to_agent_error(&SslOpError::NginxValidation {
            stderr: "nginx: configuration file test failed".to_owned(),
        })
        .tool_output,
        "nginx: configuration file test failed"
    );
    assert_eq!(
        to_agent_error(&SslOpError::ReloadFailed {
            stderr: "job for nginx.service failed".to_owned(),
        })
        .tool_output,
        "job for nginx.service failed"
    );

    for error in every_variant_carrying("plain text") {
        let rendered = to_agent_error(&error);
        let ran_a_tool = matches!(
            error,
            SslOpError::NginxValidation { .. } | SslOpError::ReloadFailed { .. }
        );
        assert_eq!(rendered.tool_output.is_empty(), !ran_a_tool);
    }
}

#[test]
fn the_mapping_adds_no_text_of_its_own_to_any_variant() {
    // What this DOES prove, and the limit of it, stated plainly rather than
    // left for a reader to assume.
    //
    // `ssl_status.rs` says nothing it produces can carry private key material.
    // That guarantee has two halves and only one of them lives here. This half
    // is that the mapping is a pure relabelling: the message is exactly the
    // failure's own `Display` and the tool output is exactly the `stderr` field
    // where the variant has one, so the mapping can neither add material nor
    // move it between fields. The OTHER half — that no variant is ever
    // constructed holding key material in the first place — is a property of
    // the ops crate and of the callers that build these values, and no test in
    // this file can see it. Driving every variant with a key-shaped string here
    // would only prove that `Display` prints what it was given, which it does.
    for error in every_variant_carrying(SECRET) {
        let rendered = to_agent_error(&error);
        assert_eq!(rendered.message, error.to_string());
        assert!(
            rendered.tool_output.is_empty() || error.to_string().contains(&rendered.tool_output)
        );
    }
}
