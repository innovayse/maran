//! Tests for the write itself: what lands on disk, and what is refused.
//!
//! Like the walk's tests, none of these needs root — the uid is an argument, and
//! a test owns its temporary directory exactly as a customer owns their home.
//! Each attack a hosting account can mount against a file the agent is about to
//! write has a test here: a symlink at the destination name, a pre-created file
//! at that name, a directory at that name, a temporary name already taken, a
//! link at a parent level, and an upload that stops before it is complete.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::{MetadataExt, PermissionsExt, symlink};
use std::path::{Path, PathBuf};

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::upload_limit::UploadLimit;
use maran_agent_core::validation::system::name::AccountName;

use super::write_in_home;
use crate::files::FilesOpError;
use crate::files::model::buffered_upload::BufferedUpload;
use crate::files::model::limit_kind::LimitKind;
use crate::files::model::upload_aborted::UploadAborted;
use crate::files::model::upload_source::UploadSource;
use crate::files::model::write_file_input::WriteFileInput;

/// The path every test here writes to.
const TARGET: &str = "sites/example.com/uploads/report.txt";

/// The temporary name the write renames from.
const TEMPORARY: &str = ".maran-write-test";

/// Chunks handed out in order, then complete or broken.
struct Scripted {
    /// The chunks, in order.
    chunks: Vec<Vec<u8>>,
    /// How many have been handed out.
    next: usize,
    /// Whether the source breaks after the last chunk instead of completing.
    breaks: bool,
}

impl Scripted {
    /// A source of `chunks` that completes.
    fn complete(chunks: &[&[u8]]) -> Self {
        Self {
            chunks: chunks.iter().map(|chunk| chunk.to_vec()).collect(),
            next: 0,
            breaks: false,
        }
    }

    /// A source of `chunks` that breaks before completing.
    fn broken(chunks: &[&[u8]]) -> Self {
        Self {
            breaks: true,
            ..Self::complete(chunks)
        }
    }
}

impl UploadSource for Scripted {
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, UploadAborted> {
        if self.next < self.chunks.len() {
            self.next += 1;
            return Ok(Some(&self.chunks[self.next - 1]));
        }
        if self.breaks {
            Err(UploadAborted)
        } else {
            Ok(None)
        }
    }
}

/// The input for `TARGET` with the given mode, refusal to replace and cap.
fn input(mode: u32, fail_if_exists: bool, cap: u64) -> WriteFileInput {
    WriteFileInput {
        account: AccountName::parse("acme").unwrap(),
        path: MutablePath::parse(TARGET).unwrap(),
        mode: FileMode::parse(mode).unwrap(),
        fail_if_exists,
        limit: UploadLimit::parse(cap).unwrap(),
    }
}

/// Writes `source` at `TARGET`, replacing, 0644, under the default cap.
fn write(home: &Path, source: &mut dyn UploadSource) -> Result<u64, FilesOpError> {
    write_in_home(
        home,
        &input(0o644, false, 0),
        TEMPORARY,
        current_uid().unwrap(),
        source,
    )
}

/// Writes `bytes` at `TARGET`, refusing to replace.
fn write_new(home: &Path, bytes: &[u8]) -> Result<u64, FilesOpError> {
    write_in_home(
        home,
        &input(0o644, true, 0),
        TEMPORARY,
        current_uid().unwrap(),
        &mut BufferedUpload::new(bytes.to_vec()),
    )
}

/// Where the file ends up.
fn target(home: &Path) -> PathBuf {
    home.join(TARGET)
}

/// Where the temporary file would be if one were left behind.
fn temporary(home: &Path) -> PathBuf {
    home.join("sites/example.com/uploads").join(TEMPORARY)
}

/// A home whose upload directory already exists.
fn prepared() -> tempfile::TempDir {
    let home = tempfile::tempdir().unwrap();
    std::fs::create_dir_all(home.path().join("sites/example.com/uploads")).unwrap();
    home
}

