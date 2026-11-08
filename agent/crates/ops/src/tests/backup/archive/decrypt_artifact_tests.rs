//! A sealed archive opens under its key and under nothing else.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{read, write};
use std::path::PathBuf;

use tempfile::TempDir;

use crate::backup::archive::encrypt_artifact::encrypt_artifact;

use super::*;

/// A key a test can name.
fn key(byte: &str) -> ArchiveKey {
    ArchiveKey::parse(&byte.repeat(32)).expect("the fixture key is well-formed")
}

/// `length` bytes that are not all alike.
fn bytes_of(length: usize) -> Vec<u8> {
    (0..length).map(|index| (index % 251) as u8).collect()
}

/// A directory holding a plain file of `length` bytes sealed under key `11`.
struct Sealed {
    /// Keeps the directory alive.
    directory: TempDir,
    /// The plain bytes that were sealed.
    plain: Vec<u8>,
}

impl Sealed {
    /// Seals `length` bytes.
    fn new(length: usize) -> Self {
        let directory = TempDir::new().unwrap();
        let plain = bytes_of(length);
        write(directory.path().join("plain"), &plain).unwrap();
        encrypt_artifact(
            &directory.path().join("plain"),
            &directory.path().join("sealed"),
            &key("11"),
        )
        .unwrap();

        Self { directory, plain }
    }

    /// The sealed file.
    fn sealed(&self) -> PathBuf {
        self.directory.path().join("sealed")
    }

    /// Where an opened copy is written.
    fn opened(&self) -> PathBuf {
        self.directory.path().join("opened")
    }

    /// Opens under `key`.
    fn open(&self, key: &ArchiveKey) -> Result<u64, BackupError> {
        let _ = std::fs::remove_file(self.opened());
        decrypt_artifact(&self.sealed(), &self.opened(), key)
    }

    /// Replaces the sealed file's bytes with `bytes`.
    fn replace(&self, bytes: &[u8]) {
        write(self.sealed(), bytes).unwrap();
    }
}

#[test]
fn a_small_archive_opens_to_the_bytes_that_were_sealed() {
    let sealed = Sealed::new(1000);

    let written = sealed.open(&key("11")).unwrap();

    assert_eq!(read(sealed.opened()).unwrap(), sealed.plain);
    assert_eq!(written as usize, sealed.plain.len());
}

#[test]
fn an_empty_archive_opens_to_nothing() {
    let sealed = Sealed::new(0);

    assert_eq!(sealed.open(&key("11")).unwrap(), 0);
    assert!(read(sealed.opened()).unwrap().is_empty());
}

#[test]
fn an_archive_spanning_several_chunks_opens_to_the_bytes_that_were_sealed() {
    let sealed = Sealed::new(CHUNK_BYTES * 2 + 12_345);

    sealed.open(&key("11")).unwrap();

    assert!(read(sealed.opened()).unwrap() == sealed.plain);
}

#[test]
fn an_archive_of_exactly_one_chunk_and_one_of_exactly_two_open() {
    for length in [CHUNK_BYTES, CHUNK_BYTES * 2] {
        let sealed = Sealed::new(length);

        sealed.open(&key("11")).unwrap();

        assert!(read(sealed.opened()).unwrap() == sealed.plain, "{length}");
    }
}

#[test]
fn the_wrong_key_does_not_open_it() {
    let sealed = Sealed::new(1000);

    assert_eq!(
        sealed.open(&key("22")),
        Err(BackupError::ArtifactDecryptionFailed)
    );
}

#[test]
fn a_flipped_bit_anywhere_in_the_header_is_refused() {
    let sealed = Sealed::new(1000);
    let original = read(sealed.sealed()).unwrap();

    for position in 0..HEADER_BYTES {
        let mut damaged = original.clone();
        damaged[position] ^= 0x01;
        sealed.replace(&damaged);

        assert_eq!(
            sealed.open(&key("11")),
            Err(BackupError::ArtifactDecryptionFailed),
            "header byte {position}"
        );
    }
}

#[test]
fn a_flipped_bit_in_the_ciphertext_or_the_tag_is_refused() {
    let sealed = Sealed::new(1000);
    let original = read(sealed.sealed()).unwrap();

    for position in [HEADER_BYTES + 4, HEADER_BYTES + 500, original.len() - 1] {
        let mut damaged = original.clone();
        damaged[position] ^= 0x80;
        sealed.replace(&damaged);

        assert_eq!(
            sealed.open(&key("11")),
            Err(BackupError::ArtifactDecryptionFailed),
            "byte {position}"
        );
    }
}

#[test]
fn an_archive_cut_short_inside_a_frame_is_refused() {
    let sealed = Sealed::new(1000);
    let original = read(sealed.sealed()).unwrap();
    sealed.replace(&original[..original.len() - 5]);

    assert_eq!(
        sealed.open(&key("11")),
        Err(BackupError::ArtifactDecryptionFailed)
    );
}

