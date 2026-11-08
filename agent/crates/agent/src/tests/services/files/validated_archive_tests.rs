//! Tests for `validated_archive`: an archive creation always runs under stated, bounded caps.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::archive_format::ArchiveFormat;

use super::validated_archive;
use crate::proto::{ArchiveFormat as WireArchiveFormat, CreateArchiveRequest, ErrorCode};

/// A valid request, for the tests that change one thing.
fn request() -> CreateArchiveRequest {
    CreateArchiveRequest {
        account_username: "alice_01".to_owned(),
        source_paths: vec!["sites/a".to_owned(), "notes.txt".to_owned()],
        destination_path: "backup.tar.gz".to_owned(),
        format: WireArchiveFormat::TarGz as i32,
        max_entries: 100_000,
        max_source_bytes: 2 * 1024 * 1024 * 1024,
    }
}

fn refused_code(request: &CreateArchiveRequest) -> i32 {
    validated_archive(request).unwrap_err().code
}

#[test]
fn a_valid_creation_is_accepted() {
    let input = validated_archive(&request()).unwrap();

    assert_eq!(input.account().as_str(), "alice_01");
    assert_eq!(input.sources().len(), 2);
    assert_eq!(input.destination().file_name(), "backup.tar.gz");
    assert_eq!(input.format(), ArchiveFormat::TarGz);
    assert_eq!(input.bounds().max_entries(), 100_000);
}

#[test]
fn a_creation_with_no_source_is_invalid_input() {
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            source_paths: Vec::new(),
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn the_home_as_a_source_or_a_destination_is_invalid_input() {
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            source_paths: vec![String::new()],
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            destination_path: String::new(),
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn archiving_a_folder_into_itself_is_invalid_input() {
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            source_paths: vec!["sites/a".to_owned()],
            destination_path: "sites/a/out.zip".to_owned(),
            format: WireArchiveFormat::Zip as i32,
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn overlapping_sources_are_invalid_input() {
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            source_paths: vec!["sites".to_owned(), "sites/a".to_owned()],
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_cap_the_request_does_not_state_or_one_above_the_ceiling_is_invalid_input() {
    for (max_entries, max_source_bytes) in [(0, 1024), (10, 0), (10_000_000, 1024), (10, u64::MAX)]
    {
        assert_eq!(
            refused_code(&CreateArchiveRequest {
                max_entries,
                max_source_bytes,
                ..request()
            }),
            ErrorCode::InvalidInput as i32,
            "{max_entries} {max_source_bytes}"
        );
    }
}

#[test]
fn an_unspecified_format_is_invalid_input() {
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            format: WireArchiveFormat::Unspecified as i32,
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_source_path_that_traverses_is_invalid_input() {
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            source_paths: vec!["../etc".to_owned()],
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_source_or_destination_inside_the_trash_is_invalid_input() {
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            source_paths: vec![".maran-trash/entries".to_owned()],
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
    assert_eq!(
        refused_code(&CreateArchiveRequest {
            destination_path: ".maran-trash/out.tar.gz".to_owned(),
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}