#[test]
fn the_chunks_land_byte_for_byte_in_order_and_the_count_is_reported() {
    let home = prepared();

    let written = write(
        home.path(),
        &mut Scripted::complete(&[b"one-", b"two-", b"three"]),
    )
    .unwrap();

    assert_eq!(written, 13);
    assert_eq!(
        std::fs::read(target(home.path())).unwrap(),
        b"one-two-three"
    );
}

#[test]
fn an_empty_upload_creates_an_empty_file() {
    let home = prepared();

    assert_eq!(write(home.path(), &mut Scripted::complete(&[])).unwrap(), 0);

    assert!(std::fs::read(target(home.path())).unwrap().is_empty());
}

#[test]
fn the_missing_parent_directories_are_created_by_the_write_and_belong_to_the_account() {
    let home = tempfile::tempdir().unwrap();

    write(home.path(), &mut Scripted::complete(&[b"x"])).unwrap();

    let directory = home.path().join("sites/example.com/uploads");
    assert!(directory.is_dir());
    assert_eq!(
        std::fs::metadata(directory).unwrap().uid(),
        current_uid().unwrap()
    );
}

#[test]
fn the_file_carries_exactly_the_mode_the_caller_asked_for_whatever_the_umask_is() {
    let home = prepared();

    for mode in [0o644, 0o600, 0o755] {
        write_in_home(
            home.path(),
            &input(mode, false, 0),
            TEMPORARY,
            current_uid().unwrap(),
            &mut BufferedUpload::new(b"x".to_vec()),
        )
        .unwrap();

        assert_eq!(
            std::fs::metadata(target(home.path()))
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            mode
        );
    }
}

#[test]
fn no_temporary_file_survives_a_successful_write() {
    let home = prepared();

    write(home.path(), &mut Scripted::complete(&[b"x"])).unwrap();

    assert!(!temporary(home.path()).exists());
}

#[test]
fn writing_twice_replaces_the_content_and_leaves_one_file() {
    let home = prepared();

    write(home.path(), &mut Scripted::complete(&[b"first"])).unwrap();
    write(home.path(), &mut Scripted::complete(&[b"second"])).unwrap();

    assert_eq!(std::fs::read(target(home.path())).unwrap(), b"second");
    assert_eq!(
        std::fs::read_dir(home.path().join("sites/example.com/uploads"))
            .unwrap()
            .count(),
        1
    );
}

#[test]
fn a_symlink_planted_at_the_destination_is_replaced_and_never_written_through() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let victim = elsewhere.path().join("victim");
    std::fs::write(&victim, b"original").unwrap();
    symlink(&victim, target(home.path())).unwrap();

    write(home.path(), &mut Scripted::complete(&[b"new"])).unwrap();

    assert_eq!(std::fs::read(&victim).unwrap(), b"original");
    let replaced = std::fs::symlink_metadata(target(home.path())).unwrap();
    assert!(
        replaced.file_type().is_file(),
        "the link itself was replaced"
    );
}

#[test]
fn a_directory_at_the_destination_is_refused_rather_than_emptied() {
    let home = prepared();
    std::fs::create_dir(target(home.path())).unwrap();

    let refused = write(home.path(), &mut Scripted::complete(&[b"x"])).unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
    assert!(target(home.path()).is_dir());
    assert!(!temporary(home.path()).exists());
}

#[test]
fn refusing_to_replace_leaves_an_existing_file_untouched_and_reports_it() {
    let home = prepared();
    std::fs::write(target(home.path()), b"mine").unwrap();

    let refused = write_new(home.path(), b"theirs").unwrap_err();

    assert_eq!(refused, FilesOpError::AlreadyExists);
    assert_eq!(std::fs::read(target(home.path())).unwrap(), b"mine");
    assert!(!temporary(home.path()).exists());
}

#[test]
fn refusing_to_replace_writes_a_name_that_is_free() {
    let home = prepared();

    // The inverse control: a refusal that fired on everything would pass the test above.
    assert_eq!(write_new(home.path(), b"fresh").unwrap(), 5);

    assert_eq!(std::fs::read(target(home.path())).unwrap(), b"fresh");
}

