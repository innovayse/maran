//! What the session matcher is built from, and what it refuses to be.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use super::LoginSessionMatcher;

fn login(suffix: &str) -> SftpUserName {
    SftpUserName::for_account(&AccountName::parse("alice").unwrap(), suffix).unwrap()
}

#[test]
fn the_pattern_names_the_login_between_sshds_title_and_its_two_endings() {
    let matcher = LoginSessionMatcher::for_login(&login("web"));

    assert_eq!(
        matcher.pattern(),
        r"^sshd(-session)?: alice_web( \[priv\]|@)"
    );
}

#[test]
fn the_pattern_is_anchored_so_a_command_line_that_only_mentions_the_title_is_not_matched() {
    let matcher = LoginSessionMatcher::for_login(&login("web"));

    assert!(matcher.pattern().starts_with('^'));
}

#[test]
fn the_login_name_is_followed_by_a_character_no_login_name_contains() {
    // `alice_web` must not match the sessions of `alice_webdav`: the tail starts
    // with a space or an `@`, neither of which is in `[a-z0-9_]`.
    let matcher = LoginSessionMatcher::for_login(&login("web"));

    assert!(matcher.pattern().ends_with(r"( \[priv\]|@)"));
}

#[test]
fn the_arguments_end_the_options_before_the_pattern() {
    let matcher = LoginSessionMatcher::for_login(&login("web"));

    assert_eq!(
        matcher.arguments("KILL"),
        vec![
            "--signal".to_owned(),
            "KILL".to_owned(),
            "--count".to_owned(),
            "--full".to_owned(),
            "--".to_owned(),
            matcher.pattern().to_owned(),
        ]
    );
}

#[test]
fn the_arguments_carry_no_uid_so_the_accounts_other_processes_are_out_of_reach() {
    // The login shares its account's uid: a uid in the argument vector would
    // end the account's other logins and the agent's own account-side children.
    let matcher = LoginSessionMatcher::for_login(&login("web"));

    assert!(
        !matcher
            .arguments("KILL")
            .iter()
            .any(|argument| argument.contains("uid"))
    );
}
