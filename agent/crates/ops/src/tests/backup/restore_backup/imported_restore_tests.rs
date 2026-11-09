//! A restore of an archive received from another host, whose dumps load
//! through the restricted loader.

use std::fs::read_to_string;

use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::dump_refusal::DumpRefusal;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::recording_backup_host::RecordingBackupHost;

use super::super::*;
use super::restore_fixture::{
    Fixture, LIVE_FILE, databases, digest, host_with, manifest, rollback_loads, run, run_scoped,
    sink,
};

/// A dump of `database` that the restricted loader's scan accepts.
fn clean_dump_of(database: &DatabaseName) -> String {
    format!(
        "USE `{name}`;\nCREATE TABLE `t` (`a` int);\nINSERT INTO `t` VALUES (1);\n",
        name = database.as_str()
    )
}

/// A dump of `database` that a database superuser would run to the end and that
/// the scan refuses.
fn hostile_dump_of(database: &DatabaseName) -> String {
    format!(
        "USE `{name}`;\nGRANT ALL PRIVILEGES ON *.* TO 'x'@'%';\n",
        name = database.as_str()
    )
}

/// The manifest of an archive that holds `count` databases whose dumps are
/// `dump`'s answers, digests included.
fn manifest_dumping(count: usize, dump: &dyn Fn(&DatabaseName) -> String) -> BackupManifest {
    let mut described = manifest(count);
    described.databases = databases(count)
        .iter()
        .map(|database| ManifestDatabase {
            name: database.as_str().to_owned(),
            bytes: dump(database).len() as u64,
            sha256: digest(&dump(database)),
        })
        .collect();

    described
}

/// A host whose archive carries `count` databases, those at the indices in
/// `hostile` holding a dump the scan refuses, published beside an IMPORTED
/// sidecar.
fn imported_host(fixture: &Fixture, count: usize, hostile: &[usize]) -> RecordingBackupHost {
    let all = databases(count);
    let dump_for = |database: &DatabaseName| {
        let index = all
            .iter()
            .position(|candidate| candidate == database)
            .unwrap();
        if hostile.contains(&index) {
            hostile_dump_of(database)
        } else {
            clean_dump_of(database)
        }
    };
    let described = manifest_dumping(count, &dump_for);
    fixture.publish_imported_sidecar(&described);

    let host = RecordingBackupHost::new();
    host.answers_identities(&[Some(fixture.identity())]);
    host.holds_manifest(described);
    for database in &all {
        host.holds_dump(database.as_str(), &dump_for(database));
    }

    host
}

/// The restricted loader's calls for a clean load of each of `names`, in order.
fn restricted_loads_of(names: &[&str]) -> Vec<String> {
    names
        .iter()
        .flat_map(|name| {
            [
                format!("create {name}"),
                "load".to_owned(),
                "drop".to_owned(),
            ]
        })
        .collect()
}

/// A whole restore of an imported archive loads every dump through the
/// restricted loader and none of them as the superuser.
#[test]
fn an_imported_archive_is_restored_through_the_restricted_loader_and_not_as_the_superuser() {
    let fixture = Fixture::new(2);
    let host = imported_host(&fixture, 2, &[]);

    let outcome = run(&fixture, &host, &databases(2), &mut sink()).unwrap();

    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, 2);
    assert_eq!(
        host.restricted_calls(),
        restricted_loads_of(&["alice_shop0", "alice_shop1"])
    );
    assert_eq!(
        host.restricted_loaded(),
        databases(2).iter().map(clean_dump_of).collect::<Vec<_>>()
    );
    assert!(
        host.loaded().is_empty(),
        "no imported dump may reach the superuser client: {:?}",
        host.loaded()
    );
}

