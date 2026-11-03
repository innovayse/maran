//! Verifying a published artifact: what a read establishes, and that every way
//! an artifact can be wrong is a typed refusal and never an `Ok`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{
    DirBuilder, Permissions, create_dir_all, hard_link, read, remove_file, set_permissions, write,
};
use std::os::unix::fs::{DirBuilderExt as _, PermissionsExt as _, symlink};
use std::path::{Path, PathBuf};

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::utils::floor_refusal::FloorRefusal;
use sha2::{Digest as _, Sha256};
use tempfile::TempDir;

use crate::backup::MANIFEST_VERSION;
use crate::backup::model::account_identity::AccountIdentity;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::model::extract_spec::ExtractSpec;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::model::unreadable_reason::UnreadableReason;
use crate::backup::recording_backup_host::{
    DROP_PROGRAM, DUMP_PROGRAM, FIXED_NOW, LOAD_PROGRAM, RecordingBackupHost,
};

use super::*;

/// The bytes the fixture's artifact holds.
const ARTIFACT_BYTES: &str = "gzip-bytes";

/// A manifest member larger than the ceiling `read_manifest` applies (4 MiB),
/// by a clear margin so that the test does not restate the constant.
const OVERSIZED_MANIFEST_BYTES: u64 = 5 * 1024 * 1024;

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// A second account, for the cases that name the wrong one.
fn other_account() -> AccountName {
    AccountName::parse("bobby").expect("the fixture name is valid")
}

/// The backup id every fixture uses.
fn backup_id() -> BackupId {
    BackupId::parse("3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b").expect("the fixture id is valid")
}

/// A different backup id.
fn other_backup_id() -> BackupId {
    BackupId::parse("9b8a7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d").expect("the fixture id is valid")
}

/// `count` databases of this account, named the way the panel names them.
fn databases(count: usize) -> Vec<DatabaseName> {
    let account = account();
    (0..count)
        .map(|index| {
            DatabaseName::for_account(&account, &format!("shop{index}"))
                .expect("the fixture name is valid")
        })
        .collect()
}

/// The dump the fixture's archive carries for `database`.
fn dump_of(database: &DatabaseName) -> String {
    format!("-- dump of {}\n", database.as_str())
}

