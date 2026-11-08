//! A restore of one database out of a backup.

use std::fs::read_to_string;

use crate::backup::model::extract_identity::ExtractIdentity;

use super::super::*;
use super::restore_fixture::{Fixture, LIVE_FILE, databases, host_with, run_scoped, sink};

/// A single-database restore replaces that database alone: no other database is
/// dropped or extracted, no file in the home is touched, and the outcome says so.
#[test]
fn a_database_restore_replaces_that_database_and_touches_no_file() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);
    let wanted = databases(2).remove(0);

    let outcome = run_scoped(
        &fixture,
        &host,
        &databases(2),
        &RestoreScope::Database(wanted.clone()),
        &mut sink(),
    )
    .unwrap();

    assert!(
        !outcome.files_restored,
        "a database restore covers no file, and must not claim one"
    );
    assert_eq!(outcome.databases_restored, 1);
    assert_eq!(outcome.databases_total, 1);
    assert_eq!(host.dropped(), vec![wanted.as_str().to_owned()]);

    // One dump extracted, as root, named after the wanted database alone.
    let members: Vec<String> = host
        .extractions()
        .into_iter()
        .map(|(member, _identity)| member)
        .collect();
    assert_eq!(
        members,
        vec![
            "manifest.json".to_owned(),
            format!("databases/{}.sql", wanted.as_str()),
        ]
    );
    assert!(
        host.extractions()
            .iter()
            .all(|(_member, identity)| *identity == ExtractIdentity::Root),
        "no part of a database restore is read as the account"
    );

    // The live home is exactly as the customer left it, and no swap machinery ran.
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(!fixture.staging().exists());
    assert!(!fixture.marker().exists());
    assert!(host.swaps().is_empty());
}

/// A database the panel has forgotten is refused whatever the archive holds, and
/// nothing is dropped.
#[test]
fn a_database_restore_of_a_database_the_panel_does_not_know_is_refused() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);
    let forgotten = databases(2).remove(1);

    let error = run_scoped(
        &fixture,
        &host,
        &databases(1),
        &RestoreScope::Database(forgotten),
        &mut sink(),
    )
    .unwrap_err();

    assert!(
        matches!(error, BackupError::UnknownDatabase { .. }),
        "expected UnknownDatabase, got {error:?}"
    );
    assert!(host.dropped().is_empty());
}

/// A database the panel knows but the archive does not name was created after the
/// backup: refused as such, with nothing extracted from the databases and nothing
/// dropped.
#[test]
fn a_database_restore_of_a_database_the_archive_does_not_hold_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let newer = databases(2).remove(1);

    let error = run_scoped(
        &fixture,
        &host,
        &databases(2),
        &RestoreScope::Database(newer),
        &mut sink(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::DatabaseNotInBackup);
    assert!(host.dropped().is_empty());
    assert_eq!(
        host.extractions().len(),
        1,
        "only the manifest may have been read out of the archive"
    );
}

/// A dump that is not the one the manifest recorded is refused before the drop,
/// for a single-database restore exactly as for a whole one.
#[test]
fn a_database_restore_whose_dump_fails_its_checksum_is_refused_before_the_drop() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let wanted = databases(1).remove(0);
    host.holds_dump(wanted.as_str(), "-- a dump nobody recorded\n");

    let error = run_scoped(
        &fixture,
        &host,
        &databases(1),
        &RestoreScope::Database(wanted),
        &mut sink(),
    )
    .unwrap_err();

    assert!(
        matches!(error, BackupError::DumpChecksumMismatch { .. }),
        "expected DumpChecksumMismatch, got {error:?}"
    );
    assert!(host.dropped().is_empty());
}
