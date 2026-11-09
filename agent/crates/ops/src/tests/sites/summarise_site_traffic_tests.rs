//! Tests for the traffic summary: where it resumes and what it counts.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::{summarise_site_traffic, summarise_with};
use crate::sites::SitesOpError;
use crate::sites::fake_site_log_host::{FakeSiteLogHost, access_line};
use crate::sites::model::traffic_cursor::TrafficCursor;
use crate::sites::model::traffic_summary::TrafficSummary;
use crate::sites::model::traffic_summary_input::TrafficSummaryInput;
use crate::sites::traffic_budget::MAXIMUM_READ_BYTES;
use crate::sites::traffic_run_slots::TrafficRunSlots;

const LIVE: &str = "a.com.access.log";
const PREVIOUS: &str = "a.com.access.log.1";
const NOON: &str = "07/Nov/2026:12:00:00 +0000";

fn input(cursor: Option<TrafficCursor>) -> TrafficSummaryInput {
    TrafficSummaryInput {
        account: AccountName::parse("acme").unwrap(),
        domain: Domain::parse("a.com").unwrap(),
        cursor,
    }
}

/// Summarises against an admission count of its own: tests run in parallel
/// threads and the process-wide count is two, so sharing it would make a test
/// fail with `TrafficBusy` because of its neighbours.
fn summarise(host: &FakeSiteLogHost, cursor: Option<TrafficCursor>) -> TrafficSummary {
    summarise_with(
        &TrafficRunSlots::new(1),
        host,
        &input(cursor),
        MAXIMUM_READ_BYTES,
    )
    .unwrap()
}

fn outcome(host: &FakeSiteLogHost) -> Result<TrafficSummary, SitesOpError> {
    summarise_with(
        &TrafficRunSlots::new(1),
        host,
        &input(None),
        MAXIMUM_READ_BYTES,
    )
}

fn total(summary: &TrafficSummary) -> u64 {
    summary.days.iter().map(|day| day.requests).sum()
}

fn lines(count: usize, time: &str) -> String {
    access_line(time, 200, 10, "/x").repeat(count)
}

#[test]
fn a_first_call_counts_the_live_file_and_returns_a_cursor_at_its_end() {
    let host = FakeSiteLogHost::new();
    let bytes = lines(3, NOON);
    host.put(LIVE, bytes.as_bytes());

    let summary = summarise(&host, None);

    assert_eq!(total(&summary), 3);
    assert_eq!(summary.days[0].body_bytes, 30);
    assert!(summary.caught_up);
    assert!(!summary.gap);
    assert_eq!(summary.cursor.unwrap().offset, bytes.len() as u64);
}

#[test]
fn a_second_call_reads_only_what_was_appended() {
    let host = FakeSiteLogHost::new();
    host.put(LIVE, lines(3, NOON).as_bytes());
    let first = summarise(&host, None);

    host.append(LIVE, lines(2, NOON).as_bytes());
    let second = summarise(&host, first.cursor);

    assert_eq!(total(&second), 2);
}

#[test]
fn a_call_with_nothing_new_counts_nothing() {
    let host = FakeSiteLogHost::new();
    host.put(LIVE, lines(3, NOON).as_bytes());
    let first = summarise(&host, None);

    let second = summarise(&host, first.cursor.clone());

    assert!(second.days.is_empty());
    assert_eq!(second.cursor, first.cursor);
    assert!(second.caught_up);
    assert!(!second.gap);
}

#[test]
fn a_rotation_between_calls_finishes_the_old_file_then_reads_the_new_one() {
    let host = FakeSiteLogHost::new();
    host.put(LIVE, lines(3, NOON).as_bytes());
    let first = summarise(&host, None);

    // Two lines land in the old file after the call, then logrotate renames it
    // and nginx opens a new one that collects three more.
    host.append(LIVE, lines(2, NOON).as_bytes());
    host.rotate(LIVE, PREVIOUS);
    host.append(LIVE, lines(3, NOON).as_bytes());
    let second = summarise(&host, first.cursor);

    assert_eq!(
        total(&second),
        5,
        "2 from the rest of .1 and 3 from the new file"
    );
    assert!(!second.gap);
    assert!(second.caught_up);
}

#[test]
fn a_cursor_that_names_a_file_no_longer_there_is_a_gap_and_reads_only_the_live_file() {
    let host = FakeSiteLogHost::new();
    host.put(PREVIOUS, lines(4, NOON).as_bytes());
    host.put(LIVE, lines(2, NOON).as_bytes());
    let stranger = TrafficCursor {
        file_id: "7:999999:0000000000000000".to_owned(),
        offset: 10,
    };

    let summary = summarise(&host, Some(stranger));

    assert!(summary.gap);
    assert_eq!(
        total(&summary),
        2,
        ".1 may be partly counted already, so it is not guessed at"
    );
}

#[test]
fn no_cursor_starts_at_the_previous_rotation_when_there_is_one() {
    let host = FakeSiteLogHost::new();
    host.put(PREVIOUS, lines(4, NOON).as_bytes());
    host.put(LIVE, lines(2, NOON).as_bytes());

    let summary = summarise(&host, None);

    assert_eq!(total(&summary), 6);
    assert!(!summary.gap);
}

#[test]
fn only_the_live_file_and_the_first_rotation_are_ever_read() {
    let host = FakeSiteLogHost::new();
    host.put("a.com.access.log.2", lines(9, NOON).as_bytes());
    host.put("a.com.access.log.3.gz", lines(9, NOON).as_bytes());
    host.put("a.com.error.log", lines(9, NOON).as_bytes());
    host.put(LIVE, lines(1, NOON).as_bytes());

    assert_eq!(total(&summarise(&host, None)), 1);
}