/// The lowercase hex SHA-256 of `bytes`.
fn digest(bytes: &[u8]) -> String {
    let mut hasher = Sha256::new();
    hasher.update(bytes);
    hasher
        .finalize()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

/// The manifest the fixture's archive carries for `count` databases.
fn manifest(count: usize) -> BackupManifest {
    BackupManifest {
        version: MANIFEST_VERSION,
        account: account().as_str().to_owned(),
        backup_id: backup_id().as_str().to_owned(),
        created_at_unix: FIXED_NOW,
        home_bytes: 10,
        databases: databases(count)
            .iter()
            .map(|database| ManifestDatabase {
                name: database.as_str().to_owned(),
                bytes: dump_of(database).len() as u64,
                sha256: digest(dump_of(database).as_bytes()),
            })
            .collect(),
        agent_version: "0.0.0".to_owned(),
    }
}

/// The member names a well-formed archive of `count` databases lists.
fn members(count: usize) -> Vec<String> {
    let mut names = vec![
        "manifest.json".to_owned(),
        "home/".to_owned(),
        "home/public/index.php".to_owned(),
        "databases/".to_owned(),
    ];
    names.extend(
        databases(count)
            .iter()
            .map(|database| format!("databases/{}.sql", database.as_str())),
    );

    names
}

/// The directories one verification works over, kept alive for a test.
struct Fixture {
    /// The stand-in for the scratch root.
    scratch_root: TempDir,
    /// The stand-in for the configured backup root.
    backup_root: TempDir,
}

impl Fixture {
    /// Builds the two trees and a published artifact with its sidecar, as a
    /// backup of `count` databases leaves them.
    fn new(count: usize) -> Self {
        let fixture = Self {
            scratch_root: TempDir::new().unwrap(),
            backup_root: TempDir::new().unwrap(),
        };

        // The chain check holds the scratch root to 0700, and a TempDir is
        // created with the umask's mode; set it here so the fixture presents
        // the chain a real host presents rather than weakening the check.
        set_permissions(fixture.scratch_root.path(), Permissions::from_mode(0o700)).unwrap();

        DirBuilder::new()
            .mode(0o700)
            .create(fixture.directory())
            .unwrap();
        write(fixture.artifact(), ARTIFACT_BYTES.as_bytes()).unwrap();
        fixture.publish_sidecar(&manifest(count));

        fixture
    }

    /// Writes the sidecar describing `manifest` and the fixture's artifact.
    fn publish_sidecar(&self, manifest: &BackupManifest) {
        self.publish_summary(&BackupSummary::readable(
            backup_id().as_str().to_owned(),
            manifest.clone(),
            ARTIFACT_BYTES.len() as u64,
            digest(ARTIFACT_BYTES.as_bytes()),
        ));
    }

    /// Writes `summary` as the sidecar, replacing whatever is there.
    fn publish_summary(&self, summary: &BackupSummary) {
        write(self.sidecar(), serde_json::to_vec_pretty(summary).unwrap()).unwrap();
    }

    /// The account's backup directory.
    fn directory(&self) -> PathBuf {
        self.backup_root.path().join("alice")
    }

    /// The published artifact's path.
    fn artifact(&self) -> PathBuf {
        self.directory().join(artifact_file_name(&backup_id()))
    }

    /// The sidecar's path.
    fn sidecar(&self) -> PathBuf {
        self.directory().join(sidecar_file_name(&backup_id()))
    }

    /// Where this verification stages the manifest.
    fn scratch(&self) -> PathBuf {
        self.scratch_root
            .path()
            .join("verify")
            .join(backup_id().as_str())
    }

    /// The placement one run uses, owned by the uid a test really runs as.
    fn placement(&self) -> VerificationPlacement {
        VerificationPlacement {
            scratch_base: self.scratch_root.path().to_path_buf(),
            scratch_root: self.scratch_root.path().to_path_buf(),
            scratch: self.scratch(),
            owner: current_uid().unwrap(),
            floor: FreeSpaceFloor::from_mebibytes(1).unwrap(),
        }
    }
}

/// A host whose archive lists `count` databases' dumps and carries a manifest
/// describing them.
fn host_with(count: usize) -> RecordingBackupHost {
    let host = RecordingBackupHost::new();
    let names = members(count);
    let borrowed: Vec<&str> = names.iter().map(String::as_str).collect();
    host.holds_members(&borrowed);
    host.holds_manifest(manifest(count));

    host
}

/// Runs one verification of the fixture's own backup.
fn run(
    fixture: &Fixture,
    host: &dyn BackupHost,
) -> Result<crate::backup::BackupVerification, BackupError> {
    verify_in(
        &fixture.placement(),
        host,
        &account(),
        &backup_id(),
        &fixture.directory(),
    )
}

/// Flips every bit of the first byte of `path`, keeping its length and mode.
fn flip_first_byte(path: &Path) {
    let mut bytes = read(path).unwrap();
    bytes[0] ^= 0xff;
    write(path, bytes).unwrap();
}

/// What a planting host does to the manifest an extraction leaves behind.
enum Plant {
    /// Replace it with a symbolic link to this file, which holds a manifest
    /// that would be accepted if read.
    SymbolicLink(PathBuf),
    /// Replace it with a second name for this file, which holds a manifest that
    /// would be accepted if read.
    HardLink(PathBuf),
    /// Stretch it past the largest manifest this agent reads.
    Oversize,
}

/// A host that behaves as [`RecordingBackupHost`] does, except that the
/// manifest an extraction leaves in the scratch is then replaced by what a
/// hostile archive's member of that name would have extracted as.
///
/// The fake writes a regular file whatever the archive "holds", so without this
/// a link member could not be exhibited at all: the check that refuses one
/// would be tested only against input it never sees.
struct PlantingHost {
    /// The host every call is delegated to.
    inner: RecordingBackupHost,
    /// What is done to the extracted manifest.
    plant: Plant,
}

impl BackupHost for PlantingHost {
    fn now_unix(&self) -> i64 {
        self.inner.now_unix()
    }

    fn account_identity(&self, account: &AccountName) -> Result<AccountIdentity, BackupError> {
        self.inner.account_identity(account)
    }

    fn dump_database(&self, database: &DatabaseName, into: &Path) -> Result<u64, BackupError> {
        self.inner.dump_database(database, into)
    }

    fn create_archive(&self, spec: &ArchiveSpec) -> Result<u64, BackupError> {
        self.inner.create_archive(spec)
    }

    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error> {
        self.inner.available_bytes(directory)
    }

    fn list_members(&self, artifact: &Path) -> Result<Vec<String>, BackupError> {
        self.inner.list_members(artifact)
    }

    fn extract(&self, spec: &ExtractSpec) -> Result<(), BackupError> {
        self.inner.extract(spec)?;

        if matches!(spec.part, ArchivePart::Manifest) {
            let extracted = spec.into.join("manifest.json");
            remove_file(&extracted).unwrap();
            match &self.plant {
                Plant::SymbolicLink(target) => symlink(target, &extracted).unwrap(),
                Plant::HardLink(target) => hard_link(target, &extracted).unwrap(),
                Plant::Oversize => {
                    let file = std::fs::File::create(&extracted).unwrap();
                    file.set_len(OVERSIZED_MANIFEST_BYTES).unwrap();
                }
            }
        }

        Ok(())
    }

    fn drop_database(&self, database: &DatabaseName) -> Result<(), BackupError> {
        self.inner.drop_database(database)
    }

    fn load_dump(&self, database: &DatabaseName, from: &Path) -> Result<(), BackupError> {
        self.inner.load_dump(database, from)
    }
}

/// A file outside the scratch holding a manifest a verification would accept
/// if it were ever read through a link.
fn acceptable_manifest_elsewhere(count: usize) -> (TempDir, PathBuf) {
    let elsewhere = TempDir::new().unwrap();
    let path = elsewhere.path().join("elsewhere.json");
    write(&path, serde_json::to_vec_pretty(&manifest(count)).unwrap()).unwrap();

    (elsewhere, path)
}

/// A sound artifact verifies, and says what it established: the inverse control
/// without which every refusal below would pass just as well against an
/// operation that refused everything.
#[test]
fn a_sound_artifact_verifies_and_reports_what_it_read() {
    let fixture = Fixture::new(2);
    let host = host_with(2);

    let verified = run(&fixture, &host).unwrap();

    assert_eq!(
        verified.member_count,
        u32::try_from(members(2).len()).unwrap()
    );
    assert_eq!(verified.database_count, 2);
    assert_eq!(
        verified.artifact_bytes,
        u64::try_from(ARTIFACT_BYTES.len()).unwrap()
    );
}

/// An account with no database verifies with a measured zero, not a refusal.
#[test]
fn a_sound_artifact_with_no_database_verifies_with_a_zero_count() {
    let fixture = Fixture::new(0);
    let host = host_with(0);

    let verified = run(&fixture, &host).unwrap();

    assert_eq!(verified.database_count, 0);
}

/// A byte flipped in the artifact is a checksum mismatch, whatever the listing
/// and the manifest would still say.
#[test]
fn a_flipped_byte_in_the_artifact_is_a_checksum_mismatch() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    flip_first_byte(&fixture.artifact());

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ChecksumMismatch);
    assert!(
        host.calls().is_empty(),
        "nothing may be read out of an artifact whose digest does not match"
    );
}

