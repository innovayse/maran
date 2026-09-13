//! `UnreadableReason` is a plain, comparable enum with a stable JSON shape.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// `Corrupt` and `UnknownVersion` are distinct values, not two names for the
/// same fact — the whole point of the type is that callers can tell them
/// apart.
#[test]
fn corrupt_and_unknown_version_are_not_equal() {
    assert_ne!(
        UnreadableReason::Corrupt,
        UnreadableReason::UnknownVersion { version: 0 }
    );
}

/// Two `UnknownVersion`s compare equal only when their version does.
#[test]
fn unknown_version_compares_by_its_version() {
    assert_eq!(
        UnreadableReason::UnknownVersion { version: 3 },
        UnreadableReason::UnknownVersion { version: 3 }
    );
    assert_ne!(
        UnreadableReason::UnknownVersion { version: 3 },
        UnreadableReason::UnknownVersion { version: 4 }
    );
}

/// `UnknownVersion` round-trips its version through JSON.
#[test]
fn unknown_version_round_trips_its_version_through_json() {
    let reason = UnreadableReason::UnknownVersion { version: 9 };

    let json = serde_json::to_string(&reason).expect("the reason serialises");
    let parsed: UnreadableReason = serde_json::from_str(&json).expect("the reason parses back");

    assert_eq!(parsed, reason);
}
