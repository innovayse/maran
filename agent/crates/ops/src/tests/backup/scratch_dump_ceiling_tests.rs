//! The ceiling one dump may reach: the narrowing itself, against stated
//! numbers, and the wiring to the scratch filesystem, against the real one.
//!
//! The split is deliberate and it is the fix for a flake. The exact figure —
//! nine tenths — used to be asserted against a SECOND reading of the same live
//! filesystem, so the expectation and the answer were two measurements of a
//! host that other tests in this binary were writing to; it failed 7 times in
//! 60 whole-suite runs. The arithmetic is now asserted where the number is
//! stated, and only the properties that survive a moving disk are asserted
//! against the disk.

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
    // measuring. The room is stated rather than read off the machine, so the
    // fraction is what the assertion is about and a byte written elsewhere on
    // the host cannot move it.
    let ceiling = narrow_to_room(200 * 1024 * 1024, u64::MAX).unwrap();

    assert_eq!(ceiling, 188_743_680);
    assert_eq!(ceiling, 200 * 1024 * 1024 / 10 * 9);
}

#[test]
fn the_tenth_that_is_left_is_left_whatever_the_room_is() {
    // The fraction, again, on numbers that do not divide evenly — a narrowing
    // that rounded the other way would spend more of the filesystem than the
    // reserve this ceiling exists to keep.
    assert_eq!(narrow_to_room(19, u64::MAX).unwrap(), 9);
    assert_eq!(narrow_to_room(10, u64::MAX).unwrap(), 9);
    assert_eq!(
        narrow_to_room(1_000_000_007, u64::MAX).unwrap(),
        900_000_000
    );
}

#[test]
fn a_filesystem_whose_spendable_share_rounds_to_nothing_is_refused() {
    // The condition a ceiling of zero would otherwise become: a dump allowed
    // no bytes at all, refused halfway through by `ENOSPC` instead of here.
    let refusal = narrow_to_room(9, u64::MAX);

    assert!(matches!(
        refusal,
        Err(BackupError::ScratchTooSmall {
            available: 9,
            required: 10
        })
    ));
    assert!(matches!(
        narrow_to_room(0, 1),
        Err(BackupError::ScratchTooSmall { available: 0, .. })
    ));
}

#[test]
fn the_ceiling_is_narrowed_to_the_room_this_filesystem_really_has() {
    // The half the seam above cannot see: that the number narrowed is a live
    // reading of the directory the dump lands in, and not the cap, a constant,
    // or a measurement nobody used.
    //
    // Deliberately NOT `ceiling == room / 10 * 9`. Free space moves between
    // this reading and the function's own — that comparison is what made this
    // test flake — and an assertion that has to be re-run to be believed is
    // not an assertion. What no jitter reaches is the ORDER of magnitude: nine
    // tenths of any reading of this filesystem sits below every other reading
    // of it and far above half of one, while `u64::MAX`, a constant ceiling,
    // or an ignored measurement would all be outside that band by orders of
    // magnitude rather than by kilobytes.
    let scratch = TempDir::new().unwrap();
    let room = available_bytes(scratch.path()).unwrap();

    let ceiling = scratch_dump_ceiling(scratch.path(), u64::MAX).unwrap();

    assert!(
        ceiling < room,
        "a dump may never be allowed the whole filesystem: {ceiling} of {room}"
    );
    assert!(
        ceiling > room / 2,
        "the ceiling did not come from this filesystem's own free space: {ceiling} of {room}"
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
