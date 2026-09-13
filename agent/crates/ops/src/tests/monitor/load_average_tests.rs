//! Tests for `LoadAverage`: reading the kernel's run-queue averages.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::LoadAverage;
use crate::monitor::fake_monitor_host::{ALMA_LOADAVG, UBUNTU_LOADAVG};

#[test]
fn the_captured_ubuntu_loadavg_is_read() {
    let load = LoadAverage::parse(UBUNTU_LOADAVG).expect("the capture holds three averages");

    assert_eq!(load.one_minute, 0.45);
    assert_eq!(load.five_minutes, 0.62);
    assert_eq!(load.fifteen_minutes, 0.93);
}

#[test]
fn the_captured_alma_loadavg_is_read() {
    let load = LoadAverage::parse(ALMA_LOADAVG).expect("the capture holds three averages");

    assert_eq!(load.one_minute, 0.45);
    assert_eq!(load.five_minutes, 0.62);
    assert_eq!(load.fifteen_minutes, 0.93);
}

#[test]
fn the_running_process_count_is_not_a_load_average() {
    // The fourth field is `<running>/<total>` and the fifth is the last pid;
    // neither is a measurement of load, and `2/1659` is not a number at all.
    let load = LoadAverage::parse("1.00 2.00 3.00 2/1659 6\n").expect("three averages lead");

    assert_eq!(load.fifteen_minutes, 3.00);
}

#[test]
fn a_line_with_fewer_than_three_numbers_is_not_understood() {
    // Reporting zeroes would be indistinguishable from an idle host, which is
    // the one reading nobody would look into.
    assert!(LoadAverage::parse("1.00 2.00\n").is_none());
}

#[test]
fn an_average_that_is_not_a_number_is_not_understood() {
    assert!(LoadAverage::parse("1.00 busy 3.00 2/1659 6\n").is_none());
}

#[test]
fn an_empty_line_is_not_understood() {
    assert!(LoadAverage::parse("").is_none());
}

#[test]
fn a_load_that_is_not_a_finite_number_is_not_understood() {
    // Rust's float parser accepts every one of these spellings, so without an
    // explicit check the panel would be handed a NaN — which survives each
    // comparison it makes and reaches the chart as a hole. The same hazard the
    // processor parser refuses to create.
    assert!(LoadAverage::parse("nan inf -5 1/1 1\n").is_none());
    assert!(LoadAverage::parse("NaN 2.00 3.00 1/1 1\n").is_none());
    assert!(LoadAverage::parse("1.00 infinity 3.00 1/1 1\n").is_none());
    assert!(LoadAverage::parse("1.00 2.00 1e400 1/1 1\n").is_none());
}

#[test]
fn a_negative_load_is_not_understood() {
    // A run queue cannot be shorter than empty.
    assert!(LoadAverage::parse("1.00 -0.50 3.00 1/1 1\n").is_none());
}

#[test]
fn a_zero_load_is_a_perfectly_good_reading() {
    // The check above refuses NEGATIVE, not falsy: an idle host reports zeroes
    // and the panel must be able to draw them.
    let load = LoadAverage::parse("0.00 0.00 0.00 1/1 1\n").expect("an idle host is readable");

    assert_eq!(load.one_minute, 0.0);
}