/// An artifact cut short is refused, and not as a smaller verified artifact.
#[test]
fn a_truncated_artifact_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    write(fixture.artifact(), &ARTIFACT_BYTES.as_bytes()[..4]).unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ChecksumMismatch);
}

/// An artifact with bytes appended is refused for the same reason.
#[test]
fn an_artifact_with_bytes_appended_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    write(fixture.artifact(), format!("{ARTIFACT_BYTES}tail")).unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ChecksumMismatch);
}

/// A sidecar whose size disagrees while its digest still matches is a sidecar
/// written wrong, and is refused as a size mismatch.
#[test]
fn a_sidecar_recording_another_size_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    fixture.publish_summary(&BackupSummary::readable(
        backup_id().as_str().to_owned(),
        manifest(1),
        ARTIFACT_BYTES.len() as u64 + 1,
        digest(ARTIFACT_BYTES.as_bytes()),
    ));

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ArtifactSizeMismatch);
}

/// A missing sidecar is a typed refusal and never an empty ok.
#[test]
fn a_missing_sidecar_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    remove_file(fixture.sidecar()).unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::SidecarUnreadable);
}

/// A sidecar that is not JSON is refused.
#[test]
fn an_unparsable_sidecar_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    write(fixture.sidecar(), b"{ not json").unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::SidecarUnreadable);
}

