//! The pre-flight refusal: enough room for what is about to be written, asked
//! before anything is written.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use tempfile::TempDir;

use super::*;

#[test]
fn a_filesystem_with_room_for_the_write_permits_it() {
    // The inverse control. A gate mutated to refuse everything passes every
    // test that only ever hands it something it must reject.
    let scratch = TempDir::new().unwrap();

    assert!(require_scratch_room(scratch.path(), 1).is_ok());
}

#[test]
fn a_write_larger_than_the_filesystem_is_refused_before_it_starts() {
    // No filesystem holds `u64::MAX` bytes, so this is the refusal without
    // needing a tiny filesystem to produce it — which a unit test cannot mount.
    // It is also the only test in this area that reaches
    // `BackupError::ScratchTooSmall`; the same variant's other producer, a
    // scratch with literally no room left, is UNOBSERVED HERE for that reason.
    let scratch = TempDir::new().unwrap();

    let refusal = require_scratch_room(scratch.path(), u64::MAX);

    let Err(BackupError::ScratchTooSmall {
        available,
        required,
    }) = refusal
    else {
        panic!("a write of u64::MAX bytes must be refused, answered: {refusal:?}");
    };
    assert_eq!(required, u64::MAX);
    assert!(available < u64::MAX);
}

#[test]
fn a_filesystem_that_cannot_be_measured_is_refused_rather_than_assumed_empty() {
    // Unknown is not plenty. A pre-flight that waved the write through when it
    // could not measure would be no pre-flight at all on exactly the hosts
    // where one is needed.
    let missing = Path::new("/nonexistent-maran-scratch-room-probe");

    let refusal = require_scratch_room(missing, 1);

    assert!(matches!(refusal, Err(BackupError::ScratchUnmeasurable)));
}

#[test]
fn a_request_the_filesystem_cannot_quite_cover_is_refused() {
    // The two assertions above bracket the threshold from a very long way
    // away — one byte, and `u64::MAX` — and a gate that had drifted by any
    // finite factor would satisfy both. Measured by mutation: moving the
    // comparison to `available < required / 2` left the whole suite green,
    // which is a restore permitted with half the room it needs.
    //
    // The figures here are a fraction of the live measurement rather than the
    // exact boundary, deliberately: another process writing into the same
    // filesystem between this test's reading and the function's own would make
    // an exact-boundary assertion flaky, and a flaky test is a P1 bug. Half is
    // permitted and one and a half is refused, which the halving mutant fails
    // on the second.
    let scratch = TempDir::new().unwrap();
    let available = available_bytes(scratch.path()).unwrap();
    assert!(
        available > 1,
        "the fixture filesystem must have room: {available}"
    );

    assert!(
        require_scratch_room(scratch.path(), available / 2).is_ok(),
        "half of what is there must be permitted"
    );
    assert!(
        matches!(
            require_scratch_room(scratch.path(), available + available / 2),
            Err(BackupError::ScratchTooSmall { .. })
        ),
        "half again as much as there is must be refused"
    );
}
