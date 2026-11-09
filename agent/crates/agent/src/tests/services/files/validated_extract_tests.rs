//! Tests for `validated_extract`: an extraction always runs under stated, bounded caps.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::archive_format::ArchiveFormat;

use super::validated_extract;
use crate::proto::{ArchiveFormat as WireArchiveFormat, ErrorCode, ExtractArchiveRequest};

/// A valid request, for the tests that change one thing.
fn request() -> ExtractArchiveRequest {
    ExtractArchiveRequest {
        account_username: "alice_01".to_owned(),
        archive_path: "uploads/site.zip".to_owned(),
        destination_path: "public".to_owned(),
        format: WireArchiveFormat::Zip as i32,
        max_entries: 100_000,
        max_expanded_bytes: 2 * 1024 * 1024 * 1024,
        max_ratio: 100,
    }
}

fn refused_code(request: &ExtractArchiveRequest) -> i32 {
    validated_extract(request).unwrap_err().code
}

#[test]
fn a_valid_extraction_is_accepted_with_its_caps() {
    let input = validated_extract(&request()).unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.archive.file_name(), "site.zip");
    assert_eq!(input.destination.file_name(), "public");
    assert_eq!(input.format, ArchiveFormat::Zip);
    assert_eq!(input.bounds.max_entries(), 100_000);
    assert_eq!(input.bounds.max_ratio(), 100);
}

#[test]
fn the_home_is_a_valid_destination_so_extract_here_works() {
    let input = validated_extract(&ExtractArchiveRequest {
        destination_path: String::new(),
        ..request()
    })
    .unwrap();

    assert!(input.destination.is_home());
}

#[test]
fn the_home_is_not_an_archive() {
    assert_eq!(
        refused_code(&ExtractArchiveRequest {
            archive_path: String::new(),
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_cap_the_request_does_not_state_is_invalid_input() {
    for (max_entries, max_expanded_bytes, max_ratio) in [(0, 1024, 10), (10, 0, 10), (10, 1024, 0)]
    {
        let code = refused_code(&ExtractArchiveRequest {
            max_entries,
            max_expanded_bytes,
            max_ratio,
            ..request()
        });

        assert_eq!(code, ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn a_cap_above_the_agents_ceiling_is_invalid_input() {
    for (max_entries, max_expanded_bytes, max_ratio) in [
        (10_000_000, 1024, 10),
        (10, u64::MAX, 10),
        (10, 1024, 1_000_000),
    ] {
        let code = refused_code(&ExtractArchiveRequest {
            max_entries,
            max_expanded_bytes,
            max_ratio,
            ..request()
        });

        assert_eq!(code, ErrorCode::InvalidInput as i32);
    }
}

#[test]
fn an_unspecified_or_unknown_format_is_invalid_input() {
    for format in [WireArchiveFormat::Unspecified as i32, 42] {
        assert_eq!(
            refused_code(&ExtractArchiveRequest {
                format,
                ..request()
            }),
            ErrorCode::InvalidInput as i32
        );
    }
}

#[test]
fn a_path_that_traverses_or_is_absolute_is_invalid_input() {
    for path in ["../x.zip", "/etc/x.zip", "a//b.zip", "a/./b.zip"] {
        assert_eq!(
            refused_code(&ExtractArchiveRequest {
                archive_path: path.to_owned(),
                ..request()
            }),
            ErrorCode::InvalidInput as i32,
            "{path}"
        );
        assert_eq!(
            refused_code(&ExtractArchiveRequest {
                destination_path: path.to_owned(),
                ..request()
            }),
            ErrorCode::InvalidInput as i32,
            "{path}"
        );
    }
}

#[test]
fn an_account_name_that_is_not_one_is_invalid_input() {
    assert_eq!(
        refused_code(&ExtractArchiveRequest {
            account_username: "Not Valid".to_owned(),
            ..request()
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_path_inside_the_trash_is_invalid_input() {
    for path in [".maran-trash", ".maran-trash/entries/x/payload"] {
        assert_eq!(
            refused_code(&ExtractArchiveRequest {
                archive_path: path.to_owned(),
                ..request()
            }),
            ErrorCode::InvalidInput as i32,
            "{path}"
        );
        assert_eq!(
            refused_code(&ExtractArchiveRequest {
                destination_path: path.to_owned(),
                ..request()
            }),
            ErrorCode::InvalidInput as i32,
            "{path}"
        );
    }
}
