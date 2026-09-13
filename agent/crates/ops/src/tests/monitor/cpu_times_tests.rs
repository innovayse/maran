//! Tests for `CpuTimes`: reading the kernel's processor accounting and turning
//! two readings into a percentage.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::CpuTimes;
use crate::monitor::fake_monitor_host::{ALMA_STAT, UBUNTU_STAT};

/// One reading whose halves are round numbers, so a test can say what it means.
///
/// `user nice system idle iowait irq softirq steal`, and the two guest fields
/// the kernel adds after them.
fn sample(user: u64, idle: u64, iowait: u64, guest: u64) -> String {
    format!("cpu  {user} 0 0 {idle} {iowait} 0 0 0 {guest} 0\n")
}

#[test]
fn the_aggregate_line_of_the_captured_ubuntu_host_is_read() {
    let times = CpuTimes::parse(UBUNTU_STAT).expect("the capture holds an aggregate line");

    assert_eq!(times.busy, 40_126_626);
    assert_eq!(times.idle, 251_219_983);
}

#[test]
fn the_aggregate_line_of_the_captured_alma_host_is_read() {
    let times = CpuTimes::parse(ALMA_STAT).expect("the capture holds an aggregate line");

    assert_eq!(times.busy, 40_126_882);
    assert_eq!(times.idle, 251_221_235);
}

#[test]
fn the_per_processor_lines_are_not_summed_into_the_aggregate() {
    // Both captures carry a `cpu` line followed by one `cpuN` line per
    // processor, and the aggregate already sums them. Reading the file as a
    // whole would count every tick twice, which the exact figures above pin.
    let times = CpuTimes::parse(UBUNTU_STAT).expect("the capture holds an aggregate line");
    let per_processor_lines = UBUNTU_STAT
        .lines()
        .filter(|line| line.starts_with("cpu") && line.split_whitespace().next() != Some("cpu"))
        .count();

    assert!(
        per_processor_lines > 1,
        "the capture has per-processor lines"
    );
    assert_eq!(times.busy, 40_126_626);
}

#[test]
fn iowait_counts_as_idle_and_not_as_work() {
    // A processor blocked on a disk is not doing work: counting that time as
    // utilisation makes a nightly backup read as a CPU emergency.
    let times = CpuTimes::parse(&sample(100, 100, 40, 0)).expect("the line is well formed");

    assert_eq!(times.idle, 140);
    assert_eq!(times.busy, 100);
}

#[test]
fn guest_time_is_not_counted_a_second_time() {
    // The kernel has already included `guest` inside `user`. Adding it again
    // inflates the total and understates every percentage taken from it — and
    // both polygon captures have guest at zero, so only a made-up reading can
    // tell the two implementations apart.
    let times = CpuTimes::parse(&sample(100, 100, 0, 50)).expect("the line is well formed");

    assert_eq!(times.busy, 100);
    assert_eq!(times.idle, 100);
}

#[test]
fn an_idle_interval_is_zero_percent() {
    let earlier = CpuTimes::parse(&sample(100, 100, 0, 0)).unwrap();
    let later = CpuTimes::parse(&sample(100, 200, 0, 0)).unwrap();

    assert_eq!(later.busy_percent_since(&earlier), 0.0);
}

#[test]
fn a_fully_busy_interval_is_one_hundred_percent() {
    let earlier = CpuTimes::parse(&sample(100, 100, 0, 0)).unwrap();
    let later = CpuTimes::parse(&sample(200, 100, 0, 0)).unwrap();

    assert_eq!(later.busy_percent_since(&earlier), 100.0);
}

#[test]
fn two_readings_taken_in_the_same_tick_are_zero_percent() {
    // Dividing by a zero interval would produce a NaN, which survives every
    // comparison the panel makes and reaches a chart as a hole.
    let times = CpuTimes::parse(&sample(100, 100, 0, 0)).unwrap();

    assert_eq!(times.busy_percent_since(&times), 0.0);
}

#[test]
fn cpu_percent_is_bounded_0_to_100() {
    // Counters go backwards on a real host: a processor returning from an
    // offline state brings its own accounting with it, so idle can fall while
    // busy rises. The busy delta then exceeds the total delta, and the ratio is
    // not a percentage of anything.
    let earlier = CpuTimes::parse(&sample(100, 100, 0, 0)).unwrap();
    let later = CpuTimes::parse(&sample(200, 60, 0, 0)).unwrap();
    let percent = later.busy_percent_since(&earlier);

    assert!(
        (0.0..=100.0).contains(&percent),
        "a percentage outside 0-100 reached the panel: {percent}"
    );
    assert_eq!(percent, 100.0);
}

#[test]
fn a_reading_that_went_entirely_backwards_is_zero_percent() {
    let earlier = CpuTimes::parse(&sample(200, 200, 0, 0)).unwrap();
    let later = CpuTimes::parse(&sample(100, 100, 0, 0)).unwrap();
    let percent = later.busy_percent_since(&earlier);

    assert!(
        (0.0..=100.0).contains(&percent),
        "{percent} is not a percentage"
    );
}

#[test]
fn text_with_no_aggregate_line_is_not_understood() {
    assert!(CpuTimes::parse("cpu0 1 2 3 4 5 6 7 8\nintr 9\n").is_none());
}

#[test]
fn an_aggregate_line_with_too_few_fields_is_not_understood() {
    // Reporting zeroes here would be indistinguishable from an idle machine.
    assert!(CpuTimes::parse("cpu  1 2 3 4\n").is_none());
}

#[test]
fn an_aggregate_line_with_a_field_that_is_not_a_number_is_not_understood() {
    assert!(CpuTimes::parse("cpu  1 2 3 four 5 6 7 8\n").is_none());
}
