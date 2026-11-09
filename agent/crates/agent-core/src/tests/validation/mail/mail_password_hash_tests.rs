//! Tests for the `mail_password_hash` module.
//!
//! The hash is a credential and a field of a colon-separated login database:
//! the shape check keeps a weak scheme, a colon or a newline out of it, and the
//! scheme parameter is what turns a hash this host cannot verify into a refusal.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MailPasswordHash;
use crate::validation::mail::mail_password_hash_error::MailPasswordHashError;
use crate::validation::mail::mail_password_scheme::MailPasswordScheme;
use crate::validation::web::bcrypt_hash_error::BcryptHashError;

/// An Argon2id string in the encoded form: 19 MiB, 2 passes, 1 lane, a 16-byte
/// salt (22 characters) and a 32-byte output (43 characters). Not the hash of
/// any password anybody uses.
const ARGON2ID: &str = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXowMTIzNDU";

/// A syntactically valid bcrypt hash, cost 12.
const BCRYPT: &str = "$2y$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXY./";

#[test]
fn the_fixtures_are_accepted_in_their_own_scheme() {
    // Guards the fixtures: every test below changes one thing about one.
    assert!(MailPasswordHash::parse(ARGON2ID, MailPasswordScheme::Argon2id).is_ok());
    assert!(MailPasswordHash::parse(BCRYPT, MailPasswordScheme::BlowfishCrypt).is_ok());
}

#[test]
fn an_argon2id_hash_is_refused_on_a_host_that_verifies_only_bcrypt() {
    assert_eq!(
        MailPasswordHash::parse(ARGON2ID, MailPasswordScheme::BlowfishCrypt),
        Err(MailPasswordHashError::SchemeNotVerifiedHere {
            expected: MailPasswordScheme::BlowfishCrypt
        })
    );
}

#[test]
fn a_bcrypt_hash_is_refused_on_a_host_that_verifies_argon2id() {
    assert_eq!(
        MailPasswordHash::parse(BCRYPT, MailPasswordScheme::Argon2id),
        Err(MailPasswordHashError::SchemeNotVerifiedHere {
            expected: MailPasswordScheme::Argon2id
        })
    );
}

#[test]
fn the_weak_schemes_are_unsupported_whatever_the_host() {
    for weak in [
        "{MD5}1a79a4d60de6718e8e5b326e338ae533",
        "{SHA}2fd4e1c67a2d28fced849ee1bb76e7391b93eb12",
        "$1$salt$hashhashhashhash",
        "$apr1$salt$hash",
        "$6$rounds=5000$salt$hash",
        "plaintext",
        "",
    ] {
        for scheme in [
            MailPasswordScheme::Argon2id,
            MailPasswordScheme::BlowfishCrypt,
        ] {
            assert_eq!(
                MailPasswordHash::parse(weak, scheme),
                Err(MailPasswordHashError::UnsupportedScheme),
                "{weak:?}"
            );
        }
    }
}

#[test]
fn a_control_character_is_refused_first_in_either_scheme() {
    let argon = format!("{ARGON2ID}\nroot:{ARGON2ID}");
    let bcrypt = format!("{BCRYPT}\nroot:{BCRYPT}");

    assert_eq!(
        MailPasswordHash::parse(&argon, MailPasswordScheme::Argon2id),
        Err(MailPasswordHashError::ControlCharacter)
    );
    assert_eq!(
        MailPasswordHash::parse(&bcrypt, MailPasswordScheme::BlowfishCrypt),
        Err(MailPasswordHashError::ControlCharacter)
    );
}

#[test]
fn a_hash_beyond_the_ceiling_is_refused() {
    let long = format!("{ARGON2ID}{}", "A".repeat(300));

    assert_eq!(
        MailPasswordHash::parse(&long, MailPasswordScheme::Argon2id),
        Err(MailPasswordHashError::TooLong { maximum: 255 })
    );
}

#[test]
fn an_argon2id_colon_or_space_in_a_field_is_refused() {
    for tampered in [
        ARGON2ID.replace("c29tZXNhbHRzb21lc2FsdA", "c29tZXNhbHRzb21lc2F:dA"),
        ARGON2ID.replace("c29tZXNhbHRzb21lc2FsdA", "c29tZXNhbHRzb21lc2F dA"),
        ARGON2ID.replace("YWJjZGVmZ2hp", "YWJjZGVmZ2h="),
    ] {
        assert_eq!(
            MailPasswordHash::parse(&tampered, MailPasswordScheme::Argon2id),
            Err(MailPasswordHashError::MalformedArgon2id),
            "{tampered}"
        );
    }
}

