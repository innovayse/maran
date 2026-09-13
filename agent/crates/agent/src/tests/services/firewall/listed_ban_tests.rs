//! Tests for the ban message a ban listing puts on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;
use maran_ops::firewall::ActiveBan;

use super::listed_ban;

/// One live ban with `expires_in`.
fn ban(expires_in: Option<Duration>) -> ActiveBan {
    ActiveBan {
        address: BanAddress::parse("198.51.100.7").expect("a valid address"),
        expires_in,
    }
}

#[test]
fn a_timed_ban_reports_the_seconds_it_has_left() {
    let wire = listed_ban(&ban(Some(Duration::from_secs(3600))));

    assert_eq!(wire.address, "198.51.100.7");
    assert_eq!(wire.expires_in_seconds, Some(3600));
}

#[test]
fn a_ban_with_no_timeout_reports_an_absence_and_not_a_zero() {
    // The distinction the explicit presence exists for. A 0 would be read as
    // "expiring this second", and the panel would stop reconciling a ban the
    // kernel intends to hold forever.
    let wire = listed_ban(&ban(None));

    assert_eq!(wire.expires_in_seconds, None);
}

#[test]
fn a_lifetime_too_large_for_the_field_saturates_rather_than_becoming_permanent() {
    // Absent means "permanent, reconcile it forever". Dropping an oversized
    // lifetime to absent would make the panel re-apply a ban after the kernel
    // had let it go; saturating keeps the answer wrong only in the direction
    // that expires.
    let wire = listed_ban(&ban(Some(Duration::from_secs(u64::from(u32::MAX) + 10))));

    assert_eq!(wire.expires_in_seconds, Some(u32::MAX));
}

#[test]
fn a_listed_ban_carries_neither_a_reason_nor_an_absolute_expiry() {
    // Both fields are deprecated in `firewall.proto` and both are unproduced:
    // the agent stores no reason at all, and what the kernel holds is a
    // remaining timeout rather than an instant. A 0 here is "unproduced", and
    // the test pins that the mapping does not invent either one.
    let wire = listed_ban(&ban(Some(Duration::from_secs(60))));

    assert_eq!(wire.reason, "");
    assert_eq!(wire.expires_at_unix, 0);
}
