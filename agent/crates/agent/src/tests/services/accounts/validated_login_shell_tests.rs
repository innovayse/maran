//! Tests for the `validated_login_shell` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::accounts::LoginShell;

use super::validated_login_shell;
use crate::proto::{ErrorCode, LoginShell as WireLoginShell};

#[test]
fn unspecified_is_nologin_because_that_is_what_an_older_caller_always_got() {
    assert_eq!(
        validated_login_shell(WireLoginShell::Unspecified as i32),
        Ok(LoginShell::Nologin)
    );
}

#[test]
fn the_nologin_case_is_nologin() {
    assert_eq!(
        validated_login_shell(WireLoginShell::Nologin as i32),
        Ok(LoginShell::Nologin)
    );
}

#[test]
fn the_bash_case_is_bash() {
    assert_eq!(
        validated_login_shell(WireLoginShell::Bash as i32),
        Ok(LoginShell::Bash)
    );
}

#[test]
fn a_number_the_enum_does_not_know_is_refused_and_not_read_as_nologin() {
    let refused = validated_login_shell(99).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.starts_with("login_shell:"));
}
