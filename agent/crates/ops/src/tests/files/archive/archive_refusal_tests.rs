//! Tests for the verdict the child of an archive operation writes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::ArchiveRefusal;
use crate::files::FilesOpError;
use crate::files::model::limit_kind::LimitKind;

#[test]
fn every_refusal_survives_a_round_trip_through_its_code() {
    for code in 0..=u8::MAX {
        if let Some(refusal) = ArchiveRefusal::from_code(code) {
            assert_eq!(refusal.code(), code);
        }
    }
    // The table is the whole enum: 24 refusals, codes 1 to 24.
    let known = (0..=u8::MAX)
        .filter(|code| ArchiveRefusal::from_code(*code).is_some())
        .count();
    assert_eq!(known, 24);
}

#[test]
fn a_byte_no_refusal_has_reads_as_nothing() {
    assert_eq!(ArchiveRefusal::from_code(0), None);
    assert_eq!(ArchiveRefusal::from_code(25), None);
    assert_eq!(ArchiveRefusal::from_code(255), None);
}

#[test]
fn every_way_an_archive_can_be_unsafe_reaches_the_wire_as_the_one_unsafe_error() {
    for refusal in [
        ArchiveRefusal::UnsafeName,
        ArchiveRefusal::LinkMember,
        ArchiveRefusal::SpecialMember,
        ArchiveRefusal::SparseMember,
        ArchiveRefusal::EncryptedMember,
        ArchiveRefusal::UnsupportedCompression,
        ArchiveRefusal::DuplicateMember,
        ArchiveRefusal::FileAsDirectory,
        ArchiveRefusal::SizeMismatch,
        ArchiveRefusal::BombRatio,
        ArchiveRefusal::TrailingData,
    ] {
        assert_eq!(
            refusal.into_op_error(),
            FilesOpError::ArchiveUnsafe,
            "{refusal:?}"
        );
    }
}

#[test]
fn the_other_refusals_map_each_to_its_own_error() {
    assert_eq!(
        ArchiveRefusal::HomeUnusable.into_op_error(),
        FilesOpError::HomeUnusable
    );
    assert_eq!(
        ArchiveRefusal::DirectoryUnusable.into_op_error(),
        FilesOpError::DirectoryUnusable
    );
    assert_eq!(
        ArchiveRefusal::NotFound.into_op_error(),
        FilesOpError::NotFound
    );
    assert_eq!(
        ArchiveRefusal::NotARegularFile.into_op_error(),
        FilesOpError::NotARegularFile
    );
    assert_eq!(
        ArchiveRefusal::Unreadable.into_op_error(),
        FilesOpError::ArchiveUnreadable
    );
    assert_eq!(
        ArchiveRefusal::AlreadyExists.into_op_error(),
        FilesOpError::AlreadyExists
    );
    assert_eq!(
        ArchiveRefusal::EntryLimit.into_op_error(),
        FilesOpError::LimitReached(LimitKind::Entries)
    );
    assert_eq!(
        ArchiveRefusal::ByteLimit.into_op_error(),
        FilesOpError::LimitReached(LimitKind::Bytes)
    );
    assert_eq!(
        ArchiveRefusal::Quota.into_op_error(),
        FilesOpError::QuotaExceeded
    );
    assert_eq!(
        ArchiveRefusal::WriteFailed.into_op_error(),
        FilesOpError::WriteFailed
    );
    assert_eq!(
        ArchiveRefusal::ReadFailed.into_op_error(),
        FilesOpError::ReadFailed
    );
    assert_eq!(
        ArchiveRefusal::UnsupportedEntry.into_op_error(),
        FilesOpError::UnsupportedEntry
    );
    assert_eq!(
        ArchiveRefusal::TooDeep.into_op_error(),
        FilesOpError::TooDeep
    );
}

#[test]
fn a_walk_error_is_read_as_a_home_problem_only_when_it_is_one() {
    assert_eq!(
        ArchiveRefusal::from_walk_error(&FilesOpError::HomeUnusable),
        ArchiveRefusal::HomeUnusable
    );
    assert_eq!(
        ArchiveRefusal::from_walk_error(&FilesOpError::DirectoryUnusable),
        ArchiveRefusal::DirectoryUnusable
    );
    assert_eq!(
        ArchiveRefusal::from_walk_error(&FilesOpError::NotFound),
        ArchiveRefusal::DirectoryUnusable
    );
}
