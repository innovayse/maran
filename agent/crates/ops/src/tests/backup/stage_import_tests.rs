//! Receiving an import's bytes: exactly the declared artifact is kept, and anything
//! else leaves no file behind.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{read, write};
use std::os::unix::fs::MetadataExt as _;

use tempfile::TempDir;

use crate::backup::test_package::{ChunkSource, generated_key, manifest_for};

use super::*;

const ID: &str = "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b";

fn declared(artifact: &[u8]) -> TransferManifest {
    manifest_for(&generated_key(), "alice", ID, artifact, &[])
}

#[test]
fn the_declared_artifact_arrives_whole_in_a_private_file() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("x.partial");
    let artifact = b"gzip-bytes".to_vec();
    let mut source = ChunkSource::clean(vec![artifact[..4].to_vec(), artifact[4..].to_vec()]);

    stage_import(&partial, &mut source, &declared(&artifact)).unwrap();

    assert_eq!(read(&partial).unwrap(), artifact);
    assert_eq!(partial.metadata().unwrap().mode() & 0o7777, ARTIFACT_MODE);
}

#[test]
fn a_stream_with_more_bytes_than_declared_is_refused_and_leaves_nothing() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("x.partial");
    let artifact = b"gzip-bytes".to_vec();
    let mut source = ChunkSource::clean(vec![artifact.clone(), b"x".to_vec()]);

    assert_eq!(
        stage_import(&partial, &mut source, &declared(&artifact)),
        Err(BackupError::ImportLengthMismatch)
    );
    assert!(!partial.exists());
}

#[test]
fn a_stream_with_fewer_bytes_than_declared_is_refused_and_leaves_nothing() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("x.partial");
    let artifact = b"gzip-bytes".to_vec();
    let mut source = ChunkSource::clean(vec![artifact[..9].to_vec()]);

    assert_eq!(
        stage_import(&partial, &mut source, &declared(&artifact)),
        Err(BackupError::ImportLengthMismatch)
    );
    assert!(!partial.exists());
}

#[test]
fn bytes_of_the_right_length_that_hash_differently_are_refused() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("x.partial");
    let mut source = ChunkSource::clean(vec![b"gzip-byteZ".to_vec()]);

    assert_eq!(
        stage_import(&partial, &mut source, &declared(b"gzip-bytes")),
        Err(BackupError::ImportDigestMismatch)
    );
    assert!(!partial.exists());
}

#[test]
fn a_stream_that_breaks_is_an_interruption_and_leaves_nothing() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("x.partial");
    let artifact = b"gzip-bytes".to_vec();
    let mut source = ChunkSource::interrupted(vec![artifact[..5].to_vec()]);

    assert_eq!(
        stage_import(&partial, &mut source, &declared(&artifact)),
        Err(BackupError::ImportInterrupted)
    );
    assert!(!partial.exists());
}

#[test]
fn a_file_already_wearing_the_stage_name_is_replaced() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("x.partial");
    write(&partial, b"litter of a run that died").unwrap();
    let artifact = b"gzip-bytes".to_vec();
    let mut source = ChunkSource::clean(vec![artifact.clone()]);

    stage_import(&partial, &mut source, &declared(&artifact)).unwrap();

    assert_eq!(read(&partial).unwrap(), artifact);
}