#[test]
fn a_site_with_no_access_log_yet_gives_an_empty_summary_and_no_cursor() {
    let host = FakeSiteLogHost::new();

    let summary = summarise(&host, None);

    assert!(summary.days.is_empty());
    assert!(summary.cursor.is_none());
    assert!(summary.caught_up);
    assert!(!summary.gap);
}

#[test]
fn only_a_rotation_and_no_live_file_yet_is_read_and_resumed_from() {
    let host = FakeSiteLogHost::new();
    host.put(PREVIOUS, lines(4, NOON).as_bytes());

    let first = summarise(&host, None);
    let second = summarise(&host, first.cursor.clone());

    assert_eq!(total(&first), 4);
    assert!(first.cursor.is_some());
    assert!(second.days.is_empty());
    assert!(!second.gap);
}

#[test]
fn requests_are_bucketed_by_the_utc_day_across_midnight() {
    let host = FakeSiteLogHost::new();
    let bytes = access_line("07/Nov/2026:23:59:59 +0000", 200, 1, "/a")
        + &access_line("08/Nov/2026:00:00:00 +0000", 200, 1, "/a")
        + &access_line("08/Nov/2026:02:00:00 +0300", 200, 1, "/a");
    host.put(LIVE, bytes.as_bytes());

    let summary = summarise(&host, None);

    let per_day: Vec<_> = summary
        .days
        .iter()
        .map(|day| (day.date.to_iso(), day.requests))
        .collect();
    assert_eq!(
        per_day,
        vec![("2026-11-07".to_owned(), 2), ("2026-11-08".to_owned(), 1)],
        "02:00 +0300 on the 8th is 23:00 UTC on the 7th"
    );
}

#[test]
fn unparsed_lines_are_reported_and_add_to_no_figure() {
    let host = FakeSiteLogHost::new();
    host.put(LIVE, format!("junk\n{}", lines(2, NOON)).as_bytes());

    let summary = summarise(&host, None);

    assert_eq!(summary.unparsed_lines, 1);
    assert_eq!(total(&summary), 2);
}

#[test]
fn a_small_budget_catches_up_over_several_calls_without_losing_or_doubling_a_line() {
    let host = FakeSiteLogHost::new();
    let line_len = access_line(NOON, 200, 10, "/x").len() as u64;
    host.put(PREVIOUS, lines(6, NOON).as_bytes());
    host.put(LIVE, lines(6, NOON).as_bytes());
    let slots = TrafficRunSlots::new(1);
    let mut cursor = None;
    let mut counted = 0;
    let mut calls = 0;

    loop {
        let summary =
            summarise_with(&slots, &host, &input(cursor.clone()), line_len * 4 + 3).unwrap();
        counted += total(&summary);
        cursor = summary.cursor.clone();
        calls += 1;
        assert!(!summary.gap);
        if summary.caught_up {
            break;
        }
        assert!(calls < 20, "every call must make progress");
    }

    assert!(calls > 1, "the budget must have stopped the first call");
    assert_eq!(counted, 12);
}

#[test]
fn a_call_beyond_the_host_ceiling_is_refused_before_anything_is_opened() {
    let host = FakeSiteLogHost::new();
    host.put(LIVE, lines(1, NOON).as_bytes());
    let none_left = TrafficRunSlots::new(0);

    let refused = summarise_with(&none_left, &host, &input(None), MAXIMUM_READ_BYTES);

    assert!(matches!(refused, Err(SitesOpError::TrafficBusy)));
    assert_eq!(host.opened_directories(), 0);
}

#[test]
fn the_slot_is_given_back_after_a_success_and_after_an_error() {
    let host = FakeSiteLogHost::new();
    host.put(LIVE, lines(1, NOON).as_bytes());
    let refusing = FakeSiteLogHost::new();
    refusing.refuse_the_directory();
    let slots = TrafficRunSlots::new(1);

    summarise_with(&slots, &host, &input(None), MAXIMUM_READ_BYTES).unwrap();
    let refused = summarise_with(&slots, &refusing, &input(None), MAXIMUM_READ_BYTES);

    assert!(matches!(refused, Err(SitesOpError::LogUnreadable { .. })));
    assert!(
        slots.try_acquire().is_ok(),
        "the permit is released by drop on every path"
    );
}

#[test]
fn a_directory_that_is_refused_is_an_error_and_not_an_empty_summary() {
    let host = FakeSiteLogHost::new();
    host.refuse_the_directory();

    assert!(matches!(
        outcome(&host),
        Err(SitesOpError::LogUnreadable { .. })
    ));
}

#[test]
fn a_log_that_is_not_a_regular_file_is_an_error_and_not_no_traffic() {
    let host = FakeSiteLogHost::new();
    host.put_directory(LIVE);

    assert!(matches!(
        outcome(&host),
        Err(SitesOpError::LogUnreadable { .. })
    ));
}

#[test]
fn the_public_entry_point_reads_a_log_through_the_process_wide_count() {
    // The one test that takes a slot of the shared count, so it can never be
    // refused by a neighbour: no other test of this file touches it.
    let host = FakeSiteLogHost::new();
    host.put(LIVE, lines(2, NOON).as_bytes());

    let summary = summarise_site_traffic(&host, &input(None)).unwrap();

    assert_eq!(total(&summary), 2);
}
