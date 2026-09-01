//! Tests for [`tail_site_log`].
//!
//! What is pinned here is what the OPERATION decides: that the history is
//! clamped before the host is asked for anything, that the log is named from
//! the site's own derivation rather than from the caller, and that the
//! directory handed to the host is the resolved one.
//!
//! What is deliberately NOT pinned here is the reading. An oversized file, a
//! FIFO, a hardlink out of the home and a directory swapped between two polls
//! are properties of real inodes, and a fake that "returned a FIFO" would only
//! be testing the fake. Those belong to tests over `sites::log_tail::follow`
//! against a temporary directory.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::domain::Domain;
use maran_agent_core::validation::name::AccountName;

use crate::sites::fake_site_host::{CANONICAL_HOME_ROOT, FakeSiteHost, php_input};
use crate::sites::log_sink::LogSink;
use crate::sites::model::tail_end::TailEnd;
use crate::sites::{MAXIMUM_HISTORY_LINES, SiteLogKind, tail_site_log};

/// A sink that records what it was given and never asks to stop.
struct Recorder {
    /// The lines delivered, with their `historical` flag.
    lines: Vec<(String, bool)>,
}

impl LogSink for Recorder {
    fn line(&mut self, line: &str, historical: bool) -> Result<(), TailEnd> {
        self.lines.push((line.to_owned(), historical));
        Ok(())
    }

    fn is_listening(&mut self) -> bool {
        true
    }
}

/// A sink that refuses the first line it is offered.
struct Deaf;

impl LogSink for Deaf {
    fn line(&mut self, _line: &str, _historical: bool) -> Result<(), TailEnd> {
        Err(TailEnd::ClientClosed)
    }

    fn is_listening(&mut self) -> bool {
        false
    }
}

fn account() -> AccountName {
    AccountName::parse("acme").unwrap()
}

fn domain() -> Domain {
    Domain::parse("example.com").unwrap()
}

#[test]
fn a_caller_asking_for_a_million_lines_gets_the_cap_and_not_the_million() {
    let host = FakeSiteHost::passing();
    let mut sink = Recorder { lines: Vec::new() };

    tail_site_log(
        &host,
        &account(),
        &domain(),
        SiteLogKind::Access,
        1_000_000,
        &mut sink,
    )
    .unwrap();

    let asked = host.tailed().expect("the host must be asked for the tail");
    assert_eq!(
        asked.history_lines, MAXIMUM_HISTORY_LINES,
        "the clamp must happen before the host is asked, not after it has read"
    );
}

#[test]
fn the_log_is_named_by_the_site_and_reached_through_the_resolved_directory() {
    let host = FakeSiteHost::passing();
    let mut sink = Recorder { lines: Vec::new() };

    tail_site_log(
        &host,
        &account(),
        &domain(),
        SiteLogKind::Error,
        10,
        &mut sink,
    )
    .unwrap();

    let asked = host.tailed().unwrap();
    assert_eq!(asked.file_name, "example.com.error.log");
    // The canonical root the fake resolves to, not the named `/home` one: a
    // tail that used the named path would be reopening a path the account can
    // swap.
    assert_eq!(
        asked.directory,
        std::path::Path::new(CANONICAL_HOME_ROOT).join("acme/logs")
    );
    assert_eq!(asked.account, php_input().account);
}

#[test]
fn a_sink_that_refuses_a_line_stops_the_tail_there() {
    let host = FakeSiteHost::passing();
    host.with_log(&["first", "second", "third"]);

    tail_site_log(
        &host,
        &account(),
        &domain(),
        SiteLogKind::Access,
        10,
        &mut Deaf,
    )
    .unwrap();

    // Nothing to assert on the sink itself — it kept nothing — so what is
    // pinned is that the operation returned rather than looping.
    assert!(host.tailed().is_some());
}

#[test]
fn the_historical_batch_is_marked_as_historical() {
    let host = FakeSiteHost::passing();
    host.with_log(&["one", "two"]);
    let mut sink = Recorder { lines: Vec::new() };

    tail_site_log(
        &host,
        &account(),
        &domain(),
        SiteLogKind::Access,
        10,
        &mut sink,
    )
    .unwrap();

    assert_eq!(
        sink.lines,
        vec![("one".to_owned(), true), ("two".to_owned(), true)]
    );
}
