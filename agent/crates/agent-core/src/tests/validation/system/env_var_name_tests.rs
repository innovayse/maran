//! Tests for the `env_var_name` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{EnvVarName, EnvVarNameError, MAX_LENGTH, RESERVED_NAMES};

#[test]
fn the_names_a_customer_actually_sets_are_accepted() {
    for candidate in ["PATH", "TZ", "CRON_TZ", "_", "_A1", "A", "APP_ENV2"] {
        assert_eq!(EnvVarName::parse(candidate).unwrap().as_str(), candidate);
    }
}

#[test]
fn mailto_and_shell_are_refused_as_reserved() {
    for name in RESERVED_NAMES {
        assert_eq!(
            EnvVarName::parse(name),
            Err(EnvVarNameError::ReservedName {
                name: name.to_owned()
            })
        );
    }

    // The two the denylist is written for, named rather than only iterated, so
    // that shrinking the list cannot quietly shrink this test with it.
    assert!(EnvVarName::parse("MAILTO").is_err());
    assert!(EnvVarName::parse("SHELL").is_err());
}

#[test]
fn the_grammar_is_enforced() {
    assert_eq!(EnvVarName::parse(""), Err(EnvVarNameError::Empty));

    assert_eq!(
        EnvVarName::parse("1PATH"),
        Err(EnvVarNameError::LeadingDigit)
    );

    for (candidate, character) in [
        ("path", 'p'),
        ("PA-TH", '-'),
        ("PA TH", ' '),
        ("PA=TH", '='),
        ("PA\nTH", '\n'),
        ("PÄTH", 'Ä'),
    ] {
        assert_eq!(
            EnvVarName::parse(candidate),
            Err(EnvVarNameError::IllegalCharacter { character })
        );
    }

    let longest = "A".repeat(MAX_LENGTH);
    assert_eq!(EnvVarName::parse(&longest).unwrap().as_str(), longest);

    let overlong = "A".repeat(MAX_LENGTH + 1);
    assert_eq!(
        EnvVarName::parse(&overlong),
        Err(EnvVarNameError::TooLong {
            maximum: MAX_LENGTH
        })
    );
}

#[test]
fn a_lowercase_mailto_is_refused_by_the_alphabet_before_the_denylist_sees_it() {
    // Cron matches these names exactly, so `mailto` would not be the reserved
    // name at all — the uppercase-only alphabet is what makes the denylist a
    // complete answer rather than a partial one.
    assert_eq!(
        EnvVarName::parse("mailto"),
        Err(EnvVarNameError::IllegalCharacter { character: 'm' })
    );
}
