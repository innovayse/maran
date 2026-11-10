//! A whole restore: what it replaces, and what it refuses before it touches
//! anything.

use std::fs::{read_to_string, write};

use maran_agent_core::utils::floor_refusal::FloorRefusal;

use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::recording_backup_host::{ARCHIVE_PROGRAM, LIST_PROGRAM, RecordingBackupHost};
use crate::backup::recording_jail_mounts::RecordingJailMounts;

use super::super::*;
use super::restore_fixture::{
    ARTIFACT_BYTES, Fixture, LIVE_FILE, account, backup_id, databases, digest, host_with, manifest,
    run, run_with_jails, sink,
};

/// A whole restore reports every database restored and the files swapped — the
/// inverse control, without which every refusal below would pass just as well
/// against an operation that refused everything.
#[test]
fn a_whole_restore_swaps_the_home_and_replaces_every_database() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);

    let outcome = run(&fixture, &host, &databases(2), &mut sink()).unwrap();

    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, 2);
    assert_eq!(outcome.databases_total, 2);
    assert!(fixture.home().join("public").join("index.php").is_file());
    assert!(!fixture.home().join("live.txt").exists());
    assert!(!fixture.previous().exists());
    assert!(!fixture.scratch().exists());
}

/// Issue #487: the file-transfer jails are a bind mount of the displaced home's
/// inode, so the restore mounts them onto the new one — and does it while the
/// parked tree still exists, because removing it first would empty what a stale
/// jail shows.
#[test]
fn a_whole_restore_mounts_the_jails_onto_the_new_home_before_the_parked_tree_goes() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let jails = RecordingJailMounts::new().watching(&fixture.previous());

    run_with_jails(&fixture, &host, &databases(1), &jails, &mut sink()).unwrap();

    assert_eq!(jails.refreshed(), vec!["alice".to_owned()]);
    assert_eq!(
        jails.watched_existed(),
        vec![true],
        "the displaced home must still be on disk when the jails are mounted again"
    );
    assert!(
        !fixture.previous().exists(),
        "and it is removed once they are"
    );
}

/// A jail that cannot be mounted onto the new home keeps the displaced tree and
/// the restore says so; the customer's files are nonetheless restored.
#[test]
fn a_jail_that_cannot_follow_the_new_home_keeps_the_parked_tree_and_fails_the_restore() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let jails = RecordingJailMounts::new().failing();

    let error = run_with_jails(&fixture, &host, &databases(1), &jails, &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::JailsNotRefreshed);
    assert!(
        fixture.home().join("public").join("index.php").is_file(),
        "the restored files are in place"
    );
    assert!(
        fixture.previous().join("live.txt").is_file(),
        "the displaced home is kept so a stale jail does not show an emptied tree"
    );
}

/// The inverse control for the two above: a restore that refuses before the
/// swap never touches a jail.
#[test]
fn a_restore_refused_before_the_swap_mounts_no_jail() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let jails = RecordingJailMounts::new();

    let error = run_with_jails(&fixture, &host, &[], &jails, &mut sink());

    assert!(error.is_err());
    assert!(jails.refreshed().is_empty(), "{:?}", jails.refreshed());
}

/// A member the archive's layout has no place for is refused before anything at
/// all is unpacked.
#[test]
fn a_hostile_archive_member_outside_home_and_databases_is_refused_before_anything_is_touched() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    host.holds_members(&[
        "manifest.json",
        "home/",
        "home/public/index.php",
        "../../etc/cron.d/pwn",
    ]);

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    match error {
        BackupError::UnexpectedArchiveMember { name } => {
            assert!(name.contains("cron.d/pwn"), "{name}");
        }
        other => panic!("expected a refused member, got {other:?}"),
    }
    // Not one member was unpacked — not even the ones that looked ordinary.
    assert!(host.extractions().is_empty());
    assert!(host.dropped().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// Below the free-space floor a restore extracts nothing and touches no
/// database; the filesystem asked is the home root's (issue #148).
#[test]
fn a_restore_below_the_free_space_floor_is_refused_before_anything_is_extracted() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    host.has_available(0);

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert!(
        matches!(
            error,
            BackupError::FreeSpace(FloorRefusal::BelowFloor { available: 0, .. })
        ),
        "{error:?}"
    );
    assert_eq!(host.measured(), vec![fixture.placement().home_root]);
    assert!(host.extractions().is_empty());
    assert!(host.dropped().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// An artifact that is not the one the panel recorded is refused before the
/// archive is opened for any purpose — and therefore before the caller's
/// pre-restore backup would ever be worth taking.
#[test]
fn an_archive_whose_checksum_does_not_match_is_refused_before_the_pre_restore_backup() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);

    let error = restore_in(
        &fixture.placement(),
        &host,
        &account(),
        &backup_id(),
        &fixture.directory(),
        &databases(1),
        &RestoreScope::Whole,
        &digest("bytes this panel never took"),
        None,
        &RecordingJailMounts::new(),
        &mut sink(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::ChecksumMismatch);
    assert!(host.calls_to(LIST_PROGRAM).is_empty());
    assert!(host.extractions().is_empty());
}

/// The cheap copy of the manifest cannot be edited into a restore: the copy
/// inside the archive is the authority and the two are compared.
#[test]
fn a_manifest_that_disagrees_with_the_sidecar_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let mut edited = manifest(1);
    edited.databases.clear();
    fixture.publish_sidecar(&edited);

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::ManifestDisagreesWithSidecar);
    assert!(host.dropped().is_empty());
}

