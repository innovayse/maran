//! Tests for the `validated_open_files_limit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::accounts::ResourceCeiling;

use super::validated_open_files_limit;
use crate::proto::resource_ceiling::Ceiling;
use crate::proto::{
    ErrorCode, ResourceCeiling as WireCeiling, ResourceCeilingUnlimited,
    SetAccountOpenFilesLimitRequest,
};

fn request(open_files: Option<WireCeiling>) -> SetAccountOpenFilesLimitRequest {
    SetAccountOpenFilesLimitRequest {
        username: "alice".to_owned(),
        open_files,
    }
}

fn limited(figure: u32) -> Option<WireCeiling> {
    Some(WireCeiling {
        ceiling: Some(Ceiling::Maximum(figure)),
    })
}

#[test]
fn a_figure_is_carried_through() {
    let limit = match validated_open_files_limit(&request(limited(4096))).unwrap() {
        ResourceCeiling::Limited(limit) => limit,
        ResourceCeiling::Unlimited => panic!("a figure must stay a figure"),
    };

    assert_eq!(limit.value(), 4096);
}

#[test]
fn the_unlimited_case_removes_the_ceiling() {
    let unlimited = Some(WireCeiling {
        ceiling: Some(Ceiling::Unlimited(ResourceCeilingUnlimited {})),
    });

    assert_eq!(
        validated_open_files_limit(&request(unlimited)),
        Ok(ResourceCeiling::Unlimited)
    );
}

#[test]
fn an_absent_ceiling_is_refused_rather_than_read_as_unlimited() {
    let refused = validated_open_files_limit(&request(None)).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.starts_with("open_files:"));
}

#[test]
fn zero_is_refused() {
    let refused = validated_open_files_limit(&request(limited(0))).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_figure_above_the_maximum_is_refused() {
    let refused = validated_open_files_limit(&request(limited(1_048_577))).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
