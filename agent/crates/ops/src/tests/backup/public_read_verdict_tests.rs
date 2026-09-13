//! The verdict's three states, and which one a destination may be saved on.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// Only a proven-private destination is acceptable.
///
/// Written as an exhaustive `match` over the three states rather than as three
/// separate assertions: a fourth state added later stops this compiling, which
/// is a better reminder than a test that silently keeps passing about the three
/// it knew.
#[test]
fn only_a_proven_private_destination_is_acceptable() {
    let verdicts = [
        PublicReadVerdict::Private,
        PublicReadVerdict::PubliclyReadable,
        PublicReadVerdict::Unproven {
            reason: "the endpoint could not be reached".to_owned(),
        },
    ];

    for verdict in verdicts {
        let expected = match verdict {
            PublicReadVerdict::Private => true,
            PublicReadVerdict::PubliclyReadable | PublicReadVerdict::Unproven { .. } => false,
        };

        assert_eq!(
            verdict.destination_is_acceptable(),
            expected,
            "{verdict:?} was judged wrongly"
        );
    }
}

/// An unproven verdict is not a private one, at the level of the type.
///
/// The distinction this pins is the whole reason the type is not a boolean: a
/// probe that could not be made says nothing about the bucket, and the only
/// place that can be enforced is where the two states are still separate
/// values.
#[test]
fn an_unproven_verdict_is_not_a_private_one() {
    let unproven = PublicReadVerdict::Unproven {
        reason: "connection refused".to_owned(),
    };

    assert_ne!(unproven, PublicReadVerdict::Private);
    assert!(!unproven.destination_is_acceptable());
}
