//! Tests for [`parse_atq_listing`]: reading `atq` output into jobs.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::accounts::parse_atq_listing::parse_atq_listing;

/// A listing line is split into id and last-token user.
#[test]
fn a_listing_line_yields_its_id_and_last_column_as_user() {
    let jobs = parse_atq_listing("7\tThu Oct 29 10:00:00 2026 a acme\n").unwrap();
    assert_eq!(jobs.len(), 1);
    assert_eq!(jobs[0].id, "7");
    assert_eq!(jobs[0].user, "acme");
}

/// An empty listing is an empty queue.
#[test]
fn an_empty_listing_has_no_jobs() {
    assert!(parse_atq_listing("").unwrap().is_empty());
}

/// Several lines are all returned in order.
#[test]
fn several_lines_are_all_returned() {
    let jobs =
        parse_atq_listing("1\tMon Jan 1 00:00:00 2027 a x\n2\tMon Jan 1 00:00:00 2027 b y\n")
            .unwrap();
    assert_eq!(
        jobs.iter().map(|j| j.user.as_str()).collect::<Vec<_>>(),
        ["x", "y"]
    );
}

/// A line without a tab is unreadable.
#[test]
fn a_line_without_a_tab_is_unparseable() {
    assert!(parse_atq_listing("garbage\n").is_none());
}

/// A non-numeric id is unreadable.
#[test]
fn a_non_numeric_id_is_unparseable() {
    assert!(parse_atq_listing("-f\tMon Jan 1 00:00:00 2027 a acme\n").is_none());
}

/// Too few columns is unreadable.
#[test]
fn a_line_with_too_few_columns_is_unparseable() {
    assert!(parse_atq_listing("3\tacme\n").is_none());
}

/// One bad line poisons the whole listing.
#[test]
fn one_bad_line_makes_the_whole_listing_unparseable() {
    assert!(parse_atq_listing("1\tMon Jan 1 00:00:00 2027 a x\nbad\n").is_none());
}
