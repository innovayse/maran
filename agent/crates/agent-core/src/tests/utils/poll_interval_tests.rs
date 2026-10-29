//! Tests for [`PollInterval`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use crate::utils::poll_interval::PollInterval;

/// The gap starts at one millisecond, doubles, and stops growing at twenty.
#[test]
fn the_gap_doubles_from_one_millisecond_up_to_twenty() {
    let mut interval = PollInterval::new();
    let naps: Vec<Duration> = (0..8).map(|_| interval.next_nap()).collect();
    let expected: Vec<Duration> = [1, 2, 4, 8, 16, 20, 20, 20]
        .iter()
        .map(|millis| Duration::from_millis(*millis))
        .collect();

    assert_eq!(naps, expected);
}
