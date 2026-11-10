//! How the exit status of `rsync` is read.

use super::RsyncSliceOutcome;

#[test]
fn zero_and_vanished_files_finish_the_copy() {
    assert_eq!(RsyncSliceOutcome::of_status(0), RsyncSliceOutcome::Finished);
    assert_eq!(
        RsyncSliceOutcome::of_status(24),
        RsyncSliceOutcome::Finished
    );
}

#[test]
fn every_other_status_is_a_failure_that_keeps_its_number() {
    for status in [1, 2, 11, 12, 23, 30, 35, 255] {
        assert_eq!(
            RsyncSliceOutcome::of_status(status),
            RsyncSliceOutcome::Failed { status }
        );
    }
}
