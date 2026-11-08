//! Tests for counting the lines of one log file.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::read_traffic_file;
use crate::sites::fake_site_log_host::{FakeSiteLogHost, access_line};
use crate::sites::traffic_budget::{MAXIMUM_LINE_BYTES, READ_CHUNK_BYTES};
use crate::sites::traffic_tally::TrafficTally;

const NOON: &str = "07/Nov/2026:12:00:00 +0000";

fn file_with(bytes: &[u8]) -> Box<dyn crate::sites::SiteLogFile> {
    let host = FakeSiteLogHost::new();
    host.put("log", bytes);
    host.open("log")
}

fn requests(tally: TrafficTally) -> u64 {
    tally.into_days().iter().map(|day| day.requests).sum()
}

#[test]
fn whole_lines_are_counted_and_the_offset_stops_at_the_last_newline() {
    let whole = access_line(NOON, 200, 1, "/a") + &access_line(NOON, 200, 1, "/b");
    let bytes = format!("{whole}203.0.113.9 - - [07/Nov/2026:12:0");
    let file = file_with(bytes.as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = u64::MAX;

    let progress = read_traffic_file(file.as_ref(), 0, &mut budget, false, &mut tally).unwrap();

    assert_eq!(progress.offset, whole.len() as u64);
    assert!(progress.finished);
    assert_eq!(requests(tally), 2);
}

#[test]
fn a_line_finished_later_is_counted_once_not_twice() {
    let host = FakeSiteLogHost::new();
    let first = access_line(NOON, 200, 1, "/a");
    let second = access_line(NOON, 200, 1, "/b");
    let (head, tail) = second.split_at(30);
    host.put("log", format!("{first}{head}").as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = u64::MAX;

    let progress =
        read_traffic_file(host.open("log").as_ref(), 0, &mut budget, false, &mut tally).unwrap();
    host.append("log", tail.as_bytes());
    let next = read_traffic_file(
        host.open("log").as_ref(),
        progress.offset,
        &mut budget,
        false,
        &mut tally,
    )
    .unwrap();

    assert_eq!(next.offset, (first.len() + second.len()) as u64);
    assert_eq!(requests(tally), 2);
}

#[test]
fn a_trailing_line_without_a_newline_is_counted_only_when_the_file_is_closed() {
    let bytes = access_line(NOON, 200, 1, "/a");
    let bytes = bytes.trim_end_matches('\n');
    let open = {
        let mut tally = TrafficTally::new();
        let mut budget = u64::MAX;
        read_traffic_file(
            file_with(bytes.as_bytes()).as_ref(),
            0,
            &mut budget,
            false,
            &mut tally,
        )
        .unwrap();
        requests(tally)
    };
    let mut tally = TrafficTally::new();
    let mut budget = u64::MAX;

    let progress = read_traffic_file(
        file_with(bytes.as_bytes()).as_ref(),
        0,
        &mut budget,
        true,
        &mut tally,
    )
    .unwrap();

    assert_eq!(open, 0, "a live file's partial line waits for its newline");
    assert_eq!(requests(tally), 1, "a closed file's last line is whole");
    assert_eq!(progress.offset, bytes.len() as u64);
}

#[test]
fn a_budget_stops_the_read_and_a_second_call_finishes_with_no_line_lost_or_doubled() {
    let line = access_line(NOON, 200, 1, "/page");
    let all = line.repeat(10);
    let file = file_with(all.as_bytes());
    let mut tally = TrafficTally::new();
    let mut offset = 0;
    let mut calls = 0;

    loop {
        let mut budget = (line.len() * 3 + 5) as u64;
        let progress =
            read_traffic_file(file.as_ref(), offset, &mut budget, false, &mut tally).unwrap();
        assert_eq!(progress.offset % line.len() as u64, 0, "at a line boundary");
        offset = progress.offset;
        calls += 1;
        if progress.finished {
            break;
        }
        assert!(calls < 20, "every call must make progress");
    }

    assert!(calls > 1, "the budget must have stopped the first call");
    assert_eq!(requests(tally), 10);
}

#[test]
fn a_zero_budget_reads_nothing_and_reports_it_did_not_finish() {
    let file = file_with(access_line(NOON, 200, 1, "/a").as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = 0;

    let progress = read_traffic_file(file.as_ref(), 0, &mut budget, false, &mut tally).unwrap();

    assert!(!progress.finished);
    assert_eq!(progress.offset, 0);
    assert_eq!(requests(tally), 0);
}

#[test]
fn the_budget_is_spent_by_what_is_read() {
    let line = access_line(NOON, 200, 1, "/a");
    let file = file_with(line.repeat(4).as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = 1_000_000;

    read_traffic_file(file.as_ref(), 0, &mut budget, false, &mut tally).unwrap();

    assert_eq!(budget, 1_000_000 - (line.len() * 4) as u64);
}

#[test]
fn a_line_over_the_cap_is_counted_once_as_unparsed_and_its_neighbours_are_counted() {
    let huge = "a".repeat(MAXIMUM_LINE_BYTES + 1000);
    let bytes = format!(
        "{}{huge}\n{}",
        access_line(NOON, 200, 1, "/before"),
        access_line(NOON, 200, 1, "/after")
    );
    let file = file_with(bytes.as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = u64::MAX;

    let progress = read_traffic_file(file.as_ref(), 0, &mut budget, false, &mut tally).unwrap();

    assert_eq!(tally.unparsed_lines(), 1);
    assert_eq!(requests(tally), 2);
    assert_eq!(progress.offset, bytes.len() as u64);
}

#[test]
fn a_line_larger_than_the_whole_budget_still_moves_the_offset_forward() {
    let file = file_with(&vec![b'a'; 3 * MAXIMUM_LINE_BYTES]);
    let mut tally = TrafficTally::new();
    let mut budget = 2 * MAXIMUM_LINE_BYTES as u64;

    let progress = read_traffic_file(file.as_ref(), 0, &mut budget, false, &mut tally).unwrap();

    assert!(!progress.finished);
    assert_eq!(
        progress.offset,
        2 * MAXIMUM_LINE_BYTES as u64,
        "without this a cursor could be pinned in front of one endless line forever"
    );
}

#[test]
fn a_line_that_is_not_the_combined_format_is_counted_as_unparsed() {
    let bytes = format!("not a log line\n{}", access_line(NOON, 200, 1, "/a"));
    let file = file_with(bytes.as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = u64::MAX;

    read_traffic_file(file.as_ref(), 0, &mut budget, false, &mut tally).unwrap();

    assert_eq!(tally.unparsed_lines(), 1);
    assert_eq!(requests(tally), 1);
}

#[test]
fn lines_straddling_the_read_chunk_are_neither_lost_nor_split() {
    let line = access_line(NOON, 200, 1, "/straddle");
    let count = READ_CHUNK_BYTES * 2 / line.len() + 3;
    let file = file_with(line.repeat(count).as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = u64::MAX;

    let progress = read_traffic_file(file.as_ref(), 0, &mut budget, false, &mut tally).unwrap();

    assert_eq!(tally.unparsed_lines(), 0);
    assert_eq!(requests(tally), count as u64);
    assert_eq!(progress.offset, (line.len() * count) as u64);
}

#[test]
fn reading_from_the_end_counts_nothing_and_is_finished() {
    let bytes = access_line(NOON, 200, 1, "/a");
    let file = file_with(bytes.as_bytes());
    let mut tally = TrafficTally::new();
    let mut budget = u64::MAX;

    let progress = read_traffic_file(
        file.as_ref(),
        bytes.len() as u64,
        &mut budget,
        false,
        &mut tally,
    )
    .unwrap();

    assert!(progress.finished);
    assert_eq!(progress.offset, bytes.len() as u64);
    assert_eq!(requests(tally), 0);
}
