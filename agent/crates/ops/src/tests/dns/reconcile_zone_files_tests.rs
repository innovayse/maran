//! Tests for `reconcile_zone_files`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{
    FakeZoneHost, directories, key_directory, signed_directory, zone_directory,
};
use super::super::model::zone_reconciliation::ZoneReconciliation;
use super::reconcile_zone_files;

#[test]
fn a_zone_with_a_file_that_the_daemon_lost_is_added_again() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "kept.example", 1, "; x\n")
        .with_unserved_file(&zone_directory(), "lost.example");

    let outcome = reconcile_zone_files(&host, distro(), &directories()).unwrap();

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

    reconcile_zone_files(&host, distro(), &directories()).unwrap();

    assert!(host.rndc_commands().iter().all(|c| c[0] != "addzone"));
}

#[test]
fn a_daemon_that_does_not_answer_is_not_a_fault() {
    let host = FakeZoneHost::new()
        .with_unserved_file(&zone_directory(), "lost.example")
        .with_daemon_down();

    assert_eq!(
        reconcile_zone_files(&host, distro(), &directories()).unwrap(),
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
        reconcile_zone_files(&host, distro(), &directories()).unwrap(),
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
        reconcile_zone_files(&host, distro(), &directories()).unwrap(),
        ZoneReconciliation::Compared {
            files: 0,
            added: 0,
            failed: 0
        }
    );
}

/// Plants a complete key for `zone` in the key directory.
fn with_key(host: FakeZoneHost, zone: &str) -> FakeZoneHost {
    host.with_file(
        &key_directory().join(format!("K{zone}.+013+11111.key")),
        "x\n",
    )
    .with_file(
        &key_directory().join(format!("K{zone}.+013+11111.private")),
        "x\n",
    )
}

#[test]
fn a_zone_in_the_signed_directory_with_its_key_is_added_again_signed() {
    let host = with_key(
        FakeZoneHost::new().with_unserved_file(&signed_directory(), "lost.example"),
        "lost.example",
    );

    let outcome = reconcile_zone_files(&host, distro(), &directories()).unwrap();

    assert_eq!(
        outcome,
        ZoneReconciliation::Compared {
            files: 1,
            added: 1,
            failed: 0
        }
    );
    let statement = host.statement("lost.example").unwrap();
    assert!(statement.contains("file \"/signed-zones/lost.example.db\""));
    assert!(statement.contains("dnssec-policy \"maran-csk\""));
    assert!(statement.contains("key-directory \"/keys\""));
}

#[test]
fn a_zone_in_the_signed_directory_without_a_key_is_not_added_and_is_counted_failed() {
    // The planted failure: adding it would have the daemon generate a key the
    // registrar's DS record does not name.
    let host = FakeZoneHost::new().with_unserved_file(&signed_directory(), "lost.example");

    let outcome = reconcile_zone_files(&host, distro(), &directories()).unwrap();

    assert_eq!(
        outcome,
        ZoneReconciliation::Compared {
            files: 1,
            added: 0,
            failed: 1
        }
    );
    assert!(host.rndc_commands().iter().all(|c| c[0] != "addzone"));
    assert!(host.served_serial("lost.example").is_none());
}

#[test]
fn an_unsigned_zone_is_still_added_with_the_old_statement() {
    // The inverse control: the plain directory's zone is added exactly as before.
    let host = FakeZoneHost::new().with_unserved_file(&zone_directory(), "lost.example");

    reconcile_zone_files(&host, distro(), &directories()).unwrap();

    assert_eq!(
        host.statement("lost.example").unwrap(),
        "{ type primary; file \"/zones/lost.example.db\"; };"
    );
}

#[test]
fn a_signed_zone_the_daemon_already_serves_is_left_alone() {
    let host = with_key(
        FakeZoneHost::new().with_zone(&signed_directory(), "kept.example", 1, "; x\n"),
        "kept.example",
    );

    reconcile_zone_files(&host, distro(), &directories()).unwrap();

    assert!(host.rndc_commands().iter().all(|c| c[0] != "addzone"));
}
