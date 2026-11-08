//! Tests for the UTC day a log timestamp falls on.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::TrafficDate;

fn day(year: i64, month: u32, day: u32, seconds: u32, offset: i64) -> String {
    TrafficDate::from_local(year, month, day, seconds, offset)
        .unwrap()
        .to_iso()
}

#[test]
fn the_epoch_is_the_first_of_january_1970() {
    assert_eq!(day(1970, 1, 1, 0, 0), "1970-01-01");
}

#[test]
fn an_ordinary_date_round_trips() {
    assert_eq!(day(2026, 11, 7, 36_733, 0), "2026-11-07");
}

#[test]
fn a_leap_day_is_a_day_and_the_next_one_follows_it() {
    assert_eq!(day(2024, 2, 29, 0, 0), "2024-02-29");
    assert_eq!(day(2024, 2, 29, 86_399, 0), "2024-02-29");
    assert_eq!(day(2024, 3, 1, 0, 0), "2024-03-01");
    assert_eq!(day(2100, 3, 1, 0, 0), "2100-03-01");
}

#[test]
fn half_past_midnight_east_of_utc_is_still_the_day_before_in_utc() {
    // 00:30 on the 8th at +0300 happened at 21:30 UTC on the 7th.
    assert_eq!(day(2026, 11, 8, 1_800, 3 * 3_600), "2026-11-07");
}

#[test]
fn half_past_eleven_west_of_utc_is_already_the_next_day_in_utc() {
    // 23:30 on the 7th at -0500 happened at 04:30 UTC on the 8th.
    assert_eq!(day(2026, 11, 7, 84_600, -5 * 3_600), "2026-11-08");
}

#[test]
fn a_year_boundary_is_crossed_by_the_offset() {
    assert_eq!(day(2027, 1, 1, 600, 3_600), "2026-12-31");
}

#[test]
fn dates_the_calendar_does_not_have_are_refused() {
    assert!(TrafficDate::from_local(2026, 0, 1, 0, 0).is_none());
    assert!(TrafficDate::from_local(2026, 13, 1, 0, 0).is_none());
    assert!(TrafficDate::from_local(2026, 1, 0, 0, 0).is_none());
    assert!(TrafficDate::from_local(2026, 1, 32, 0, 0).is_none());
    assert!(TrafficDate::from_local(2026, 1, 1, 86_400, 0).is_none());
}

#[test]
fn days_order_by_the_calendar() {
    let earlier = TrafficDate::from_local(2026, 11, 7, 0, 0).unwrap();
    let later = TrafficDate::from_local(2026, 11, 8, 0, 0).unwrap();

    assert!(earlier < later);
}