/// A sidecar of a version this agent does not understand is refused.
#[test]
fn a_sidecar_of_an_unknown_version_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let mut summary = BackupSummary::readable(
        backup_id().as_str().to_owned(),
        manifest(1),
        ARTIFACT_BYTES.len() as u64,
        digest(ARTIFACT_BYTES.as_bytes()),
    );
    summary.version = Some(99);
    fixture.publish_summary(&summary);

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::SidecarUnreadable);
}

/// A sidecar that describes itself as unreadable is refused: the record cannot
/// be believed, so there is nothing to compare the artifact with.
#[test]
fn a_sidecar_that_describes_itself_as_unreadable_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    fixture.publish_summary(&BackupSummary::unreadable(
        backup_id().as_str().to_owned(),
        UnreadableReason::Corrupt,
    ));

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::SidecarUnreadable);
}

/// A digest that is not 64 lowercase hexadecimal characters is a sidecar that
/// cannot be trusted, and not a mismatch of an unspecified kind.
#[test]
fn a_malformed_digest_in_the_sidecar_is_a_sidecar_refusal_and_not_a_mismatch() {
    for malformed in [
        digest(ARTIFACT_BYTES.as_bytes()).to_uppercase(),
        digest(ARTIFACT_BYTES.as_bytes())[..63].to_owned(),
        "z".repeat(64),
        String::new(),
    ] {
        let fixture = Fixture::new(1);
        let host = host_with(1);
        fixture.publish_summary(&BackupSummary::readable(
            backup_id().as_str().to_owned(),
            manifest(1),
            ARTIFACT_BYTES.len() as u64,
            malformed.clone(),
        ));

        let error = run(&fixture, &host).unwrap_err();

        assert_eq!(error, BackupError::SidecarUnreadable, "{malformed:?}");
    }
}

/// A sidecar whose manifest says something other than the archive's manifest
/// is refused, even though the artifact's own digest matches.
#[test]
fn a_sidecar_that_disagrees_with_the_manifest_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let mut elsewhere = manifest(1);
    elsewhere.home_bytes += 1;
    fixture.publish_sidecar(&elsewhere);

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ManifestDisagreesWithSidecar);
}

/// The two files describing different databases is the same refusal.
#[test]
fn a_sidecar_listing_other_databases_than_the_manifest_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    fixture.publish_sidecar(&manifest(2));

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ManifestDisagreesWithSidecar);
}

/// A manifest naming a database whose dump is not among the members is refused:
/// a smaller count would be an ok about something that was not read.
#[test]
fn a_manifest_naming_a_database_whose_dump_is_missing_is_refused() {
    let fixture = Fixture::new(2);
    let host = host_with(2);
    let first_dump = format!("databases/{}.sql", databases(2)[0].as_str());
    let present: Vec<String> = members(2)
        .into_iter()
        .filter(|name| *name != first_dump)
        .collect();
    let borrowed: Vec<&str> = present.iter().map(String::as_str).collect();
    host.holds_members(&borrowed);

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::DumpMissing);
}

/// A manifest naming a database no account could own is refused before the
/// name is used for anything, and the error carries no part of it.
#[test]
fn a_manifest_naming_a_database_this_account_could_not_own_is_refused() {
    for name in ["bobby_shop", "../../etc/passwd", "alice_", ""] {
        let fixture = Fixture::new(1);
        let mut hostile = manifest(1);
        hostile.databases[0].name = name.to_owned();
        fixture.publish_sidecar(&hostile);
        let host = host_with(1);
        host.holds_manifest(hostile);

        let error = run(&fixture, &host).unwrap_err();

        assert_eq!(error, BackupError::ManifestUnreadable, "{name:?}");
        assert!(!error.to_string().contains("passwd"), "{error}");
    }
}