#[test]
fn an_argon2id_string_with_a_wrong_version_or_a_missing_or_extra_field_is_malformed() {
    for tampered in [
        ARGON2ID.replace("v=19", "v=16"),
        ARGON2ID.replace("$v=19", ""),
        format!("{ARGON2ID}$extra"),
        ARGON2ID.replace(",p=1", ""),
        ARGON2ID.replace("m=19456,t=2,p=1", "m=19456,p=1,t=2"),
        ARGON2ID.replace("m=19456", "m=+19456"),
        ARGON2ID.replace("m=19456", "m="),
        ARGON2ID.replace("m=19456", "m=99999999999"),
        ARGON2ID.replace("m=19456", "m=019456"),
        ARGON2ID.replace("t=2", "t=02"),
        ARGON2ID.replace("p=1", "p=00"),
    ] {
        assert_eq!(
            MailPasswordHash::parse(&tampered, MailPasswordScheme::Argon2id),
            Err(MailPasswordHashError::MalformedArgon2id),
            "{tampered}"
        );
    }
}

#[test]
fn the_argon2id_cost_range_is_enforced_at_both_ends() {
    for (replacement, accepted) in [
        ("m=19455,t=2,p=1", false),
        ("m=19456,t=2,p=1", true),
        ("m=1048576,t=10,p=16", true),
        ("m=1048577,t=2,p=1", false),
        ("m=19456,t=1,p=1", false),
        ("m=19456,t=11,p=1", false),
        ("m=19456,t=2,p=0", false),
        ("m=19456,t=2,p=17", false),
    ] {
        let hash = ARGON2ID.replace("m=19456,t=2,p=1", replacement);
        let result = MailPasswordHash::parse(&hash, MailPasswordScheme::Argon2id);

        if accepted {
            assert!(result.is_ok(), "{replacement}");
        } else {
            assert_eq!(
                result,
                Err(MailPasswordHashError::Argon2idCostOutOfRange),
                "{replacement}"
            );
        }
    }
}

#[test]
fn an_argon2id_salt_or_output_of_the_wrong_length_is_malformed() {
    let short_salt = ARGON2ID.replace("c29tZXNhbHRzb21lc2FsdA", "c29tZQ");
    let short_output = ARGON2ID.replace("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXowMTIzNDU", "YWJj");

    for tampered in [short_salt, short_output] {
        assert_eq!(
            MailPasswordHash::parse(&tampered, MailPasswordScheme::Argon2id),
            Err(MailPasswordHashError::MalformedArgon2id)
        );
    }
}

#[test]
fn a_bcrypt_hash_is_judged_by_the_bcrypt_reading() {
    let low_cost = BCRYPT.replace("$12$", "$04$");

    assert_eq!(
        MailPasswordHash::parse(&low_cost, MailPasswordScheme::BlowfishCrypt),
        Err(MailPasswordHashError::Bcrypt(
            BcryptHashError::CostOutOfRange {
                minimum: 10,
                maximum: 14
            }
        ))
    );
}

#[test]
fn the_scheme_prefix_is_added_in_the_form_the_login_database_stores() {
    let argon = MailPasswordHash::parse(ARGON2ID, MailPasswordScheme::Argon2id).unwrap();
    let bcrypt = MailPasswordHash::parse(BCRYPT, MailPasswordScheme::BlowfishCrypt).unwrap();

    assert_eq!(
        argon.with_scheme_prefix().expose(),
        format!("{{ARGON2ID}}{ARGON2ID}")
    );
    assert_eq!(
        bcrypt.with_scheme_prefix().expose(),
        format!("{{BLF-CRYPT}}{BCRYPT}")
    );
    assert_eq!(argon.expose(), ARGON2ID);
    assert_eq!(argon.scheme(), MailPasswordScheme::Argon2id);
}

#[test]
fn the_hash_never_appears_in_debug_output_nor_in_the_wrapped_form() {
    let hash = MailPasswordHash::parse(ARGON2ID, MailPasswordScheme::Argon2id).unwrap();

    assert!(!format!("{hash:?}").contains("c29tZXNhbHRz"));
    assert!(format!("{hash:?}").contains("redacted"));
    assert!(!format!("{:?}", hash.with_scheme_prefix()).contains("c29tZXNhbHRz"));
}
