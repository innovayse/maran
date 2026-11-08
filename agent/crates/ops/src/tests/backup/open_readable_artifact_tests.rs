//! The path a reader opens: the stored file when it is plain, a decrypted copy when it is sealed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{read, write};

use tempfile::TempDir;

use crate::backup::archive::encrypt_artifact::encrypt_artifact;

use super::*;

/// A key a test can name.
fn key(byte: &str) -> ArchiveKey {
    ArchiveKey::parse(&byte.repeat(32)).expect("the fixture key is well-formed")
}

/// A stored artifact (plain, or sealed under key `11`) and an empty scratch.
struct World {
    /// Keeps both directories alive.
    root: TempDir,
}

impl World {
    /// A world whose artifact holds `contents`, sealed when `sealed`.
    fn new(contents: &[u8], sealed: bool) -> Self {
        let root = TempDir::new().unwrap();
        std::fs::create_dir(root.path().join("scratch")).unwrap();
        if sealed {
            write(root.path().join("plain"), contents).unwrap();
            encrypt_artifact(
                &root.path().join("plain"),
                &root.path().join("stored"),
                &key("11"),
            )
            .unwrap();
        } else {
            write(root.path().join("stored"), contents).unwrap();
        }

        Self { root }
    }

    /// The stored artifact.
    fn stored(&self) -> PathBuf {
        self.root.path().join("stored")
    }

    /// The scratch directory.
    fn scratch(&self) -> PathBuf {
        self.root.path().join("scratch")
    }
}

#[test]
fn a_plain_artifact_is_read_where_it_lies_with_or_without_a_key() {
    let world = World::new(&[0x1f, 0x8b, 0x08, 0, 0, 0, 0, 0, 0, 3], false);

    let without = open_readable_artifact(&world.stored(), &world.scratch(), None).unwrap();
    let with = open_readable_artifact(&world.stored(), &world.scratch(), Some(&key("22"))).unwrap();

    assert_eq!(without, world.stored());
    assert_eq!(with, world.stored());
    assert_eq!(
        std::fs::read_dir(world.scratch()).unwrap().count(),
        0,
        "a plain artifact must cost the scratch nothing"
    );
}

#[test]
fn a_sealed_artifact_is_read_through_a_decrypted_copy_in_the_scratch() {
    let world = World::new(b"the plain archive", true);

    let readable =
        open_readable_artifact(&world.stored(), &world.scratch(), Some(&key("11"))).unwrap();

    assert!(readable.starts_with(world.scratch()), "{readable:?}");
    assert_eq!(read(&readable).unwrap(), b"the plain archive");
    assert!(
        read(world.stored()).unwrap().starts_with(b"MARANENC"),
        "the stored file must be untouched"
    );
}

#[test]
fn a_sealed_artifact_and_no_key_is_refused_before_anything_is_written() {
    let world = World::new(b"the plain archive", true);

    let refusal = open_readable_artifact(&world.stored(), &world.scratch(), None);

    assert_eq!(refusal, Err(BackupError::EncryptionKeyRequired));
    assert_eq!(std::fs::read_dir(world.scratch()).unwrap().count(), 0);
}

#[test]
fn a_sealed_artifact_and_the_wrong_key_is_refused() {
    let world = World::new(b"the plain archive", true);

    let refusal = open_readable_artifact(&world.stored(), &world.scratch(), Some(&key("22")));

    assert_eq!(refusal, Err(BackupError::ArtifactDecryptionFailed));
}

#[test]
fn a_scratch_without_room_for_the_copy_is_refused_before_it_is_written() {
    let world = World::new(b"the plain archive", true);
    // A scratch directory that does not exist cannot be measured.
    let missing = world.root.path().join("no-such-scratch");

    let refusal = open_readable_artifact(&world.stored(), &missing, Some(&key("11")));

    assert_eq!(refusal, Err(BackupError::ScratchUnmeasurable));
}
