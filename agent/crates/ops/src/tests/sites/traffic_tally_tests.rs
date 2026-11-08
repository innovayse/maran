//! Tests for the capped accumulator.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::TrafficTally;
use crate::sites::model::parsed_access_line::ParsedAccessLine;
use crate::sites::model::traffic_date::TrafficDate;
use crate::sites::traffic_budget::{
    MAXIMUM_DAYS, MAXIMUM_DISTINCT_PATHS, MAXIMUM_STATUS_CODES, RETURNED_PATHS,
};

fn date(day: u32) -> TrafficDate {
    TrafficDate::from_local(2026, 11, day, 0, 0).unwrap()
}

fn line(day: u32, status: u16, bytes: u64, path: Option<&str>) -> ParsedAccessLine {
    ParsedAccessLine {
        date: date(day),
        status,
        body_bytes: bytes,
        path: path.map(str::to_owned),
    }
}

#[test]
fn requests_bytes_statuses_and_paths_are_summed_per_day() {
    let mut tally = TrafficTally::new();
    tally.count(&line(7, 200, 100, Some("/a")));
    tally.count(&line(7, 200, 50, Some("/a")));
    tally.count(&line(7, 404, 10, Some("/b")));
    tally.count(&line(8, 200, 1, Some("/a")));

    let days = tally.into_days();

    assert_eq!(days.len(), 2);
    assert_eq!(days[0].date, date(7));
    assert_eq!(days[0].requests, 3);
    assert_eq!(days[0].body_bytes, 160);
    assert_eq!(
        days[0]
            .statuses
            .iter()
            .map(|s| (s.code, s.requests))
            .collect::<Vec<_>>(),
        vec![(200, 2), (404, 1)]
    );
    assert_eq!(days[0].paths[0].path, "/a");
    assert_eq!(days[0].paths[0].requests, 2);
    assert_eq!(days[0].other_path_requests, 0);
    assert_eq!(days[1].requests, 1);
}

#[test]
fn a_flood_of_distinct_paths_stops_growing_the_map_and_keeps_the_totals() {
    let mut tally = TrafficTally::new();
    tally.count(&line(7, 200, 1, Some("/hot")));

    let flood = MAXIMUM_DISTINCT_PATHS + 500;
    for index in 0..flood {
        tally.count(&line(7, 200, 1, Some(&format!("/scan/{index}"))));
    }
    // A path that already has a counter keeps adding after the cap.
    tally.count(&line(7, 200, 1, Some("/hot")));
    tally.count(&line(7, 200, 1, Some("/hot")));

    let days = tally.into_days();
    let day = &days[0];
    let total = 1 + flood as u64 + 2;

    assert_eq!(day.requests, total, "the totals stay exact");
    assert_eq!(day.paths.len(), RETURNED_PATHS);
    assert_eq!(day.paths[0].path, "/hot");
    assert_eq!(day.paths[0].requests, 3);
    let listed: u64 = day.paths.iter().map(|p| p.requests).sum();
    assert_eq!(day.other_path_requests, total - listed);
}

#[test]
fn the_distinct_path_map_never_holds_more_than_the_cap() {
    let mut tally = TrafficTally::new();
    for index in 0..(MAXIMUM_DISTINCT_PATHS + 50) {
        tally.count(&line(7, 200, 0, Some(&format!("/p{index}"))));
    }

    let held = tally.days.values().next().unwrap().paths.len();

    assert_eq!(held, MAXIMUM_DISTINCT_PATHS);
}

#[test]
fn status_codes_stop_at_the_cap_but_requests_do_not() {
    let mut tally = TrafficTally::new();
    let codes = MAXIMUM_STATUS_CODES as u16 + 5;
    for code in 0..codes {
        tally.count(&line(7, 100 + code, 0, None));
    }
    // A code that already has a counter keeps adding after the cap.
    tally.count(&line(7, 100, 0, None));

    let days = tally.into_days();

    assert_eq!(days[0].statuses.len(), MAXIMUM_STATUS_CODES);
    assert_eq!(days[0].requests, u64::from(codes) + 1);
    assert_eq!(days[0].statuses[0].requests, 2);
}

#[test]
fn paths_are_ranked_by_requests_then_by_path_and_cut_to_the_returned_count() {
    let mut tally = TrafficTally::new();
    for index in 0..(RETURNED_PATHS + 10) {
        tally.count(&line(7, 200, 0, Some(&format!("/p{index:03}"))));
    }
    tally.count(&line(7, 200, 0, Some("/p059")));

    let days = tally.into_days();
    let paths = &days[0].paths;

    assert_eq!(paths.len(), RETURNED_PATHS);
    assert_eq!(paths[0].path, "/p059", "most requested first");
    assert_eq!(paths[1].path, "/p000", "ties broken by path, ascending");
    assert_eq!(paths[2].path, "/p001");
}

#[test]
fn requests_without_a_path_count_as_other() {
    let mut tally = TrafficTally::new();
    tally.count(&line(7, 400, 0, None));
    tally.count(&line(7, 200, 0, Some("/a")));

    let days = tally.into_days();

    assert_eq!(days[0].other_path_requests, 1);
}

#[test]
fn a_day_past_the_cap_is_counted_as_unparsed_and_does_not_grow_the_tally() {
    let mut tally = TrafficTally::new();
    for day in 1..=(MAXIMUM_DAYS as u32 + 1) {
        tally.count(&line(day, 200, 0, None));
    }
    // A day already held is still counted.
    tally.count(&line(1, 200, 0, None));

    assert_eq!(tally.unparsed_lines(), 1);
    let days = tally.into_days();
    assert_eq!(days.len(), MAXIMUM_DAYS);
    assert_eq!(days[0].requests, 2);
}

#[test]
fn counters_saturate_instead_of_wrapping() {
    let mut tally = TrafficTally::new();
    tally.count(&line(7, 200, u64::MAX, None));
    tally.count(&line(7, 200, 5, None));

    assert_eq!(tally.into_days()[0].body_bytes, u64::MAX);
}
