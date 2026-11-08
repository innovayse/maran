//! Tests for the `validated_inode_limit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::accounts::ResourceCeiling;

use super::validated_inode_limit;
use crate::proto::resource_ceiling::Ceiling;
use crate::proto::{ErrorCode, ResourceCeiling as WireCeiling, ResourceCeilingUnlimited};

fn limited(figure: u32) -> WireCeiling {
    WireCeiling {
        ceiling: Some(Ceiling::Maximum(figure)),
    }
}

#[test]
fn an_absent_ceiling_is_unlimited_for_a_caller_that_predates_the_field() {
    assert_eq!(validated_inode_limit(None), Ok(ResourceCeiling::Unlimited));
}

#[test]
fn the_unlimited_case_is_unlimited() {
    let wire = WireCeiling {
        ceiling: Some(Ceiling::Unlimited(ResourceCeilingUnlimited {})),
    };

    assert_eq!(
        validated_inode_limit(Some(&wire)),
        Ok(ResourceCeiling::Unlimited)
    );
}

#[test]
fn a_figure_is_carried_through() {
    let limit = match validated_inode_limit(Some(&limited(250_000))).unwrap() {
        ResourceCeiling::Limited(limit) => limit,
        ResourceCeiling::Unlimited => panic!("a figure must stay a figure"),
    };

    assert_eq!(limit.value(), 250_000);
}

#[test]
fn zero_is_refused_rather_than_read_as_unlimited() {
    let refused = validated_inode_limit(Some(&limited(0))).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.starts_with("inode_limit:"));
}

#[test]
fn a_present_ceiling_that_sets_neither_case_is_refused() {
    // The inverse control of the absent case: only an absent MESSAGE is the
    // compatible reading; one that is there and states nothing is a malformed one.
    let empty = WireCeiling { ceiling: None };

    let refused = validated_inode_limit(Some(&empty)).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.starts_with("inode_limit:"));
}
