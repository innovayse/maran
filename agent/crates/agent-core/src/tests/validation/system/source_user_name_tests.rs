//! Tests for the `source_user_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{SourceUserName, SourceUserNameError};

#[test]
fn ordinary_logins_are_accepted() {
    for candidate in ["root", "migrator", "_apt", "deploy-1", "a", "user_name-2"] {
        assert_eq!(
            SourceUserName::parse(candidate).unwrap().as_str(),
            candidate
        );
    }
}

#[test]
fn a_login_of_exactly_thirty_two_characters_is_accepted_and_thirty_three_is_not() {
    let longest = format!("a{}", "b".repeat(31));
    assert_eq!(SourceUserName::parse(&longest).unwrap().as_str(), longest);

    assert_eq!(
        SourceUserName::parse(&format!("{longest}b")),
        Err(SourceUserNameError::TooLong { limit: 32 })
    );
}

#[test]
fn an_empty_login_is_refused() {
    assert_eq!(SourceUserName::parse(""), Err(SourceUserNameError::Empty));
}

#[test]
fn a_leading_dash_digit_or_uppercase_letter_is_refused() {
    for candidate in ["-oProxyCommand=x", "-root", "1user", "Root", "9", "."] {
        assert_eq!(
            SourceUserName::parse(candidate),
            Err(SourceUserNameError::BadFirstCharacter),
            "{candidate}"
        );
    }
}

#[test]
fn characters_outside_the_login_grammar_are_refused() {
    for candidate in [
        "user name",
        "user@host",
        "user/../x",
        "user\n",
        "user\0",
        "useR",
        "user.name",
        "user;id",
        "utilisateur\u{e9}",
    ] {
        assert_eq!(
            SourceUserName::parse(candidate),
            Err(SourceUserNameError::IllegalCharacter),
            "{candidate:?}"
        );
    }
}

#[test]
fn a_dash_inside_the_login_is_accepted() {
    assert!(SourceUserName::parse("a-b").is_ok());
}
