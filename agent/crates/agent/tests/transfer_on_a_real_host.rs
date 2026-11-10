//! Moving an account's backup between servers, observed against a real host (issue #127).
//!
//! This suite exists because the unit tests of `export_backup` and `import_backup` run against a recording
//! host and a hand-built archive, and three of the propositions that matter can be seen only on a real one:
//! that the bytes an export sends are the bytes of the artifact the real archiver wrote, that the digest the
//! signed manifest states is the one `sha256sum` computes over that file (a second, independent measurement),
//! and that an import is refused for an account whose system user exists in the real password database
//! (decision D1: the name must be free on the target; nothing is renamed).
//!
//! The signing key is the fixture PKCS#8 document beside this file. The package it signs is checked from outside:
//! the manifest names the account, the backup id and the artifact's size and digest, and the signature verifies
//! with the key's own public point (`openssl`, not the code under test).
//!
//! **What this does not observe, stated so it is not mistaken for coverage.** A completed import of a package
//! whose account does not exist yet, and the restore that follows it: that needs a second install, which is the
//! closing check of the issue (`docker/polygon/asserts/account-migration.sh`, run by hand on docker). A dump loaded
//! through the restricted loader is covered by the loader's own real-host cases, not here.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;

use std::fs::{create_dir_all, read, write};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::Arc;

use maran_agent::services::backup::db_host_catalog::DbHostCatalog;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::backup::model::backup_stage::BackupStage;
use maran_ops::backup::model::creation_request::CreationRequest;
use maran_ops::backup::model::progress_sink::ProgressSink;
use maran_ops::backup::package::package_signing_key::PackageSigningKey;
use maran_ops::backup::package::signed_manifest::SignedManifest;
use maran_ops::backup::{
    BackupError, ExportSink, ExportStopped, ImportInterrupted, ImportRequest, ImportSource,
    ProcessBackupHost, create_backup, export_backup, import_backup,
};
use maran_ops::db::ProcessDbHost;
use polygon_account::PolygonAccount;

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The id the backup in these cases is created under.
const FIXTURE_BACKUP_ID: &str = "7b1f6c0e-2d5a-4f43-9a37-5c1e0d9a2b11";

/// The string planted in a real file under the home; the exported bytes must carry it (the positive control for
/// "the export is the real archive": a stream that held nothing could not).
const PLANTED_MARKER: &str = "maran-transfer-planted-canary";

/// Refuses to go on unless this process is inside a polygon image.
///
/// # Panics
///
/// Panics when the polygon marker is absent: a skip of an `#[ignore]`d suite would report as a pass.
fn require_polygon() {
    let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
    assert!(
        !marker.is_empty(),
        "this suite runs the real archiver against a real host and must run inside a polygon container: \
         {POLYGON_MARKER} is not set. See docker/README.md."
    );
}

/// The adapter for the family this container actually is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The signing key every case uses: the fixture document beside this file.
fn fixture_key() -> PackageSigningKey {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("tests/fixtures/package_signing_key.pkcs8.der");
    PackageSigningKey::from_pkcs8(&read(&path).expect("the fixture key document"))
        .expect("a P-384 key")
}

/// The backup id these cases use, validated.
fn fixture_id() -> BackupId {
    BackupId::parse(FIXTURE_BACKUP_ID).expect("the fixture's id is a uuid")
}

/// The root every case writes into: the agent's own.
fn root() -> LocalBackupRoot {
    LocalBackupRoot::default()
}

/// The artifact `id` publishes under for `account`.
fn artifact_of(account: &AccountName, id: &BackupId) -> PathBuf {
    root()
        .as_path()
        .join(account.as_str())
        .join(format!("{}.tar.gz", id.as_str()))
}

/// The SHA-256 `sha256sum` reports for `path`: an independent measurement of the digest the manifest states.
fn sha256sum_of(path: &Path) -> String {
    let output = Command::new("sha256sum")
        .arg(path)
        .output()
        .expect("sha256sum runs on a polygon host");
    String::from_utf8_lossy(&output.stdout)
        .split_whitespace()
        .next()
        .unwrap_or_default()
        .to_owned()
}

