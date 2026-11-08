//! The single-database restore's pairing of the wanted database with the manifest.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::MANIFEST_VERSION;

use super::*;

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// A database of the fixture account.
fn database(name: &str) -> DatabaseName {
    DatabaseName::for_account(&account(), name).expect("the fixture name is valid")
}

/// A manifest naming `names`.
fn manifest(names: &[&str]) -> BackupManifest {
    BackupManifest {
        version: MANIFEST_VERSION,
        account: "alice".to_owned(),
        backup_id: "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b".to_owned(),
        created_at_unix: 0,
        home_bytes: 0,
        databases: names
            .iter()
            .map(|name| ManifestDatabase {
                name: database(name).as_str().to_owned(),
                bytes: 1,
                sha256: "0".repeat(64),
            })
            .collect(),
        agent_version: "0.0.0".to_owned(),
    }
}

/// The inverse control: a database the panel knows and the manifest names
/// resolves to the panel's own name and the manifest's entry for it.
#[test]
fn a_known_database_the_manifest_names_resolves() {
    let (name, entry) = resolve_one_database(
        &manifest(&["shop", "blog"]),
        &[database("shop"), database("blog")],
        &database("blog"),
    )
    .unwrap();

    assert_eq!(name, database("blog"));
    assert_eq!(entry.name, "alice_blog");
}

/// A database the panel has forgotten is refused whatever the manifest holds: the
/// panel's rows decide what exists.
#[test]
fn a_database_the_panel_does_not_know_is_refused_even_when_the_manifest_names_it() {
    let error = resolve_one_database(
        &manifest(&["shop", "blog"]),
        &[database("shop")],
        &database("blog"),
    )
    .unwrap_err();

    assert_eq!(
        error,
        BackupError::UnknownDatabase {
            name: "alice_blog".to_owned()
        }
    );
}

/// A known database the manifest does not name was created after the backup, and
/// is refused as a finding about the backup.
#[test]
fn a_known_database_the_manifest_does_not_name_is_not_in_the_backup() {
    let error = resolve_one_database(
        &manifest(&["shop"]),
        &[database("shop"), database("blog")],
        &database("blog"),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::DatabaseNotInBackup);
}

/// Other manifest entries the panel does not know do not matter to a
/// single-database restore: only the wanted one is read.
#[test]
fn a_manifest_entry_for_another_unknown_database_is_not_this_restores_business() {
    let resolved = resolve_one_database(
        &manifest(&["shop", "ghost"]),
        &[database("shop")],
        &database("shop"),
    );

    assert!(resolved.is_ok());
}
