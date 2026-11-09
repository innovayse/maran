//! Tests for `reconcile_zone_files`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, zone_directory};
use super::super::model::zone_reconciliation::ZoneReconciliation;
use super::reconcile_zone_files;

#[test]
fn a_zone_with_a_file_that_the_daemon_lost_is_added_again() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "kept.example", 1, "; x\n")
        .with_unserved_file(&zone_directory(), "lost.example");

    let outcome = reconcile_zone_files(&host, distro(), &zone_directory()).unwrap();

    assert_eq!(
        outcome,
        ZoneReconciliation::Compared {
            files: 2,
            added: 1,
            failed: 0
        }
    );
    assert!(host.served_serial("lost.example").is_some());
}

#[test]
fn a_zone_the_daemon_serves_is_left_alone() {
    let host = FakeZoneHost::new().with_zone(&zone_directory(), "kept.example", 1, "; x\n");

    reconcile_zone_files(&host, distro(), &zone_directory()).unwrap();

    assert!(host.rndc_commands().iter().all(|c| c[0] != "addzone"));
}

#[test]
fn a_daemon_that_does_not_answer_is_not_a_fault() {
    let host = FakeZoneHost::new()
        .with_unserved_file(&zone_directory(), "lost.example")
        .with_daemon_down();

    assert_eq!(
        reconcile_zone_files(&host, distro(), &zone_directory()).unwrap(),
        ZoneReconciliation::DaemonNotAnswering
    );
}

#[test]
fn a_zone_the_daemon_refuses_is_counted_and_the_pass_goes_on() {
    let host = FakeZoneHost::new()
        .with_unserved_file(&zone_directory(), "a.example")
        .with_unserved_file(&zone_directory(), "b.example")
        .with_load_refused();

    assert_eq!(
        reconcile_zone_files(&host, distro(), &zone_directory()).unwrap(),
        ZoneReconciliation::Compared {
            files: 2,
            added: 0,
            failed: 2
        }
    );
}

#[test]
fn an_empty_directory_compares_nothing_and_adds_nothing() {
    let host = FakeZoneHost::new();

    assert_eq!(
        reconcile_zone_files(&host, distro(), &zone_directory()).unwrap(),
        ZoneReconciliation::Compared {
            files: 0,
            added: 0,
            failed: 0
        }
    );
}
