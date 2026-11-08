//! Tests for the `client_body_size` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{ClientBodySize, ClientBodySizeError, MAXIMUM_MEBIBYTES};

#[test]
fn the_ends_of_the_range_are_accepted() {
    assert_eq!(ClientBodySize::parse(1).unwrap().mebibytes(), 1);
    assert_eq!(
        ClientBodySize::parse(MAXIMUM_MEBIBYTES)
            .unwrap()
            .mebibytes(),
        MAXIMUM_MEBIBYTES
    );
}

#[test]
fn zero_is_refused_because_it_is_what_an_absent_field_decodes_to() {
    assert_eq!(ClientBodySize::parse(0), Err(ClientBodySizeError::Zero));
}

#[test]
fn a_ceiling_above_the_maximum_is_refused() {
    assert_eq!(
        ClientBodySize::parse(MAXIMUM_MEBIBYTES + 1),
        Err(ClientBodySizeError::TooLarge {
            value: MAXIMUM_MEBIBYTES + 1,
            maximum: MAXIMUM_MEBIBYTES,
        })
    );
}
