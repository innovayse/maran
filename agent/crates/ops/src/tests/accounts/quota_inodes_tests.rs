//! Tests for the `quota_inodes` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::inode_limit::InodeLimit;

use super::QuotaInodes;
use crate::accounts::ResourceCeiling;

#[test]
fn a_limited_ceiling_is_its_figure_in_digits() {
    let ceiling = ResourceCeiling::Limited(InodeLimit::parse(250_000).unwrap());

    assert_eq!(QuotaInodes::new(ceiling).as_argument(), "250000");
}

#[test]
fn an_unlimited_ceiling_is_zero_because_that_is_how_setquota_spells_it() {
    assert_eq!(
        QuotaInodes::new(ResourceCeiling::Unlimited).as_argument(),
        "0"
    );
}

#[test]
fn the_largest_figure_is_passed_whole() {
    let ceiling = ResourceCeiling::Limited(InodeLimit::parse(u32::MAX).unwrap());

    assert_eq!(QuotaInodes::new(ceiling).as_argument(), "4294967295");
}
