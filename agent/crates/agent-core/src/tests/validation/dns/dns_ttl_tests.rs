//! Tests for the `dns_ttl` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::dns_ttl::DnsTtl;
use super::super::dns_ttl_error::DnsTtlError;

#[test]
fn the_bounds_are_accepted() {
    assert_eq!(DnsTtl::parse(60).unwrap().seconds(), 60);
    assert_eq!(DnsTtl::parse(604_800).unwrap().seconds(), 604_800);
}

#[test]
fn zero_and_one_below_the_floor_are_refused() {
    assert_eq!(DnsTtl::parse(0), Err(DnsTtlError::TooSmall { value: 0 }));
    assert_eq!(DnsTtl::parse(59), Err(DnsTtlError::TooSmall { value: 59 }));
}

#[test]
fn one_above_the_ceiling_is_refused() {
    assert_eq!(
        DnsTtl::parse(604_801),
        Err(DnsTtlError::TooLarge { value: 604_801 })
    );
}
