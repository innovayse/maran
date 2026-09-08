//! The ceiling one dump may reach, derived from the scratch filesystem's own
//! live free space.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::utils::available_bytes::available_bytes;
use tempfile::TempDir;

use super::*;

#[test]
fn a_cap_below_the_filesystems_room_is_the_answer() {
    // The inverse control for the refusals below: a function that refused
    // everything would satisfy them all.
    let scratch = TempDir::new().unwrap();

    let ceiling = scratch_dump_ceiling(scratch.path(), 1).unwrap();

    assert_eq!(ceiling, 1);
}

#[test]
fn a_cap_above_the_filesystems_room_is_narrowed_to_nine_tenths_of_what_is_left() {
    // This is the whole point of the change: an 8 GiB constant on a host with
    // 200 MiB left is a ceiling that reports on a filesystem it is not
    // measuring. The expectation is computed from a second reading of the same
    // filesystem rather than restated from the code, so a change to the
    // fraction has to change this line too.
    let scratch = TempDir::new().unwrap();
    let room = available_bytes(scratch.path()).unwrap();

    let ceiling = scratch_dump_ceiling(scratch.path(), u64::MAX).unwrap();

    assert_eq!(ceiling, room / 10 * 9);
    assert!(
        ceiling < u64::MAX,
        "the cap must not have survived the narrowing: {ceiling}"
    );
    assert!(
        ceiling < room,
        "a dump may never be allowed the whole filesystem: {ceiling} of {room}"
    );
}

#[test]
fn a_filesystem_that_cannot_be_measured_is_refused_and_never_given_the_cap() {
    // The direction that matters. Falling back to the constant here would mean
    // the ceiling silently reverts to 8 GiB precisely when the agent has lost
    // sight of the disk.
    let missing = Path::new("/nonexistent-maran-scratch-ceiling-probe");

    let refusal = scratch_dump_ceiling(missing, 8 * 1024 * 1024 * 1024);

    assert!(matches!(refusal, Err(BackupError::ScratchUnmeasurable)));
}
