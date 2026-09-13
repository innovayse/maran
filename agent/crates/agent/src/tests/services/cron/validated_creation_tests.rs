//! The allowance a creation request carries survives the wire boundary.
//!
//! The three customer-supplied values are validated types and are covered by the
//! validators' own tests. What has no other observer is the fourth: `max_entries`
//! is the only thing this rpc carries that is neither validated nor transformed,
//! so a boundary that dropped it — or that turned an absent field into a number —
//! would compile, leave every other assertion in the tree green, and silently
//! disable the one protection that makes this module's plan limit atomic.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_creation;
use crate::proto::CronSchedule as WireSchedule;

/// A schedule the agent accepts, so no test here fails for the wrong reason.
fn nightly() -> WireSchedule {
    WireSchedule {
        minute: "30".to_owned(),
        hour: "3".to_owned(),
        day_of_month: "*".to_owned(),
        month: "*".to_owned(),
        day_of_week: "*".to_owned(),
    }
}

/// What the bundle answers for `max_entries`, for a request that is otherwise valid.
fn allowance_of(max_entries: Option<u32>) -> Option<u32> {
    let (_account, _schedule, _command, allowance) =
        validated_creation("alice", Some(&nightly()), "/usr/bin/true", max_entries)
            .expect("the request is otherwise valid");

    allowance
}

#[test]
fn a_stated_allowance_reaches_the_operation_unchanged() {
    assert_eq!(allowance_of(Some(7)), Some(7));
}

#[test]
fn an_allowance_of_zero_stays_a_stated_allowance_rather_than_becoming_absence() {
    // The sentinel this contract deliberately does not use. Zero is a real
    // allowance — a plan permitting no scheduled tasks at all — so a boundary
    // that collapsed it to `None` would give exactly that plan an unlimited one.
    assert_eq!(allowance_of(Some(0)), Some(0));
}

#[test]
fn an_unstated_allowance_stays_absent_rather_than_becoming_zero() {
    // The skew case. A caller predating the field sends nothing, and absence must
    // NOT be read as a number: as an allowance, zero means "allow nothing", so
    // reading it that way would refuse every cron entry such a caller ever asked
    // for.
    assert_eq!(allowance_of(None), None);
}
