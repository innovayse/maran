//! Tests for the `sftp_user_name` module.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md); the
//! source file declares this module with `#[path]`, keeping it a child able to
//! reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::system::name::AccountName;
use crate::validation::system::sftp_user_name_error::SftpUserNameError;

use super::SftpUserName;

fn account() -> AccountName {
    AccountName::parse("acme").expect("a valid account name")
}

#[test]
fn a_valid_request_is_prefixed_with_the_owning_account() {
    let name = SftpUserName::for_account(&account(), "deploy").expect("a valid request");

    assert_eq!(name.as_str(), "acme_deploy");
}

#[test]
fn an_underscore_in_the_request_is_rejected_so_ownership_cannot_be_forged() {
    let result = SftpUserName::for_account(&account(), "bob_deploy");

    assert!(matches!(
        result,
        Err(SftpUserNameError::UnexpectedCharacter { character: '_' })
    ));
}

#[test]
fn an_empty_request_is_rejected() {
    assert!(matches!(
        SftpUserName::for_account(&account(), ""),
        Err(SftpUserNameError::Empty)
    ));
}

#[test]
fn a_request_that_overflows_the_useradd_limit_is_rejected() {
    // "acme" + "_" + 28 = 33 bytes, one past the 32-byte useradd limit.
    let result = SftpUserName::for_account(&account(), &"a".repeat(28));

    assert!(matches!(
        result,
        Err(SftpUserNameError::TooLong { length: 33 })
    ));
}

#[test]
fn every_character_that_would_forge_an_sshd_directive_or_a_path_is_rejected() {
    // sshd_config is line-oriented, so a newline in this value appends directives of the
    // caller's choosing to the SSH daemon's configuration (rules/security.md §4). The name
    // also becomes a home-directory path segment. Without this test the allow-list could be
    // widened by one character unnoticed.
    for requested in [
        "deploy\nMatch User root",
        "deploy\r",
        "deploy ",
        "deploy:",
        "deploy/../root",
        "deploy'",
        "Deploy",
        "deploy-1",
    ] {
        assert!(
            SftpUserName::for_account(&account(), requested).is_err(),
            "{requested:?} must be rejected"
        );
    }
}

#[test]
fn a_request_that_exactly_fills_the_useradd_limit_is_accepted() {
    // "acme" + "_" + 27 = 32 bytes, exactly the useradd limit. Without this the ceiling's `>`
    // could become `>=` with no named test noticing.
    let name = SftpUserName::for_account(&account(), &"a".repeat(27)).expect("a valid request");

    assert_eq!(name.as_str().len(), 32);
}

#[test]
fn an_accepted_name_satisfies_useradds_own_name_regex() {
    // useradd's NAME_REGEX is `[a-z_][a-z0-9_-]*`. The prefix is an AccountName, which already
    // begins with a lowercase letter, and every character after it is [a-z0-9_].
    let name = SftpUserName::for_account(&account(), "deploy2").expect("a valid request");
    let mut characters = name.as_str().chars();
    let first = characters.next().expect("a non-empty name");

    assert!(first.is_ascii_lowercase() || first == '_');
    assert!(characters.all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '_'));
}
