//! One directory comes out as the account, and only when the archive holds it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::MetadataExt as _;
use std::path::PathBuf;

use maran_agent_core::utils::current_uid::current_uid;
use tempfile::TempDir;

use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::recording_backup_host::RecordingBackupHost;

use super::*;

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The directory a restore asks for.
fn site() -> RelativePath {
    RelativePath::parse("sites/shop.example.com").unwrap()
}

/// The members of an archive holding the site.
fn members_with_the_site() -> Vec<String> {
    [
        "manifest.json",
        "home/",
        "home/sites/",
        "home/sites/shop.example.com/",
        "home/sites/shop.example.com/index.php",
        "databases/",
    ]
    .iter()
    .map(|member| (*member).to_owned())
    .collect()
}

/// The identity the staging directory is handed to: the current user and the
/// group of the directory it is made in, which the current user can really apply.
fn owner(root: &TempDir) -> AccountIdentity {
    AccountIdentity {
        uid: current_uid().unwrap(),
        gid: root.path().symlink_metadata().unwrap().gid(),
    }
}

/// Runs one extraction into a fresh staging directory under a fresh root.
fn extract(
    members: &[String],
) -> (
    TempDir,
    RecordingBackupHost,
    Result<(), BackupError>,
    PathBuf,
) {
    let root = TempDir::new().unwrap();
    let staging = root.path().join("alice.one");
    let host = RecordingBackupHost::new();

    let answer = extract_directory_as_account(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        members,
        &staging,
        &account(),
        &site(),
        &owner(&root),
    );

    (root, host, answer, staging)
}

/// The inverse control: an archive holding the site is extracted, once, as the
/// account, into the staging directory it was told.
#[test]
fn an_archive_holding_the_site_is_extracted_as_the_account() {
    let (_root, host, answer, staging) = extract(&members_with_the_site());

    assert_eq!(answer, Ok(()));
    assert_eq!(
        host.extractions(),
        vec![(
            "home/sites/shop.example.com".to_owned(),
            ExtractIdentity::Account(account())
        )]
    );
    assert!(staging.join("shop.example.com").join("index.php").is_file());
}

/// An archive without the site is refused, and nothing is created: the staging
/// directory is not made and the archiver is not run.
#[test]
fn an_archive_without_the_site_is_refused_before_anything_is_created() {
    let members: Vec<String> = ["manifest.json", "home/", "home/sites/", "databases/"]
        .iter()
        .map(|member| (*member).to_owned())
        .collect();

    let (_root, host, answer, staging) = extract(&members);

    assert_eq!(answer, Err(BackupError::DirectoryNotInBackup));
    assert!(host.extractions().is_empty());
    assert!(!staging.exists());
}

/// A directory entry alone is enough: an empty site directory is a site.
#[test]
fn an_empty_directory_entry_is_enough_to_hold_the_site() {
    let members: Vec<String> = ["manifest.json", "home/", "home/sites/shop.example.com/"]
        .iter()
        .map(|member| (*member).to_owned())
        .collect();

    let (_root, _host, answer, _staging) = extract(&members);

    assert_eq!(answer, Ok(()));
}

/// Whole components, never a text prefix: a neighbour whose name starts with the
/// site's is not the site.
#[test]
fn a_neighbour_sharing_a_prefix_does_not_count_as_the_site() {
    let members: Vec<String> = [
        "manifest.json",
        "home/sites/shop.example.com.evil/",
        "home/sites/shop.example.community/index.php",
    ]
    .iter()
    .map(|member| (*member).to_owned())
    .collect();

    let (_root, _host, answer, _staging) = extract(&members);

    assert_eq!(answer, Err(BackupError::DirectoryNotInBackup));
}
