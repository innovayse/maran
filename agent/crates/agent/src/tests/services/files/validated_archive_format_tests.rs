//! Tests for `validated_archive_format`: only the two formats the contract offers get through.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::files::model::archive_format::ArchiveFormat;

use super::validated_archive_format;
use crate::proto::{ArchiveFormat as WireArchiveFormat, ErrorCode};

#[test]
fn zip_and_tar_gz_are_read_as_themselves() {
    assert_eq!(
        validated_archive_format(WireArchiveFormat::Zip as i32).unwrap(),
        ArchiveFormat::Zip
    );
    assert_eq!(
        validated_archive_format(WireArchiveFormat::TarGz as i32).unwrap(),
        ArchiveFormat::TarGz
    );
}

#[test]
fn the_unspecified_format_is_refused_rather_than_read_as_a_default() {
    let refused = validated_archive_format(WireArchiveFormat::Unspecified as i32).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_number_the_contract_does_not_have_is_refused() {
    for number in [3, 99, -1, i32::MAX] {
        let refused = validated_archive_format(number).unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32, "{number}");
    }
}
