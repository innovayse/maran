//! Tests for the agent's own check of a panel challenge request.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::ValidatedPanelChallenge;
use crate::proto::{ErrorCode, WritePanelChallengeRequest};

fn request(token: &str, key_authorization: &str) -> WritePanelChallengeRequest {
    WritePanelChallengeRequest {
        token: token.to_owned(),
        key_authorization: key_authorization.to_owned(),
    }
}

#[test]
fn a_token_and_its_key_authorization_are_accepted() {
    let validated = ValidatedPanelChallenge::parse(&request("tok_1", "tok_1.abc-DEF")).unwrap();

    assert_eq!(validated.token.as_str(), "tok_1");
    assert_eq!(validated.key_authorization.as_str(), "tok_1.abc-DEF");
}

#[test]
fn a_traversing_token_is_refused_as_invalid_input() {
    let refused = ValidatedPanelChallenge::parse(&request("../../etc/passwd", "x.y")).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_key_authorization_for_another_token_is_refused_as_invalid_input() {
    let refused = ValidatedPanelChallenge::parse(&request("tok_1", "tok_2.abc")).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
