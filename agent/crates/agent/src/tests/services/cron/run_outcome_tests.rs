//! Tests for the wire payload a run started by hand travels as.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_ops::cron::{CronRun, CronRunLimit as RunLimit};

use super::run_outcome;
use crate::proto::CronRunLimit;

/// A run with this limit and status.
fn run(limit: Option<RunLimit>, exit_code: Option<i32>) -> CronRun {
    CronRun {
        duration: Duration::from_millis(1234),
        exit_code,
        stdout: "out".to_owned(),
        stderr: "err".to_owned(),
        limit,
    }
}

#[test]
fn a_run_that_ended_on_its_own_carries_its_status_and_no_limit() {
    let payload = run_outcome(run(None, Some(2)));

    assert_eq!(payload.duration_ms, 1234);
    assert_eq!(payload.exit_code, Some(2));
    assert_eq!(payload.stdout, "out");
    assert_eq!(payload.stderr, "err");
    assert_eq!(payload.limit, CronRunLimit::Unspecified as i32);
}

#[test]
fn each_ceiling_travels_as_its_own_value_with_no_status() {
    assert_eq!(
        run_outcome(run(Some(RunLimit::Runtime), None)).limit,
        CronRunLimit::Runtime as i32
    );
    let output = run_outcome(run(Some(RunLimit::Output), None));
    assert_eq!(output.limit, CronRunLimit::Output as i32);
    assert_eq!(output.exit_code, None);
}
