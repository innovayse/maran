//! Tests for the `basic_auth_user_name` module.
//!
//! The name is the first field of a `name:hash` line, so the colon and the
//! newline are the two values this type exists to refuse.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{BasicAuthUserName, BasicAuthUserNameError, MAXIMUM_LENGTH};

#[test]
fn ordinary_names_are_accepted() {
    for name in [
        "alice",
        "A",
        "deploy-bot",
        "j.doe_2",
        "x".repeat(MAXIMUM_LENGTH).as_str(),
    ] {
        assert_eq!(BasicAuthUserName::parse(name).unwrap().as_str(), name);
    }
}

#[test]
fn an_empty_name_is_refused() {
    assert_eq!(
        BasicAuthUserName::parse(""),
        Err(BasicAuthUserNameError::Empty)
    );
}

#[test]
fn a_name_over_the_limit_is_refused() {
    let name = "x".repeat(MAXIMUM_LENGTH + 1);

    assert_eq!(
        BasicAuthUserName::parse(&name),
        Err(BasicAuthUserNameError::TooLong {
            length: MAXIMUM_LENGTH + 1,
            maximum: MAXIMUM_LENGTH,
        })
    );
}

#[test]
fn a_control_character_is_refused_before_anything_else() {
    // The newline would end the credential line and start another user.
    for name in ["alice\nmallory", "alice\r", "al\0ice", "a\tb"] {
        assert_eq!(
            BasicAuthUserName::parse(name),
            Err(BasicAuthUserNameError::ControlCharacter),
            "{name:?}"
        );
    }
}

#[test]
fn the_colon_and_every_other_character_outside_the_alphabet_is_refused() {
    for name in [
        "al:ice", "alice ", "al ice", "alice;", "al\"ice", "ali$ce", "álice", "a/b",
    ] {
        assert_eq!(
            BasicAuthUserName::parse(name),
            Err(BasicAuthUserNameError::ForbiddenCharacter),
            "{name:?}"
        );
    }
}
