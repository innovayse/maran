//! How an id floor is read out of a `login.defs`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::login_defs_floor;
use crate::utils::login_defs_floor_reading::LoginDefsFloorReading;

/// A set value is read, and comments and other keys around it are not.
#[test]
fn a_set_value_is_read() {
    let text = "# UID_MIN 5\nUID_MAX 60000\nUID_MIN\t\t500\nGID_MIN 600\n";

    assert_eq!(
        login_defs_floor(text, "UID_MIN"),
        LoginDefsFloorReading::Set(500)
    );
    assert_eq!(
        login_defs_floor(text, "GID_MIN"),
        LoginDefsFloorReading::Set(600)
    );
}

/// The last definition decides, as it does for `useradd`.
#[test]
fn the_last_definition_wins() {
    assert_eq!(
        login_defs_floor("UID_MIN 1000\nUID_MIN 2000\n", "UID_MIN"),
        LoginDefsFloorReading::Set(2000)
    );
}

/// An unusable deciding line is not answered with an older one: the host no longer means it.
#[test]
fn an_unusable_last_definition_is_not_shadowed_by_an_earlier_one() {
    assert_eq!(
        login_defs_floor("UID_MIN 2000\nUID_MIN 0\n", "UID_MIN"),
        LoginDefsFloorReading::Unusable
    );
}

/// A file that does not mention the key (or only comments it out, or only sets a relative) leaves it unset.
#[test]
fn a_file_without_the_key_leaves_it_unset() {
    for text in ["#UID_MIN 1000\nSYS_UID_MIN 100\n", "# UID_MIN 500\n", ""] {
        assert_eq!(
            login_defs_floor(text, "UID_MIN"),
            LoginDefsFloorReading::Unset,
            "{text:?}"
        );
    }
}

/// A value that is not a positive number is unusable: zero would put root in the customer range.
#[test]
fn zero_and_garbage_and_a_missing_value_are_unusable() {
    for text in ["UID_MIN 0\n", "UID_MIN abc\n", "UID_MIN\n", "UID_MIN -1\n"] {
        assert_eq!(
            login_defs_floor(text, "UID_MIN"),
            LoginDefsFloorReading::Unusable,
            "{text:?}"
        );
    }
}

/// Tabs and the carriage return of a file edited on Windows around the value are tolerated.
#[test]
fn tabs_and_carriage_returns_around_the_value_are_tolerated() {
    assert_eq!(
        login_defs_floor("UID_MIN\t\t\t 1500\r\n", "UID_MIN"),
        LoginDefsFloorReading::Set(1500)
    );
}