/// A member outside the archive's layout is refused, entire, before the
/// manifest is extracted.
#[test]
fn a_member_that_walks_up_out_of_the_archive_is_refused_before_anything_is_extracted() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    host.holds_members(&[
        "manifest.json",
        "home/",
        "home/../../etc/cron.d/pwn",
        "databases/",
    ]);

    let error = run(&fixture, &host).unwrap_err();

    assert!(
        matches!(error, BackupError::UnexpectedArchiveMember { .. }),
        "{error:?}"
    );
    assert!(host.extractions().is_empty(), "nothing may be extracted");
}

/// An absolute member name is refused the same way.
#[test]
fn an_absolute_member_name_is_refused_before_anything_is_extracted() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    host.holds_members(&["manifest.json", "/etc/shadow", "databases/"]);

    let error = run(&fixture, &host).unwrap_err();

    assert!(
        matches!(error, BackupError::UnexpectedArchiveMember { .. }),
        "{error:?}"
    );
    assert!(host.extractions().is_empty(), "nothing may be extracted");
}

/// A member that has no place in the layout at all is refused, and a hostile
/// name sitting AFTER well-formed ones is still found, because the whole
/// listing is judged.
#[test]
fn a_stray_member_after_well_formed_ones_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let mut names = members(1);
    names.push("etc/cron.d/pwn".to_owned());
    let borrowed: Vec<&str> = names.iter().map(String::as_str).collect();
    host.holds_members(&borrowed);

    let error = run(&fixture, &host).unwrap_err();

    assert!(
        matches!(error, BackupError::UnexpectedArchiveMember { .. }),
        "{error:?}"
    );
}

/// A refused member name reaches the error escaped, with a newline neither
/// splitting a log line nor carried whole.
#[test]
fn a_refused_member_name_is_escaped_and_truncated_in_the_error() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let hostile = format!("evil\nINJECTED{}", "x".repeat(500));
    host.holds_members(&["manifest.json", &hostile]);

    let error = run(&fixture, &host).unwrap_err();

    let printed = error.to_string();
    assert!(!printed.contains('\n'), "{printed:?}");
    assert!(printed.len() < 300, "{printed:?}");
}

/// A manifest member extracted as a symbolic link is refused, and the file it
/// points at is never read: it holds a manifest that would be accepted, so a
/// check that was absent would answer ok.
#[test]
fn a_manifest_that_extracts_as_a_symbolic_link_is_refused() {
    let fixture = Fixture::new(1);
    let (_elsewhere, target) = acceptable_manifest_elsewhere(1);
    let host = PlantingHost {
        inner: host_with(1),
        plant: Plant::SymbolicLink(target),
    };

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::MemberNotRegularFile);
}

/// A manifest member extracted as a hard link to a file elsewhere is refused
/// for the same reason, and for the same blind spot in a scan of names.
#[test]
fn a_manifest_that_extracts_as_a_hard_link_is_refused() {
    let fixture = Fixture::new(1);
    let (_elsewhere, target) = acceptable_manifest_elsewhere(1);
    let host = PlantingHost {
        inner: host_with(1),
        plant: Plant::HardLink(target),
    };

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::MemberNotRegularFile);
}

/// The inverse control of the two above: the same planting host, planting
/// nothing the check refuses, is accepted. Without it the two refusals could be
/// satisfied by a verification that refused every manifest.
#[test]
fn the_same_manifest_as_a_plain_file_is_accepted() {
    let fixture = Fixture::new(1);
    let host = host_with(1);

    let verified = run(&fixture, &host).unwrap();

    assert_eq!(verified.database_count, 1);
}

/// A manifest larger than any this agent writes is refused before it is
/// parsed.
#[test]
fn an_oversized_manifest_is_refused() {
    let fixture = Fixture::new(1);
    let host = PlantingHost {
        inner: host_with(1),
        plant: Plant::Oversize,
    };

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ManifestTooLarge);
}

/// A listing past the deadline is a bound, a refusal that says nothing about
/// the artifact, and never a verification of the part read so far.
#[test]
fn a_listing_that_runs_past_its_deadline_is_a_bound_and_never_an_ok() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    host.time_out_listing();

    let error = run(&fixture, &host).unwrap_err();

    assert!(
        matches!(error, BackupError::Bound(BoundExceeded::TimedOut { .. })),
        "{error:?}"
    );
    assert!(host.extractions().is_empty(), "nothing may be extracted");
}