/// Whether `signature` over `message` verifies with `public_point`, asked of `openssl` and not of the code under test.
fn openssl_verifies(public_point: &[u8], message: &[u8], signature: &[u8], scratch: &Path) -> bool {
    create_dir_all(scratch).expect("scratch directory");
    // An uncompressed SEC1 point wrapped in the fixed SubjectPublicKeyInfo header of a P-384 key.
    let mut spki = hex_to_bytes("3076301006072a8648ce3d020106052b81040022036200");
    spki.extend_from_slice(public_point);
    write(scratch.join("key.der"), &spki).expect("key file");
    write(scratch.join("message"), message).expect("message file");
    write(scratch.join("signature"), signature).expect("signature file");

    Command::new("openssl")
        .args(["dgst", "-sha384", "-verify"])
        .arg(scratch.join("key.der"))
        .args(["-keyform", "DER", "-signature"])
        .arg(scratch.join("signature"))
        .arg(scratch.join("message"))
        .output()
        .expect("openssl runs on a polygon host")
        .status
        .success()
}

/// Decodes a lowercase hexadecimal string.
fn hex_to_bytes(text: &str) -> Vec<u8> {
    (0..text.len())
        .step_by(2)
        .map(|index| u8::from_str_radix(&text[index..index + 2], 16).expect("hex"))
        .collect()
}

/// A sink that keeps the signed manifest and every chunk an export sends.
#[derive(Default)]
struct CollectingSink {
    /// The signed manifest, once the header arrived.
    signed: Option<SignedManifest>,
    /// The artifact bytes, in order.
    bytes: Vec<u8>,
}

impl ExportSink for CollectingSink {
    fn header(&mut self, signed: &SignedManifest) -> Result<(), ExportStopped> {
        self.signed = Some(signed.clone());
        Ok(())
    }

    fn chunk(&mut self, data: &[u8]) -> Result<(), ExportStopped> {
        self.bytes.extend_from_slice(data);
        Ok(())
    }
}

/// A source that hands an import the same bytes in slices.
struct SliceSource {
    /// The bytes still to hand over.
    remaining: Vec<u8>,
    /// The last slice handed out, kept alive for the borrow.
    current: Vec<u8>,
}

impl ImportSource for SliceSource {
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, ImportInterrupted> {
        if self.remaining.is_empty() {
            return Ok(None);
        }
        let take = self.remaining.len().min(64 * 1024);
        self.current = self.remaining.drain(..take).collect();
        Ok(Some(&self.current))
    }
}

/// A progress sink that ignores the stages.
struct QuietSink;

impl ProgressSink for QuietSink {
    fn report(&mut self, _stage: BackupStage, _percent: u32) {}
}

/// Removes everything a previous run left for `account`, without using the code under test to do it.
fn clear_backups(account: &AccountName) {
    let _ = std::fs::remove_dir_all(root().as_path().join(account.as_str()));
}

/// Makes a real backup of a real account whose home holds the planted marker, and exports it.
fn backup_and_export(account: &PolygonAccount) -> (SignedManifest, Vec<u8>) {
    clear_backups(account.name());
    write(account.home().join("index.html"), PLANTED_MARKER).expect("a file in the home");

    create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &DbHostCatalog::new(Arc::new(ProcessDbHost::new(polygon_distro()))),
        &CreationRequest {
            account: account.name(),
            backup_id: &fixture_id(),
            root: &root(),
            floor: &FreeSpaceFloor::from_mebibytes(1).expect("a one-mebibyte floor"),
            key: None,
        },
        &mut QuietSink,
    )
    .unwrap_or_else(|error| panic!("a backup of a real account must succeed: {error}"));

    let mut sink = CollectingSink::default();
    export_backup(
        account.name(),
        &fixture_id(),
        &root(),
        &fixture_key(),
        &mut sink,
    )
    .unwrap_or_else(|error| panic!("exporting a real backup must succeed: {error}"));

    (sink.signed.expect("the export sent a header"), sink.bytes)
}

