//! Revalidating what a `SetLocalSubmission` request carries.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::proto::AgentError;
use crate::proto::{ErrorCode, SetLocalSubmissionRequest};
use crate::services::mail::validated_local_submission::validated_local_submission;

/// A request the agent accepts, to be spoiled one field at a time.
fn request() -> SetLocalSubmissionRequest {
    SetLocalSubmissionRequest {
        account_username: "alice".to_owned(),
        domains: vec!["example.test".to_owned()],
        send_limit_per_hour: 200,
        mailbox_logins: vec!["info@example.test".to_owned()],
    }
}

#[test]
fn a_well_formed_request_becomes_the_binding_it_asks_for() {
    let binding = validated_local_submission(&request(), |_| Ok(1001)).unwrap();

    assert_eq!(binding.account.as_str(), "alice");
    assert_eq!(binding.uid, 1001);
    assert_eq!(binding.domains[0].as_str(), "example.test");
    assert_eq!(binding.send_limit_per_hour.value(), 200);
    assert_eq!(binding.mailbox_logins[0].to_address(), "info@example.test");
}

#[test]
fn an_absent_send_limit_is_refused_rather_than_read_as_unlimited() {
    let mut spoiled = request();
    spoiled.send_limit_per_hour = 0;

    let error = validated_local_submission(&spoiled, |_| Ok(1001)).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_uid_comes_from_the_resolver_and_not_from_the_request() {
    let binding = validated_local_submission(&request(), |account| {
        assert_eq!(account.as_str(), "alice");
        Ok(4242)
    })
    .unwrap();

    assert_eq!(binding.uid, 4242);
}

#[test]
fn an_account_the_resolver_refuses_is_refused_with_the_resolvers_answer() {
    let error = validated_local_submission(&request(), |_| {
        Err(AgentError {
            code: ErrorCode::NotFound as i32,
            message: "the account has no system user".to_owned(),
            tool_output: String::new(),
        })
    })
    .unwrap_err();

    assert_eq!(error.code, ErrorCode::NotFound as i32);
}

#[test]
fn a_uid_outside_the_hosting_range_is_refused_whatever_the_resolver_says() {
    let error = validated_local_submission(&request(), |_| Ok(0)).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_invalid_account_name_is_refused_before_the_resolver_runs() {
    let mut spoiled = request();
    spoiled.account_username = "../root".to_owned();

    let error =
        validated_local_submission(&spoiled, |_| panic!("the resolver must not run")).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_domain_with_a_newline_is_refused_and_not_quoted() {
    let mut spoiled = request();
    spoiled.domains = vec!["example.test\nmallory example.test".to_owned()];

    let error = validated_local_submission(&spoiled, |_| Ok(1001)).unwrap_err();

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(error.message.starts_with("domain 0"));
    assert!(!error.message.contains("mallory"));
}