/// A manifest naming another account is refused.
#[test]
fn a_manifest_naming_another_account_is_refused() {
    let fixture = Fixture::new(1);
    let mut foreign = manifest(1);
    foreign.account = other_account().as_str().to_owned();
    fixture.publish_sidecar(&foreign);
    let host = host_with(1);
    host.holds_manifest(foreign);

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ManifestAccountMismatch);
}

/// A request that names another account than the manifest's is the same
/// refusal in the other direction: the files are alice's, the caller says bobby.
#[test]
fn a_request_naming_another_account_than_the_artifacts_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);

    let error = verify_in(
        &fixture.placement(),
        &host,
        &other_account(),
        &backup_id(),
        &fixture.directory(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::ManifestAccountMismatch);
}

/// An artifact copied under another id carries a manifest for the first one,
/// and is refused rather than verified as whatever it contains.
#[test]
fn an_artifact_copied_under_another_backup_id_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let copied = fixture
        .directory()
        .join(artifact_file_name(&other_backup_id()));
    std::fs::copy(fixture.artifact(), &copied).unwrap();
    std::fs::copy(
        fixture.sidecar(),
        fixture
            .directory()
            .join(sidecar_file_name(&other_backup_id())),
    )
    .unwrap();

    let error = verify_in(
        &fixture.placement(),
        &host,
        &account(),
        &other_backup_id(),
        &fixture.directory(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::ManifestBackupMismatch);
}

/// A backup id that was never published under this account does not resolve,
/// and nothing is created to find that out.
#[test]
fn a_backup_id_that_is_not_in_the_directory_is_not_found() {
    let fixture = Fixture::new(1);
    let host = host_with(1);

    let error = verify_in(
        &fixture.placement(),
        &host,
        &account(),
        &other_backup_id(),
        &fixture.directory(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::NotFound);
    assert!(host.calls().is_empty());
    assert!(!fixture.scratch().exists());
}

/// A symbolic link wearing the artifact's name is neither verified nor
/// followed, even when what it points at is a perfectly good artifact.
#[test]
fn a_symbolic_link_wearing_the_artifacts_name_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let real = fixture.directory().join("real.tar.gz");
    std::fs::rename(fixture.artifact(), &real).unwrap();
    symlink(&real, fixture.artifact()).unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ArtifactNotRegularFile);
    assert!(host.calls().is_empty());
}

/// An artifact with a second hard link is refused.
#[test]
fn an_artifact_with_a_second_hard_link_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    hard_link(fixture.artifact(), fixture.directory().join("alias")).unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ArtifactNotRegularFile);
}

/// A directory wearing the artifact's name is refused.
#[test]
fn a_directory_wearing_the_artifacts_name_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    remove_file(fixture.artifact()).unwrap();
    create_dir_all(fixture.artifact()).unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ArtifactNotRegularFile);
}

/// A sidecar that is a symbolic link is refused, not followed.
#[test]
fn a_sidecar_that_is_a_symbolic_link_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let real = fixture.directory().join("real.meta.json");
    std::fs::rename(fixture.sidecar(), &real).unwrap();
    symlink(&real, fixture.sidecar()).unwrap();

    let error = run(&fixture, &host).unwrap_err();

    assert_eq!(error, BackupError::ArtifactNotRegularFile);
}

/// A file another uid owns is not an artifact this agent published. The
/// placement says root's uid is somebody else's here, and the control above
/// (the sound artifact) is the same fixture under the right owner.
#[test]
fn an_artifact_owned_by_another_uid_is_refused() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    let mut placement = fixture.placement();
    placement.owner = current_uid().unwrap().wrapping_add(1);

    let error = verify_in(
        &placement,
        &host,
        &account(),
        &backup_id(),
        &fixture.directory(),
    )
    .unwrap_err();

    assert_eq!(error, BackupError::ArtifactNotRegularFile);
}

/// The room for the scratch is asked of the directory the manifest lands in,
/// and below the floor the verification is refused as a host failure and not as
/// a finding about the artifact.
#[test]
fn a_scratch_filesystem_below_the_floor_refuses_the_verification() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    host.has_available(0);

    let error = run(&fixture, &host).unwrap_err();

    assert!(
        matches!(
            error,
            BackupError::FreeSpace(FloorRefusal::BelowFloor { .. })
        ),
        "{error:?}"
    );
    assert_eq!(host.measured(), vec![fixture.scratch()]);
    assert!(host.extractions().is_empty());
}