/// One refused dump among clean ones fails the restore before ANY database is
/// dropped, any user is created or the home is touched: the scan runs over every
/// dump first.
#[test]
fn an_imported_archive_with_a_refused_dump_fails_before_any_drop() {
    let fixture = Fixture::new(2);
    // The SECOND database's dump is the hostile one, so a restore that scanned
    // as it loaded would already have dropped and replaced the first.
    let host = imported_host(&fixture, 2, &[1]);

    let error = run(&fixture, &host, &databases(2), &mut sink()).unwrap_err();

    assert_eq!(
        error,
        BackupError::RestrictedLoadRefused {
            reason: DumpRefusal::ForbiddenStatement
        }
    );
    assert!(host.dropped().is_empty());
    assert!(host.restricted_calls().is_empty());
    assert!(host.loaded().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(!fixture.staging().exists());
    assert!(!fixture.scratch().exists());
}

/// An archive this host made is restored exactly as it always was: the
/// superuser client loads each dump and the restricted loader is not asked.
#[test]
fn an_own_archive_is_still_loaded_as_the_superuser() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);

    run(&fixture, &host, &databases(2), &mut sink()).unwrap();

    assert!(host.restricted_calls().is_empty());
    let loaded: Vec<String> = host
        .loaded()
        .into_iter()
        .map(|(name, _path)| name)
        .collect();
    assert_eq!(
        loaded,
        vec!["alice_shop0".to_owned(), "alice_shop1".to_owned()]
    );
}

/// A single-database restore of an imported archive takes the restricted path
/// too, for the one dump it extracts.
#[test]
fn an_imported_database_restore_is_loaded_through_the_restricted_loader() {
    let fixture = Fixture::new(2);
    let host = imported_host(&fixture, 2, &[]);
    let wanted = databases(2).remove(0);

    let outcome = run_scoped(
        &fixture,
        &host,
        &databases(2),
        &RestoreScope::Database(wanted.clone()),
        &mut sink(),
    )
    .unwrap();

    assert_eq!(outcome.databases_restored, 1);
    assert_eq!(host.dropped(), vec![wanted.as_str().to_owned()]);
    assert_eq!(
        host.restricted_calls(),
        restricted_loads_of(&["alice_shop0"])
    );
    assert!(host.loaded().is_empty());
}

/// A single-database restore of an imported archive whose dump is refused drops
/// nothing.
#[test]
fn an_imported_database_restore_with_a_refused_dump_fails_before_the_drop() {
    let fixture = Fixture::new(2);
    let host = imported_host(&fixture, 2, &[0]);
    let wanted = databases(2).remove(0);

    let error = run_scoped(
        &fixture,
        &host,
        &databases(2),
        &RestoreScope::Database(wanted),
        &mut sink(),
    )
    .unwrap_err();

    assert_eq!(
        error,
        BackupError::RestrictedLoadRefused {
            reason: DumpRefusal::ForbiddenStatement
        }
    );
    assert!(host.dropped().is_empty());
    assert!(host.restricted_calls().is_empty());
}

/// When an imported archive's load fails after the first drop, the databases are
/// put back from the panel's OWN rollback dumps, loaded as the superuser, and
/// not through the restricted loader: the only superuser loads in the run are
/// the rollback reloads.
#[test]
fn the_rollback_of_an_imported_restore_reloads_the_panels_own_dumps_as_the_superuser() {
    let fixture = Fixture::new(2);
    let host = imported_host(&fixture, 2, &[]);
    host.fail_restricted_load_for("alice_shop1");

    let error = run(&fixture, &host, &databases(2), &mut sink()).unwrap_err();

    assert_eq!(
        error,
        BackupError::RolledBack {
            failed: "alice_shop1".to_owned(),
            rolled_back: vec!["alice_shop1".to_owned(), "alice_shop0".to_owned()],
        }
    );
    assert_eq!(
        rollback_loads(&host),
        vec!["alice_shop1".to_owned(), "alice_shop0".to_owned()]
    );
    assert_eq!(
        host.loaded().len(),
        2,
        "every superuser load in an imported restore is a rollback reload: {:?}",
        host.loaded()
    );
    assert_eq!(
        host.restricted_calls(),
        restricted_loads_of(&["alice_shop0", "alice_shop1"])
    );
}
