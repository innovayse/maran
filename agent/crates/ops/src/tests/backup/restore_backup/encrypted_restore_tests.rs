//! A restore of a sealed archive, and the key it needs.

use std::fs::{read_to_string, write};

use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::archive::encrypt_artifact::encrypt_artifact;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::recording_backup_host::{LIST_PROGRAM, RecordingBackupHost};

use super::super::*;
use super::restore_fixture::{
    ARTIFACT_BYTES, Fixture, LIVE_FILE, account, backup_id, databases, digest, host_with, manifest,
    sink,
};

/// A key a test can name.
fn archive_key(byte: &str) -> ArchiveKey {
    ArchiveKey::parse(&byte.repeat(32)).expect("the fixture key is well-formed")
}

/// Replaces the fixture's published artifact with its sealed form under `key`,
/// republishes a sidecar describing the sealed bytes, and answers the digest of
/// those bytes — the one the panel would have recorded.
fn seal_fixture(fixture: &Fixture, count: usize, key: &ArchiveKey) -> String {
    let sealing = fixture.directory().join("sealing");
    encrypt_artifact(&fixture.artifact(), &sealing, key).unwrap();
    std::fs::rename(&sealing, fixture.artifact()).unwrap();
    let (sha256, bytes) = checksum_file(&fixture.artifact()).unwrap();
    let summary = BackupSummary::readable(
        backup_id().as_str().to_owned(),
        manifest(count),
        bytes,
        sha256.clone(),
    )
    .sealed();
    write(
        fixture.sidecar(),
        serde_json::to_vec_pretty(&summary).unwrap(),
    )
    .unwrap();

    sha256
}

/// Runs one whole restore over `fixture`, expecting `expected_sha256` and
/// holding `key`.
fn run_keyed(
    fixture: &Fixture,
    host: &RecordingBackupHost,
    count: usize,
    expected_sha256: &str,
    key: Option<&ArchiveKey>,
) -> Result<RestoreOutcome, BackupError> {
    restore_in(
        &fixture.placement(),
        host,
        &account(),
        &backup_id(),
        &fixture.directory(),
        &databases(count),
        &RestoreScope::Whole,
        expected_sha256,
        key,
        &mut sink(),
    )
}

/// **A sealed archive restores under its key, reading its members from the
/// decrypted copy** — and the copy does not outlive the restore.
#[test]
fn a_sealed_archive_restores_under_its_key_and_is_read_from_the_decrypted_copy() {
    let fixture = Fixture::new(1);
    let sealed_digest = seal_fixture(&fixture, 1, &archive_key("11"));
    let host = host_with(&fixture, 1);

    let outcome = run_keyed(&fixture, &host, 1, &sealed_digest, Some(&archive_key("11"))).unwrap();

    assert!(outcome.files_restored);
    let listed = host.calls_to(LIST_PROGRAM);
    assert!(!listed.is_empty());
    for call in &listed {
        assert!(
            call[1].starts_with(fixture.scratch().to_str().unwrap()),
            "the listing was asked of {}",
            call[1]
        );
    }
    assert!(
        !fixture.scratch().exists(),
        "the decrypted copy must not outlive the restore"
    );
}

/// **A sealed archive and no key leaves the account exactly as it was**, before
/// anything is extracted.
#[test]
fn a_sealed_archive_with_no_key_is_refused_and_the_account_is_untouched() {
    let fixture = Fixture::new(1);
    let sealed_digest = seal_fixture(&fixture, 1, &archive_key("11"));
    let host = host_with(&fixture, 1);

    let error = run_keyed(&fixture, &host, 1, &sealed_digest, None).unwrap_err();

    assert_eq!(error, BackupError::EncryptionKeyRequired);
    assert!(host.extractions().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(!fixture.scratch().exists());
}

/// The wrong key is refused in the same place, with the same guarantee.
#[test]
fn a_sealed_archive_under_the_wrong_key_is_refused_and_the_account_is_untouched() {
    let fixture = Fixture::new(1);
    let sealed_digest = seal_fixture(&fixture, 1, &archive_key("11"));
    let host = host_with(&fixture, 1);

    let error =
        run_keyed(&fixture, &host, 1, &sealed_digest, Some(&archive_key("22"))).unwrap_err();

    assert_eq!(error, BackupError::ArtifactDecryptionFailed);
    assert!(host.extractions().is_empty());
    assert_eq!(
        read_to_string(fixture.home().join("live.txt")).unwrap(),
        LIVE_FILE
    );
    assert!(!fixture.scratch().exists());
}

/// **The digest is still checked first, over the stored bytes.** A sealed
/// archive whose digest is not the panel's is refused as a checksum mismatch
/// before any key is tried — so a key is never spent on bytes the panel did not
/// take.
#[test]
fn a_sealed_archive_with_the_wrong_digest_is_refused_before_the_key_is_tried() {
    let fixture = Fixture::new(1);
    seal_fixture(&fixture, 1, &archive_key("11"));
    let host = host_with(&fixture, 1);

    let error = run_keyed(
        &fixture,
        &host,
        1,
        &digest("bytes the panel never took"),
        None,
    )
    .unwrap_err();

    assert_eq!(error, BackupError::ChecksumMismatch);
}

/// **The inverse control.** A plain archive restores exactly as it always did
/// when a key is in hand: every backup taken before archives could be sealed
/// stays restorable.
#[test]
fn a_plain_archive_restores_the_same_when_a_key_is_supplied() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);

    let outcome = run_keyed(
        &fixture,
        &host,
        1,
        &digest(ARTIFACT_BYTES),
        Some(&archive_key("11")),
    )
    .unwrap();

    assert!(outcome.files_restored);
    let listed = host.calls_to(LIST_PROGRAM);
    assert!(
        listed
            .iter()
            .all(|call| call[1] == fixture.artifact().to_string_lossy())
    );
}