/// A verification reads and writes nothing it is not entitled to: it extracts
/// the manifest as root and nothing else, starts no dump, drop or load, and
/// leaves the artifact and its sidecar as they were.
#[test]
fn a_verification_extracts_only_the_manifest_and_changes_nothing_it_read() {
    let fixture = Fixture::new(2);
    let host = host_with(2);
    let artifact_before = read(fixture.artifact()).unwrap();
    let sidecar_before = read(fixture.sidecar()).unwrap();

    run(&fixture, &host).unwrap();

    assert_eq!(
        host.extractions(),
        vec![("manifest.json".to_owned(), ExtractIdentity::Root)]
    );
    assert!(host.calls_to(DUMP_PROGRAM).is_empty());
    assert!(host.calls_to(DROP_PROGRAM).is_empty());
    assert!(host.calls_to(LOAD_PROGRAM).is_empty());
    assert_eq!(read(fixture.artifact()).unwrap(), artifact_before);
    assert_eq!(read(fixture.sidecar()).unwrap(), sidecar_before);
}

/// Repeating a verification answers the same and changes nothing.
#[test]
fn verifying_twice_answers_the_same() {
    let fixture = Fixture::new(1);
    let host = host_with(1);

    let first = run(&fixture, &host).unwrap();
    let second = run(&fixture, &host).unwrap();

    assert_eq!(first, second);
}

/// The scratch is gone after a success and after a refusal alike, so a
/// verification leaves nothing under the bulk root.
#[test]
fn the_scratch_is_removed_on_a_success_and_on_a_refusal() {
    let sound = Fixture::new(1);
    run(&sound, &host_with(1)).unwrap();
    assert!(!sound.scratch().exists(), "left after a success");

    let refused = Fixture::new(1);
    let host = host_with(1);
    host.holds_members(&["manifest.json", "databases/"]);
    let error = run(&refused, &host).unwrap_err();
    assert_eq!(error, BackupError::DumpMissing);
    assert!(!refused.scratch().exists(), "left after a refusal");
}

/// A bound is not one of the findings about an artifact: the panel treats the
/// two differently, so they must not compare equal.
#[test]
fn a_bound_is_none_of_the_findings_about_an_artifact() {
    let fixture = Fixture::new(1);
    let host = host_with(1);
    host.time_out_listing();

    let error = run(&fixture, &host).unwrap_err();

    for finding in [
        BackupError::ChecksumMismatch,
        BackupError::ArtifactSizeMismatch,
        BackupError::SidecarUnreadable,
        BackupError::DumpMissing,
        BackupError::ManifestUnreadable,
        BackupError::MemberNotRegularFile,
        BackupError::ManifestAccountMismatch,
        BackupError::ManifestBackupMismatch,
        BackupError::ManifestDisagreesWithSidecar,
    ] {
        assert_ne!(error, finding);
    }
}

/// A verification that arrives while another operation holds the account's
/// lock is refused immediately, and first: the root handed in is the real
/// default one, which on this machine is neither present nor root-owned, so
/// reaching it at all would answer a root error instead.
#[test]
fn verifying_while_the_account_is_locked_reports_already_running() {
    let account = AccountName::parse("verlockd").expect("the fixture name is valid");
    let held = take_account_lock(&account);
    assert!(held.is_some(), "the fixture must own the lock");

    let outcome = verify_backup(
        &host_with(1),
        &account,
        &backup_id(),
        &LocalBackupRoot::default(),
        &FreeSpaceFloor::from_mebibytes(1).unwrap(),
    );

    assert_eq!(outcome, Err(BackupError::AlreadyRunning));
}

/// The lock is released by the verification's own return — the inverse control
/// for the test above, which passes just as well if the guard were never
/// dropped.
#[test]
fn a_finished_verification_leaves_the_account_unlocked() {
    let account = AccountName::parse("verlockr").expect("the fixture name is valid");

    let _ = verify_backup(
        &host_with(1),
        &account,
        &backup_id(),
        &LocalBackupRoot::default(),
        &FreeSpaceFloor::from_mebibytes(1).unwrap(),
    );

    assert!(
        take_account_lock(&account).is_some(),
        "the verification kept the lock"
    );
}
