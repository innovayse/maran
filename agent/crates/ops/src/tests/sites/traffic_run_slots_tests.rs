//! Tests for the host-wide admission count of traffic summaries.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::TrafficRunSlots;
use crate::sites::SitesOpError;

#[test]
fn slots_are_granted_up_to_the_count_and_refused_beyond_it() {
    let slots = TrafficRunSlots::new(2);

    let first = slots.try_acquire().unwrap();
    let _second = slots.try_acquire().unwrap();

    assert!(matches!(
        slots.try_acquire(),
        Err(SitesOpError::TrafficBusy)
    ));

    drop(first);
    assert!(
        slots.try_acquire().is_ok(),
        "dropping a grant gives the slot back"
    );
}

#[test]
fn no_slots_at_all_refuses_everything() {
    assert!(matches!(
        TrafficRunSlots::new(0).try_acquire(),
        Err(SitesOpError::TrafficBusy)
    ));
}
