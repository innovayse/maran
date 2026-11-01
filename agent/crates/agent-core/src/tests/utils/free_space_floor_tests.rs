//! Tests for [`FreeSpaceFloor`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::{Error, ErrorKind};

use crate::utils::floor_refusal::FloorRefusal;
use crate::utils::free_space_floor::FreeSpaceFloor;
use crate::utils::free_space_floor_error::FreeSpaceFloorError;

const MIB: u64 = 1024 * 1024;

#[test]
fn a_floor_is_counted_in_mebibytes() {
    assert_eq!(FreeSpaceFloor::from_mebibytes(3).unwrap().bytes(), 3 * MIB);
}

#[test]
fn the_default_floor_is_one_gibibyte() {
    assert_eq!(FreeSpaceFloor::default_floor().bytes(), 1024 * MIB);
    assert_eq!(
        FreeSpaceFloor::default_floor(),
        FreeSpaceFloor::from_mebibytes(FreeSpaceFloor::DEFAULT_MEBIBYTES).unwrap()
    );
}

#[test]
fn a_floor_of_zero_is_refused_rather_than_read_as_switched_off() {
    assert_eq!(
        FreeSpaceFloor::from_mebibytes(0),
        Err(FreeSpaceFloorError::Zero)
    );
}

#[test]
fn a_floor_whose_byte_count_overflows_is_refused() {
    assert_eq!(
        FreeSpaceFloor::from_mebibytes(u64::MAX),
        Err(FreeSpaceFloorError::TooLarge {
            mebibytes: u64::MAX
        })
    );
}

#[test]
fn exactly_the_floor_available_is_admitted() {
    let floor = FreeSpaceFloor::from_mebibytes(2).unwrap();

    assert_eq!(floor.admit(Ok(2 * MIB)), Ok(()));
}

#[test]
fn one_byte_below_the_floor_is_refused_with_both_figures() {
    let floor = FreeSpaceFloor::from_mebibytes(2).unwrap();

    assert_eq!(
        floor.admit(Ok(2 * MIB - 1)),
        Err(FloorRefusal::BelowFloor {
            available: 2 * MIB - 1,
            floor: 2 * MIB
        })
    );
}

#[test]
fn a_filesystem_that_cannot_be_measured_is_refused_and_never_admitted() {
    let floor = FreeSpaceFloor::from_mebibytes(1).unwrap();

    assert_eq!(
        floor.admit(Err(Error::new(ErrorKind::NotFound, "gone"))),
        Err(FloorRefusal::Unmeasurable)
    );
}
