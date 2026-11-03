//! What a stopped, an already-gone and a refused unit mean for a run.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::reading_of;
use crate::cron::cron_error::CronError;

#[test]
fn a_unit_that_was_stopped_is_a_run_that_is_no_longer_going() {
    assert_eq!(reading_of(0), Ok(()));
}

#[test]
fn a_unit_that_was_already_gone_is_a_run_that_is_no_longer_going() {
    // `--collect` removes a transient unit the moment its job ends, so a job
    // that finished between the ceiling and the stop is not loaded any more.
    assert_eq!(reading_of(5), Ok(()));
}

#[test]
fn any_other_status_is_a_run_that_could_not_be_confirmed_stopped() {
    // The inverse control of the two above: a reading that accepted every
    // status would let a refused stop be reported as a stopped run.
    for status in [1, 2, 3, 4, 6, 126, 127, 255, -1] {
        assert_eq!(
            reading_of(status),
            Err(CronError::RunNotStopped),
            "status {status}"
        );
    }
}