#[test]
#[ignore = "creates a real account and a real artifact: polygon only"]
fn an_export_of_a_real_backup_sends_the_artifact_its_signed_manifest_describes() {
    require_polygon();
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyxferone");

    let (signed, bytes) = backup_and_export(&account);
    let on_disk = artifact_of(account.name(), &fixture_id());

    // The bytes sent are the file on disk, and a second tool agrees with the digest the manifest states.
    assert_eq!(
        bytes,
        read(&on_disk).expect("the artifact"),
        "the export is not the artifact byte for byte"
    );
    // Read as text and searched, not parsed: the manifest is the exact byte sequence that was signed.
    let manifest = String::from_utf8(signed.manifest.clone()).expect("the manifest is UTF-8 JSON");
    assert!(
        manifest.contains(account.name().as_str()),
        "the manifest does not name the account"
    );
    assert!(
        manifest.contains(FIXTURE_BACKUP_ID),
        "the manifest does not name the backup"
    );
    assert!(
        manifest.contains(&bytes.len().to_string()),
        "the manifest does not state the artifact's size"
    );
    assert!(
        manifest.contains(&sha256sum_of(&on_disk)),
        "the manifest's digest is not the one sha256sum computes"
    );

    // The positive control: the real archive carries the planted file, so a stream that held nothing could not pass.
    let listing = Command::new("tar")
        .arg("tzf")
        .arg(&on_disk)
        .output()
        .expect("tar runs");
    assert!(
        String::from_utf8_lossy(&listing.stdout).contains("index.html"),
        "the archive lacks the planted file"
    );

    // The signature verifies with the key's own point under a tool that is not the code under test.
    let scratch = std::env::temp_dir().join("maran-transfer-verify");
    assert!(
        openssl_verifies(
            &signed.public_key,
            &signed.manifest,
            &signed.signature,
            &scratch
        ),
        "the signature does not verify"
    );
    // The inverse control: one flipped bit in the manifest must NOT verify, or the check above could not fail.
    let mut tampered = signed.manifest.clone();
    tampered[0] ^= 0x01;
    assert!(
        !openssl_verifies(&signed.public_key, &tampered, &signed.signature, &scratch),
        "a tampered manifest verified"
    );
}

#[test]
#[ignore = "creates a real account and a real artifact: polygon only"]
fn an_export_names_the_account_it_was_asked_for_and_no_other() {
    require_polygon();
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyxfertwo");
    let other = PolygonAccount::create("polyxferoth");
    assert_ne!(
        account.ids().uid(),
        other.ids().uid(),
        "the premise of this case is two accounts, not one under two names"
    );
    backup_and_export(&account);

    let mut sink = CollectingSink::default();
    let outcome = export_backup(
        other.name(),
        &fixture_id(),
        &root(),
        &fixture_key(),
        &mut sink,
    );

    assert!(
        matches!(outcome, Err(BackupError::NotFound)),
        "another account's export of this id must be not found: {outcome:?}"
    );
    assert!(
        sink.signed.is_none() && sink.bytes.is_empty(),
        "something was sent for an account that owns no such backup"
    );
}

#[test]
#[ignore = "creates a real account and a real artifact: polygon only"]
fn an_import_is_refused_for_an_account_whose_system_user_exists_and_nothing_is_published() {
    require_polygon();
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyxferthr");
    let (signed, bytes) = backup_and_export(&account);

    // The same package, into a fresh id: the user exists in the real password database, so it is refused (D1).
    let before = std::fs::read_dir(root().as_path().join(account.name().as_str()))
        .expect("backup directory")
        .count();
    let outcome = import_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &ImportRequest {
            signed,
            expected_fingerprint: fixture_key().fingerprint(),
            max_bytes: 0,
        },
        &mut SliceSource {
            remaining: bytes,
            current: Vec::new(),
        },
        &root(),
        &FreeSpaceFloor::from_mebibytes(1).expect("a one-mebibyte floor"),
    );
    let after = std::fs::read_dir(root().as_path().join(account.name().as_str()))
        .expect("backup directory")
        .count();

    assert!(
        matches!(
            outcome,
            Err(BackupError::ImportAccountExists | BackupError::ImportBackupIdTaken)
        ),
        "an import for an existing account must be refused: {outcome:?}"
    );
    assert_eq!(
        before, after,
        "a refused import left something in the account's backup directory"
    );
}

#[test]
#[ignore = "creates a real account and a real artifact: polygon only"]
fn an_import_with_the_wrong_fingerprint_is_refused_before_anything_is_touched() {
    require_polygon();
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyxferfou");
    let (signed, bytes) = backup_and_export(&account);

    let outcome = import_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &ImportRequest {
            signed,
            expected_fingerprint: "0".repeat(64),
            max_bytes: 0,
        },
        &mut SliceSource {
            remaining: bytes,
            current: Vec::new(),
        },
        &root(),
        &FreeSpaceFloor::from_mebibytes(1).expect("a one-mebibyte floor"),
    );

    assert!(
        matches!(outcome, Err(BackupError::PackageFingerprintMismatch)),
        "a fingerprint the operator did not confirm must be refused: {outcome:?}"
    );
}
