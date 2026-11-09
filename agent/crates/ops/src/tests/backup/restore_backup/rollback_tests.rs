//! A restore that fails after the point of no return, and what the account is
//! then.

use std::fs::read_to_string;

use crate::backup::MANIFEST_VERSION;
use crate::backup::recording_backup_host::RecordingBackupHost;

use super::super::*;
use super::restore_fixture::{
    Fixture, LIVE_FILE, account, databases, host_with, manifest, rollback_loads, run, sink,
};

/// Anything that fails before the first drop is undone by doing nothing: the
/// home is the customer's, every database is untouched, and the operation's own
/// scratch and staging trees are gone.
#[test]
fn a_failure_before_the_first_drop_leaves_the_home_and_every_database_untouched() {
    let fixture = Fixture::new(1);
    let host = RecordingBackupHost::new();
    let mut future = manifest(1);
    future.version = MANIFEST_VERSION + 1;
    host.holds_manifest(future);

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::ManifestVersionUnknown);
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(host.dropped().is_empty());
    assert!(!fixture.staging().exists());
    assert!(!fixture.scratch().exists());
}

/// After the point of no return, every database already replaced is put back
/// from its own rollback dump, in reverse order.
#[test]
fn a_failure_after_the_first_drop_reloads_every_rollback_dump_in_reverse_order() {
    let fixture = Fixture::new(3);
    let host = host_with(&fixture, 3);
    host.fail_archive_load_for("alice_shop2");

    let error = run(&fixture, &host, &databases(3), &mut sink()).unwrap_err();

    assert_eq!(
        error,
        BackupError::RolledBack {
            failed: "alice_shop2".to_owned(),
            rolled_back: vec![
                "alice_shop2".to_owned(),
                "alice_shop1".to_owned(),
                "alice_shop0".to_owned()
            ]
        }
    );
    assert_eq!(
        rollback_loads(&host),
        vec![
            "alice_shop2".to_owned(),
            "alice_shop1".to_owned(),
            "alice_shop0".to_owned()
        ]
    );
    // The swap has not happened yet, so the files were never at risk.
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// A rollback that cannot put a database back names it, in its own error
/// variant, so it can never be read as the ordinary rollback.
#[test]
fn a_rollback_that_itself_fails_names_the_databases_it_could_not_restore() {
    let fixture = Fixture::new(3);
    let host = host_with(&fixture, 3);
    host.fail_archive_load_for("alice_shop2");
    host.fail_rollback_loads();

    let error = run(&fixture, &host, &databases(3), &mut sink()).unwrap_err();

    match error {
        BackupError::RolledBackPartially {
            failed,
            rolled_back,
            not_rolled_back,
        } => {
            assert_eq!(failed, "alice_shop2");
            // Every database this restore had replaced failed its rollback, so
            // the successful list is empty — and it is ASSERTED rather than
            // ignored: an empty `rolled_back` beside a full `not_rolled_back`
            // is the difference between "nothing could be put back" and "the
            // field is not being filled at all", which is exactly what a
            // mutation of the rollback bookkeeping produces.
            assert!(rolled_back.is_empty());
            assert_eq!(
                not_rolled_back,
                vec![
                    "alice_shop2".to_owned(),
                    "alice_shop1".to_owned(),
                    "alice_shop0".to_owned()
                ]
            );
        }
        other => panic!("expected a partial rollback, got {other:?}"),
    }
}

/// The second rename failing is reversed at once, and the account has its home
/// back at the path everything else on this host expects it at.
#[test]
fn a_failed_second_rename_reverses_the_first_and_the_account_has_its_home_back() {
    let fixture = Fixture::new(1);
    let placement = fixture.placement();
    // The staging tree was never built, so the second rename cannot succeed.
    assert!(!placement.staging.exists());

    let error = swap_home(&placement, &account(), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::StagingUnusable);
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(!placement.previous.exists());
}

/// A restore that could not replace every database reports FAILED. There is no
/// success value that can describe four of five.
#[test]
fn a_restore_that_loses_a_database_reports_failed_not_completed() {
    let fixture = Fixture::new(3);
    let host = host_with(&fixture, 3);
    host.fail_archive_load_for("alice_shop1");

    let answer = run(&fixture, &host, &databases(3), &mut sink());

    assert!(
        answer.is_err(),
        "a restore that lost a database reported {answer:?}"
    );
    // And the whole restore, which is the only thing that may report an
    // outcome, reports counts that are equal.
    let whole = Fixture::new(3);
    let outcome = run(&whole, &host_with(&whole, 3), &databases(3), &mut sink()).unwrap();
    assert_eq!(outcome.databases_restored, outcome.databases_total);
}

/// A load killed at its deadline is not a success and not left alone: the
/// server may have taken part of the dump, so it is unwound exactly like a load
/// that failed, every database already replaced going back from its own
/// rollback dump.
#[test]
fn a_load_that_times_out_after_the_first_drop_is_rolled_back_like_a_failed_load() {
    let fixture = Fixture::new(3);
    let host = host_with(&fixture, 3);
    host.time_out_archive_load_for("alice_shop2");

    let error = run(&fixture, &host, &databases(3), &mut sink()).unwrap_err();

    assert_eq!(
        error,
        BackupError::RolledBack {
            failed: "alice_shop2".to_owned(),
            rolled_back: vec![
                "alice_shop2".to_owned(),
                "alice_shop1".to_owned(),
                "alice_shop0".to_owned()
            ]
        }
    );
    // The swap has not happened yet, so the files were never at risk.
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}
