//! Tests for [`read_vhost_files`]: fetching the configuration files a listing names.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::RemoteCommand;
use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{RecordingSink, count_of, read_nginx};
use crate::transfer::inventory::inventory_run::InventoryRun;
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::model::vhost_budget::VhostBudget;
use crate::transfer::inventory::read_vhost_files::read_vhost_files;

/// Reads the nginx files of `shell` with a full budget.
fn read_with(shell: &FakeSourceShell, sink: &mut RecordingSink, budget: &mut VhostBudget) -> usize {
    let mut run = InventoryRun::new(shell, sink);
    read_vhost_files(
        &mut run,
        &RemoteCommand::ListNginxVhostFiles,
        &|path| RemoteCommand::ReadNginxVhostFile { path },
        budget,
    )
    .unwrap()
    .len()
}

/// Exactly the files the listing names are read, and nothing else is asked for.
#[test]
fn only_the_listed_files_are_read() {
    let shell = FakeSourceShell::new()
        .answering(
            RemoteCommand::ListNginxVhostFiles,
            "/etc/nginx/conf.d/a.conf\n/etc/nginx/conf.d/b.conf\n",
        )
        .answering(read_nginx("/etc/nginx/conf.d/a.conf"), "server { }")
        .answering(read_nginx("/etc/nginx/conf.d/b.conf"), "server { }");
    let mut sink = RecordingSink::default();

    let read = read_with(&shell, &mut sink, &mut VhostBudget::new());

    assert_eq!(read, 2);
    assert_eq!(shell.ran().len(), 3);
    assert!(sink.warnings.is_empty());
}

/// A path the grammar refuses is never asked for: the shell sees only the listing
/// and the one good file.
#[test]
fn a_refused_path_is_never_asked_for() {
    let shell = FakeSourceShell::new()
        .answering(
            RemoteCommand::ListNginxVhostFiles,
            "/etc/nginx/conf.d/a b.conf\n/etc/nginx/conf.d/ok.conf\n",
        )
        .answering(read_nginx("/etc/nginx/conf.d/ok.conf"), "server { }");
    let mut sink = RecordingSink::default();

    let read = read_with(&shell, &mut sink, &mut VhostBudget::new());

    assert_eq!(read, 1);
    assert_eq!(shell.ran().len(), 2);
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::SiteUnreadable),
        1
    );
}

/// A file that cannot be read is a warning and the next file is still read.
#[test]
fn an_unreadable_file_is_a_warning_and_the_next_is_read() {
    let shell = FakeSourceShell::new()
        .answering(
            RemoteCommand::ListNginxVhostFiles,
            "/etc/nginx/conf.d/a.conf\n/etc/nginx/conf.d/b.conf\n",
        )
        .exiting(read_nginx("/etc/nginx/conf.d/a.conf"), 1, "")
        .answering(read_nginx("/etc/nginx/conf.d/b.conf"), "server { }");
    let mut sink = RecordingSink::default();

    let read = read_with(&shell, &mut sink, &mut VhostBudget::new());

    assert_eq!(read, 1);
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::SiteUnreadable),
        1
    );
}

/// A listing that cannot be read is one warning and no files.
#[test]
fn an_unreadable_listing_is_one_warning() {
    let shell = FakeSourceShell::new().exiting(RemoteCommand::ListNginxVhostFiles, 127, "");
    let mut sink = RecordingSink::default();

    let read = read_with(&shell, &mut sink, &mut VhostBudget::new());

    assert_eq!(read, 0);
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::SiteUnreadable),
        1
    );
}

/// A file past the output bound is not parsed in part: it is dropped and marked.
#[test]
fn a_file_past_the_output_bound_is_dropped() {
    let big = "# filler filler filler\n".repeat(InventoryLimits::MAX_OUTPUT_BYTES / 20);
    let shell = FakeSourceShell::new()
        .answering(
            RemoteCommand::ListNginxVhostFiles,
            "/etc/nginx/conf.d/big.conf\n",
        )
        .answering(read_nginx("/etc/nginx/conf.d/big.conf"), &big);
    let mut sink = RecordingSink::default();

    let read = read_with(&shell, &mut sink, &mut VhostBudget::new());

    assert_eq!(read, 0);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 1);
}

/// An exhausted budget stops the reading with Truncated and asks for no more
/// files.
#[test]
fn an_exhausted_budget_stops_the_reading() {
    let shell = FakeSourceShell::new()
        .answering(
            RemoteCommand::ListNginxVhostFiles,
            "/etc/nginx/conf.d/a.conf\n/etc/nginx/conf.d/b.conf\n",
        )
        .answering(read_nginx("/etc/nginx/conf.d/a.conf"), "server { }")
        .answering(read_nginx("/etc/nginx/conf.d/b.conf"), "server { }");
    let mut sink = RecordingSink::default();
    let mut budget = VhostBudget::new();
    while budget.take_file() {}
    let mut run_budget = VhostBudget::new();
    run_budget.take_bytes(InventoryLimits::MAX_VHOST_BYTES);

    let none_left = read_with(&shell, &mut sink, &mut budget);
    let bytes_gone = read_with(&shell, &mut sink, &mut run_budget);

    assert_eq!(none_left, 0);
    assert_eq!(bytes_gone, 0);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 2);
}

/// A `find` that exits 1 because one of its directories is missing still printed
/// the files of the other: they are read. The listing of a host that has neither
/// directory is empty and warns about nothing.
#[test]
fn a_listing_that_exits_one_is_still_an_answer() {
    let shell = FakeSourceShell::new()
        .exiting(
            RemoteCommand::ListNginxVhostFiles,
            1,
            "/etc/nginx/conf.d/a.conf\n",
        )
        .answering(read_nginx("/etc/nginx/conf.d/a.conf"), "server { }");
    let empty = FakeSourceShell::new().exiting(RemoteCommand::ListNginxVhostFiles, 1, "");
    let mut sink = RecordingSink::default();

    let read = read_with(&shell, &mut sink, &mut VhostBudget::new());
    let read_empty = read_with(&empty, &mut sink, &mut VhostBudget::new());

    assert_eq!(read, 1);
    assert_eq!(read_empty, 0);
    assert!(sink.warnings.is_empty());
}
