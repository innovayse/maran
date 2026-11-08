//! Tests for the `mail_password_scheme` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MailPasswordScheme;

#[test]
fn each_scheme_is_spelled_as_the_imap_server_spells_it() {
    assert_eq!(MailPasswordScheme::Argon2id.dovecot_name(), "ARGON2ID");
    assert_eq!(
        MailPasswordScheme::BlowfishCrypt.dovecot_name(),
        "BLF-CRYPT"
    );
}
