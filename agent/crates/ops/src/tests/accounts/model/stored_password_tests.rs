//! Tests for the `stored_password` module.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::accounts::StoredPassword;

/// A shadow field that a real hash would look like, on either family.
const SHA512_HASH: &str = "$6$ou076yYZZcNDB1l3$9ClrBhB8kkMnQbmP16aIMTk3c0NcW..K8AxyIxIvLPY";

/// A field made only of lock markers holds no password on either family.
///
/// `!` is what `useradd` leaves on the Debian family and `!!` what it leaves on
/// the RHEL one, both measured; they are the SAME fact and the classification
/// must not distinguish them.
#[test]
fn a_field_of_lock_markers_alone_is_a_login_with_no_password() {
    assert_eq!(StoredPassword::classify("!"), StoredPassword::Absent);
    assert_eq!(StoredPassword::classify("!!"), StoredPassword::Absent);
    assert_eq!(StoredPassword::classify("*"), StoredPassword::Absent);
    assert_eq!(StoredPassword::classify("!*"), StoredPassword::Absent);
}

/// A hash behind a lock marker is a suspension with something to reverse.
///
/// This is the inverse control for the test above, and it is the whole defect:
/// `passwd -S` reports `L`/`LK` for both rows, so a check written against it
/// cannot separate them and `usermod --unlock` treats them completely
/// differently.
#[test]
fn a_hash_behind_a_lock_marker_is_locked_and_not_absent() {
    let field = format!("!{SHA512_HASH}");

    assert_eq!(StoredPassword::classify(&field), StoredPassword::Locked);
    assert_ne!(StoredPassword::classify(&field), StoredPassword::Absent);
}

/// A bare hash is a login a password can authenticate.
#[test]
fn a_bare_hash_is_a_usable_password() {
    assert_eq!(
        StoredPassword::classify(SHA512_HASH),
        StoredPassword::Usable
    );
    assert_eq!(
        StoredPassword::classify("$y$j9T$SOVNYq3GfgzsQt.4Ofx9j0$IcjlX1ZYX0yI6EAfCynS"),
        StoredPassword::Usable
    );
}

/// An empty field is not "no password": it is a login that needs none.
///
/// Kept separate from [`StoredPassword::Absent`] because it is the state
/// `passwd -u -f` leaves behind on the RHEL family, which is why that command
/// is not used to reverse a suspension.
#[test]
fn an_empty_field_is_a_login_that_authenticates_with_no_password() {
    assert_eq!(StoredPassword::classify(""), StoredPassword::Empty);
}

/// Only the two states holding no lock marker let a password in.
#[test]
fn exactly_the_empty_and_usable_states_can_authenticate() {
    assert!(StoredPassword::Empty.can_authenticate());
    assert!(StoredPassword::Usable.can_authenticate());
    assert!(!StoredPassword::Absent.can_authenticate());
    assert!(!StoredPassword::Locked.can_authenticate());
}

/// An unrecognised field is read as a password rather than as nothing.
///
/// The conservative direction: treating an unfamiliar format as "nothing worth
/// unlocking" would let a real credential be skipped over silently.
#[test]
fn an_unrecognised_field_is_treated_as_a_password_that_exists() {
    assert_eq!(
        StoredPassword::classify("something-unfamiliar"),
        StoredPassword::Usable
    );
}
