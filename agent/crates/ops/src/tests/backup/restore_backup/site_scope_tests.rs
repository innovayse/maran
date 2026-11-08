//! A restore of one directory (a site) out of a backup.

use std::fs::read_to_string;

use maran_agent_core::validation::fs::relative_path::RelativePath;

use crate::backup::model::account_identity::AccountIdentity;
use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::recording_backup_host::{EXTRACT_PROGRAM, RecordingBackupHost};

use super::super::*;
use super::restore_fixture::{Fixture, LIVE_FILE, account, databases, host_with, run_scoped, sink};

/// The directory a site occupies in the fixture, as a single-directory restore
/// names it.
fn site_directory() -> RelativePath {
    RelativePath::parse("sites/shop.example.com").unwrap()
}

/// Makes `host`'s archive list the members of a backup that holds the site.
fn holds_the_site(host: &RecordingBackupHost) {
    host.holds_members(&[
        "manifest.json",
        "home/",
        "home/sites/",
        "home/sites/shop.example.com/",
        "home/sites/shop.example.com/index.php",
        "databases/",
    ]);
}

/// A single-directory restore extracts that directory as the account, swaps it by
/// itself and nothing else: no database is dropped, no dump is extracted, no
/// marker is written, and the live home's other files are left alone.
#[test]
fn a_directory_restore_replaces_that_directory_and_nothing_else() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);
    holds_the_site(&host);

    let outcome = run_scoped(
        &fixture,
        &host,
        &databases(2),
        &RestoreScope::Directory(site_directory()),
        &mut sink(),
    )
    .unwrap();

    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, 0);
    assert_eq!(outcome.databases_total, 0);
    assert!(host.dropped().is_empty());
    assert!(host.loaded().is_empty());

    let extractions = host.extractions();
    assert_eq!(
        extractions,
        vec![
            ("manifest.json".to_owned(), ExtractIdentity::Root),
            (
                "home/sites/shop.example.com".to_owned(),
                ExtractIdentity::Account(account())
            ),
        ]
    );

    let swaps = host.swaps();
    assert_eq!(swaps.len(), 1);
    assert_eq!(swaps[0].home, fixture.home());
    assert_eq!(swaps[0].staging, fixture.staging());
    assert_eq!(swaps[0].parked, fixture.previous());
    assert_eq!(swaps[0].directory, site_directory());

    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE,
        "the files outside the directory are exactly as the customer left them"
    );
    assert!(!fixture.marker().exists());
    assert!(!fixture.staging().exists(), "the staging tree is removed");
    assert!(
        !fixture.previous().exists(),
        "nothing is left parked after a good swap"
    );
}

/// The archiver is told to drop `home/` and every directory above the site, so the
/// site lands as the first entry of the staging directory under its own name.
#[test]
fn a_directory_restore_strips_the_leading_components_and_names_one_member() {
    let fixture = Fixture::new(0);
    let host = host_with(&fixture, 0);
    holds_the_site(&host);

    run_scoped(
        &fixture,
        &host,
        &[],
        &RestoreScope::Directory(site_directory()),
        &mut sink(),
    )
    .unwrap();

    let extraction = host.calls_to(EXTRACT_PROGRAM).pop().unwrap();
    assert!(
        extraction
            .iter()
            .any(|argument| argument == "--strip-components=2")
    );
    assert_eq!(extraction.last().unwrap(), "home/sites/shop.example.com");
}

/// A site the archive does not hold is refused before anything is staged or
/// extracted: the customer is told the backup predates the site, and the home is
/// not touched.
#[test]
fn a_directory_restore_of_a_site_the_archive_does_not_hold_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    host.holds_members(&["manifest.json", "home/", "home/sites/", "databases/"]);

    let error = run_scoped(
        &fixture,
        &host,
        &databases(1),
        &RestoreScope::Directory(site_directory()),
        &mut sink(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::DirectoryNotInBackup);
    assert!(host.swaps().is_empty());
    assert_eq!(host.extractions().len(), 1, "only the manifest was read");
    assert!(!fixture.staging().exists());
}

/// A member that merely starts with the site's name is not the site: the
/// comparison is on whole components.
#[test]
fn a_sibling_that_shares_a_prefix_with_the_site_is_not_the_site() {
    let fixture = Fixture::new(0);
    let host = host_with(&fixture, 0);
    host.holds_members(&[
        "manifest.json",
        "home/",
        "home/sites/",
        "home/sites/shop.example.com.evil/",
        "home/sites/shop.example.community/index.php",
    ]);

    let error = run_scoped(
        &fixture,
        &host,
        &[],
        &RestoreScope::Directory(site_directory()),
        &mut sink(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::DirectoryNotInBackup);
}

/// A swap the host refuses is a restore that changed nothing, and the parking
/// directory made for it is not left behind.
#[test]
fn a_directory_restore_whose_swap_is_refused_leaves_no_litter() {
    let fixture = Fixture::new(0);
    let host = host_with(&fixture, 0);
    holds_the_site(&host);
    host.fail_directory_swaps();

    let error = run_scoped(
        &fixture,
        &host,
        &[],
        &RestoreScope::Directory(site_directory()),
        &mut sink(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::StagingUnusable);
    assert!(!fixture.previous().exists());
    assert!(!fixture.staging().exists());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// A directory restore asks who the account is again before it moves anything,
/// for the reason the whole restore does: a uid read at the start may belong to
/// somebody else by now.
#[test]
fn a_directory_restore_whose_account_uid_moved_refuses_before_it_swaps() {
    let fixture = Fixture::new(0);
    let host = host_with(&fixture, 0);
    holds_the_site(&host);
    host.answers_identities(&[Some(AccountIdentity {
        uid: fixture.identity().uid + 1,
        gid: fixture.identity().gid,
    })]);

    let error = run_scoped(
        &fixture,
        &host,
        &[],
        &RestoreScope::Directory(site_directory()),
        &mut sink(),
    )
    .unwrap_err();

    assert!(
        matches!(error, BackupError::AccountIdentityChanged),
        "expected AccountIdentityChanged, got {error:?}"
    );
    assert!(host.swaps().is_empty());
}