/// **The case a naive parse-only check cannot see, reaching restore.** This
/// sidecar's JSON parses cleanly into today's `BackupSummary` shape — the
/// manifest and the digest are byte-for-byte the ones the archive and the
/// checksum agree on — and the only thing wrong with it is a `version` this
/// agent has never heard of. `list_backups` already refuses this exact
/// document as unreadable; before `require_sidecar_agrees` went through
/// `read_sidecar`, `restore_backup` accepted it, because it never looked at
/// `version` at all. It must now be refused here too.
#[test]
fn a_sidecar_naming_an_unknown_version_is_refused_even_when_it_otherwise_agrees() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let unknown_version = crate::backup::model::backup_summary::SUMMARY_VERSION + 1;

    let agreeing_but_unknown_version = serde_json::json!({
        "backup_id": backup_id().as_str(),
        "readable": true,
        "reason": null,
        "version": unknown_version,
        "manifest": manifest(1),
        "artifact_bytes": ARTIFACT_BYTES.len() as u64,
        "artifact_sha256": digest(ARTIFACT_BYTES),
    });
    write(
        fixture.sidecar(),
        serde_json::to_vec_pretty(&agreeing_but_unknown_version).unwrap(),
    )
    .unwrap();

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(error, BackupError::ManifestDisagreesWithSidecar);
    assert!(host.dropped().is_empty());
}

/// The inverse control a refusing gate needs: a sidecar at today's version,
/// agreeing with the manifest, still restores — the check above is a version
/// gate, not a check that now refuses everything.
#[test]
fn a_sidecar_at_the_current_version_still_restores() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);

    let outcome = run(&fixture, &host, &databases(1), &mut sink()).unwrap();

    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, 1);
}

/// A database the panel no longer knows about is refused, and nothing creates
/// it — an orphan owned by a user nothing points at is worse than a refusal.
#[test]
fn a_database_the_panel_does_not_know_is_refused_and_never_created() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);

    // The panel knows only the first of the two the archive carries.
    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    match error {
        BackupError::UnknownDatabase { name } => assert_eq!(name, "alice_shop1"),
        other => panic!("expected an unknown database, got {other:?}"),
    }
    assert!(host.dropped().is_empty());
    assert!(host.loaded().is_empty());
}

/// A dump that is not the one this backup wrote is refused BEFORE any drop,
/// because after the drop there is nothing left to refuse into.
#[test]
fn a_dump_whose_checksum_does_not_match_is_refused_before_any_drop() {
    let fixture = Fixture::new(1);
    let host = RecordingBackupHost::new();
    host.holds_manifest(manifest(1));
    host.holds_dump(databases(1)[0].as_str(), "not the dump this backup took");

    let error = run(&fixture, &host, &databases(1), &mut sink()).unwrap_err();

    assert_eq!(
        error,
        BackupError::DumpChecksumMismatch {
            database: "alice_shop0".to_owned()
        }
    );
    assert!(host.dropped().is_empty());
}

/// A scratch that cannot hold the rollback dumps is refused before the first
/// drop, not discovered halfway through one.
///
/// The manifest's own byte counts are what the pre-flight adds up — they are
/// the only sizes known before the dumps are taken — so a manifest claiming
/// impossible dumps is how a unit test reaches a filesystem it cannot fill.
#[test]
fn a_scratch_too_small_for_the_rollback_dumps_is_refused_before_any_database_is_dropped() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);
    let mut enormous = manifest(2);
    for database in &mut enormous.databases {
        database.bytes = u64::MAX / 2;
    }
    host.holds_manifest(enormous.clone());
    fixture.publish_sidecar(&enormous);

    let error = run(&fixture, &host, &databases(2), &mut sink()).unwrap_err();

    assert!(
        matches!(error, BackupError::ScratchTooSmall { .. }),
        "the restore must refuse for want of room, answered: {error:?}"
    );
    // The property the refusal exists for: it happened while everything was
    // still there to refuse for.
    assert!(host.dropped().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
}

/// The home is extracted as the account and the dumps are not — R3 and R4, read
/// off the host's own log of who ran what.
#[test]
fn the_home_is_extracted_inside_fork_as_account_and_the_dumps_are_not() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);

    run(&fixture, &host, &databases(1), &mut sink()).unwrap();

    assert_eq!(
        host.extractions(),
        vec![
            ("manifest.json".to_owned(), ExtractIdentity::Root),
            ("databases".to_owned(), ExtractIdentity::Root),
            ("home".to_owned(), ExtractIdentity::Account(account())),
        ]
    );
}

/// Progress reports never go backwards, and everything whose failure leaves the
/// account untouched is reported under `verifying`.
#[test]
fn every_step_that_is_recoverable_by_doing_nothing_is_reported_as_verifying() {
    let fixture = Fixture::new(2);
    let host = host_with(&fixture, 2);
    let mut sink = sink();

    run(&fixture, &host, &databases(2), &mut sink).unwrap();

    let mut previous = 0;
    for (_stage, percent) in &sink.reports {
        assert!(*percent >= previous, "{:?}", sink.reports);
        previous = *percent;
    }
    let last_verifying = sink
        .reports
        .iter()
        .rposition(|(stage, _)| *stage == RestoreStage::Verifying)
        .expect("the verifying stage is reported");
    let first_drop_report = sink
        .reports
        .iter()
        .position(|(stage, _)| *stage == RestoreStage::RestoringDatabases)
        .expect("the database stage is reported");
    assert!(last_verifying < first_drop_report);
    // No archiver run is reported by a restore: it creates nothing.
    assert!(host.calls_to(ARCHIVE_PROGRAM).is_empty());
}
