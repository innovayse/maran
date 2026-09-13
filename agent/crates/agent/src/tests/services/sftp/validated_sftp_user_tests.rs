//! Tests for the login name every SFTP rpc is rebuilt into.
//!
//! Two of the three rpcs this feeds re-credential or revoke a login, so a name
//! forwarded off the wire would be one customer taking over another customer's
//! file access. The name is built from the account the panel authorised and can
//! be built no other way.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_sftp_user;
use crate::proto::ErrorCode;

#[test]
fn the_login_is_rebuilt_under_the_account_the_panel_authorised() {
    let (account, user) = validated_sftp_user("alice", "web").expect("valid");

    assert_eq!(account.as_str(), "alice");
    assert_eq!(user.as_str(), "alice_web");
}

#[test]
fn a_suffix_naming_another_tenants_login_is_refused_rather_than_forwarded() {
    let refused = validated_sftp_user("alice", "bob_web").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_fully_qualified_name_off_the_wire_cannot_be_used_as_the_suffix() {
    // The separator is outside the suffix alphabet, so "bob_web" cannot arrive
    // as a whole name; a caller that tries gets a refusal, never bob's login.
    assert!(validated_sftp_user("alice", "alice_web").is_err());
}

#[test]
fn an_account_name_the_agent_will_not_accept_is_refused() {
    let refused = validated_sftp_user("", "web").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