#[test]
fn an_archive_cut_at_a_chunk_boundary_is_refused_not_read_as_shorter() {
    // The truncation the last-chunk flag exists to catch: two whole frames
    // remain, each authentic, and only the flag says the first of them was not
    // the end.
    let sealed = Sealed::new(CHUNK_BYTES * 2 + 100);
    let original = read(sealed.sealed()).unwrap();
    let frame = 4 + CHUNK_BYTES + TAG_BYTES;
    sealed.replace(&original[..HEADER_BYTES + 2 * frame]);

    assert_eq!(
        sealed.open(&key("11")),
        Err(BackupError::ArtifactDecryptionFailed)
    );
}

#[test]
fn an_archive_with_a_frame_appended_is_refused() {
    let sealed = Sealed::new(1000);
    let mut extended = read(sealed.sealed()).unwrap();
    let frame = extended[HEADER_BYTES..].to_vec();
    extended.extend_from_slice(&frame);
    sealed.replace(&extended);

    assert_eq!(
        sealed.open(&key("11")),
        Err(BackupError::ArtifactDecryptionFailed)
    );
}

#[test]
fn two_frames_swapped_are_refused() {
    let sealed = Sealed::new(CHUNK_BYTES * 2);
    let original = read(sealed.sealed()).unwrap();
    let frame = 4 + CHUNK_BYTES + TAG_BYTES;
    let mut swapped = original[..HEADER_BYTES].to_vec();
    swapped.extend_from_slice(&original[HEADER_BYTES + frame..HEADER_BYTES + 2 * frame]);
    swapped.extend_from_slice(&original[HEADER_BYTES..HEADER_BYTES + frame]);
    sealed.replace(&swapped);

    assert_eq!(
        sealed.open(&key("11")),
        Err(BackupError::ArtifactDecryptionFailed)
    );
}

#[test]
fn a_frame_that_claims_to_be_larger_than_a_chunk_is_refused_before_it_is_read() {
    let sealed = Sealed::new(1000);
    let mut damaged = read(sealed.sealed()).unwrap();
    damaged[HEADER_BYTES..HEADER_BYTES + 4].copy_from_slice(&u32::MAX.to_be_bytes());
    sealed.replace(&damaged);

    assert_eq!(
        sealed.open(&key("11")),
        Err(BackupError::ArtifactDecryptionFailed)
    );
}

#[test]
fn a_frame_shorter_than_a_tag_is_refused() {
    let sealed = Sealed::new(1000);
    let mut damaged = read(sealed.sealed()).unwrap();
    damaged[HEADER_BYTES..HEADER_BYTES + 4].copy_from_slice(&3_u32.to_be_bytes());
    sealed.replace(&damaged);

    assert_eq!(
        sealed.open(&key("11")),
        Err(BackupError::ArtifactDecryptionFailed)
    );
}

#[test]
fn a_file_that_ends_inside_the_header_or_straight_after_it_is_refused() {
    let sealed = Sealed::new(1000);
    let original = read(sealed.sealed()).unwrap();

    for length in [0, 8, HEADER_BYTES - 1, HEADER_BYTES, HEADER_BYTES + 2] {
        sealed.replace(&original[..length]);

        assert_eq!(
            sealed.open(&key("11")),
            Err(BackupError::ArtifactDecryptionFailed),
            "{length}"
        );
    }
}

#[test]
fn a_plain_file_handed_to_the_decryptor_is_refused() {
    let directory = TempDir::new().unwrap();
    let plain = directory.path().join("plain.tar.gz");
    write(
        &plain,
        [
            0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 1, 2, 3,
        ],
    )
    .unwrap();

    let refusal = decrypt_artifact(&plain, &directory.path().join("out"), &key("11"));

    assert_eq!(refusal, Err(BackupError::ArtifactDecryptionFailed));
}

#[test]
fn an_existing_target_is_refused_as_an_unusable_scratch_and_left_alone() {
    let sealed = Sealed::new(10);
    write(sealed.opened(), b"already here").unwrap();

    let refusal = decrypt_artifact(&sealed.sealed(), &sealed.opened(), &key("11"));

    assert_eq!(refusal, Err(BackupError::ScratchUnusable));
    assert_eq!(read(sealed.opened()).unwrap(), b"already here");
}

#[test]
fn nothing_is_written_for_a_header_that_does_not_parse() {
    let sealed = Sealed::new(10);
    let mut damaged = read(sealed.sealed()).unwrap();
    damaged[0] ^= 0xff;
    sealed.replace(&damaged);

    let _ = sealed.open(&key("11"));

    assert!(
        !sealed.opened().exists(),
        "a refused header must not create the target"
    );
}