#[test]
fn refusing_to_replace_also_refuses_a_symlink_at_the_name_and_follows_nothing() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let victim = elsewhere.path().join("victim");
    std::fs::write(&victim, b"original").unwrap();
    symlink(&victim, target(home.path())).unwrap();

    let refused = write_new(home.path(), b"new").unwrap_err();

    assert_eq!(refused, FilesOpError::AlreadyExists);
    assert_eq!(std::fs::read(&victim).unwrap(), b"original");
    assert!(
        std::fs::symlink_metadata(target(home.path()))
            .unwrap()
            .file_type()
            .is_symlink()
    );
}

#[test]
fn a_dangling_symlink_at_the_destination_creates_nothing_where_it_pointed() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let pointed_at = elsewhere.path().join("not-yet");
    symlink(&pointed_at, target(home.path())).unwrap();

    write(home.path(), &mut Scripted::complete(&[b"x"])).unwrap();

    assert!(!pointed_at.exists());
}

#[test]
fn a_temporary_name_a_customer_occupied_first_is_refused_rather_than_written_through() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let victim = elsewhere.path().join("victim");
    std::fs::write(&victim, b"original").unwrap();
    symlink(&victim, temporary(home.path())).unwrap();

    let refused = write(home.path(), &mut Scripted::complete(&[b"x"])).unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
    assert_eq!(
        std::fs::read(&victim).unwrap(),
        b"original",
        "O_EXCL is what keeps the bytes out of a descriptor the customer chose"
    );
}

#[test]
fn a_symlink_at_a_parent_level_is_refused_and_nothing_is_written_through_it() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    std::fs::create_dir(home.path().join("sites")).unwrap();
    symlink(elsewhere.path(), home.path().join("sites/example.com")).unwrap();

    let refused = write(home.path(), &mut Scripted::complete(&[b"x"])).unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
    assert_eq!(std::fs::read_dir(elsewhere.path()).unwrap().count(), 0);
}

#[test]
fn a_body_of_exactly_the_cap_is_written() {
    let home = prepared();

    let written = write_in_home(
        home.path(),
        &input(0o644, false, 8),
        TEMPORARY,
        current_uid().unwrap(),
        &mut Scripted::complete(&[b"1234", b"5678"]),
    )
    .unwrap();

    assert_eq!(written, 8);
}

#[test]
fn a_body_one_byte_over_the_cap_is_refused_and_leaves_the_old_file() {
    let home = prepared();
    std::fs::write(target(home.path()), b"old").unwrap();

    let refused = write_in_home(
        home.path(),
        &input(0o644, false, 8),
        TEMPORARY,
        current_uid().unwrap(),
        &mut Scripted::complete(&[b"1234", b"56789"]),
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::LimitReached(LimitKind::Bytes));
    assert_eq!(std::fs::read(target(home.path())).unwrap(), b"old");
    assert!(!temporary(home.path()).exists());
}

#[test]
fn an_upload_that_stops_before_it_is_complete_publishes_nothing_and_leaves_the_old_file() {
    let home = prepared();
    std::fs::write(target(home.path()), b"old").unwrap();

    let refused = write(home.path(), &mut Scripted::broken(&[b"partial"])).unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
    assert_eq!(std::fs::read(target(home.path())).unwrap(), b"old");
    assert!(!temporary(home.path()).exists());
}

#[test]
fn an_aborted_upload_to_a_new_name_leaves_no_file_at_all() {
    let home = prepared();

    write(home.path(), &mut Scripted::broken(&[b"partial"])).unwrap_err();

    assert!(!target(home.path()).exists());
}

#[test]
fn the_written_file_is_owned_by_the_account_the_write_ran_as() {
    let home = prepared();

    write(home.path(), &mut Scripted::complete(&[b"x"])).unwrap();

    assert_eq!(
        std::fs::metadata(target(home.path())).unwrap().uid(),
        current_uid().unwrap(),
        "a root-owned file in a customer's tree is one they cannot remove"
    );
}
