//! Tests for `get_host_metrics`: one reading of the whole machine.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::get_host_metrics;
use crate::monitor::MonitorError;
use crate::monitor::fake_monitor_host::FakeMonitorHost;

/// One kibibyte, the unit the memory file's `kB` label means.
const KIB: u64 = 1024;

#[test]
fn a_snapshot_of_the_captured_ubuntu_host_reads_every_statistic() {
    let host = FakeMonitorHost::from_ubuntu_captures();

    let metrics = get_host_metrics(&host).expect("every capture is readable");

    assert_eq!(metrics.memory.total_bytes, 16_049_416 * KIB);
    assert_eq!(metrics.memory.used_bytes, (16_049_416 - 10_460_836) * KIB);
    assert_eq!(metrics.network.received_bytes, 90);
    assert_eq!(metrics.network.transmitted_bytes, 42);
    assert_eq!(metrics.load.one_minute, 0.45);
    assert_eq!(metrics.root_filesystem.used_bytes, 512);
    assert_eq!(metrics.root_filesystem.total_bytes, 1024);
}

#[test]
fn a_snapshot_of_the_captured_alma_host_reads_every_statistic() {
    let host = FakeMonitorHost::from_alma_captures();

    let metrics = get_host_metrics(&host).expect("every capture is readable");

    assert_eq!(metrics.memory.used_bytes, (16_049_416 - 10_445_224) * KIB);
    assert_eq!(metrics.load.fifteen_minutes, 0.93);
}

#[test]
fn the_processor_figure_costs_exactly_one_wait_between_two_readings() {
    // A percentage exists only between two readings of a counter, and something
    // has to pass between them. Exactly one wait: a second would double what a
    // dashboard refresh costs for no more accuracy.
    let host = FakeMonitorHost::from_ubuntu_captures();

    get_host_metrics(&host).expect("every capture is readable");

    assert_eq!(host.pauses(), 1);
}

#[test]
fn a_busy_interval_between_the_two_readings_is_reported() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_cpu_samples("cpu  100 0 0 100 0 0 0 0\n", "cpu  200 0 0 100 0 0 0 0\n");

    let metrics = get_host_metrics(&host).expect("both readings are well formed");

    assert_eq!(metrics.cpu_percent, 100.0);
}

#[test]
fn the_processor_figure_stays_within_its_range() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_cpu_samples(
        "cpu  100 0 0 100 0 0 0 0\n",
        // A counter that went backwards, as a processor returning from an
        // offline state produces.
        "cpu  200 0 0 60 0 0 0 0\n",
    );

    let metrics = get_host_metrics(&host).expect("both readings are well formed");

    assert!((0.0..=100.0).contains(&metrics.cpu_percent));
}

#[test]
fn a_statistics_file_that_cannot_be_read_fails_the_call() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unreadable_statistics();

    assert_eq!(
        get_host_metrics(&host),
        Err(MonitorError::HostStatisticsUnavailable)
    );
}

#[test]
fn a_statistic_that_cannot_be_understood_is_not_reported_as_zero() {
    // A zero here is not a missing value, it is a claim — a host with no
    // memory — and the panel would draw it as one.
    let host = FakeMonitorHost::from_ubuntu_captures().with_memory("Buffers:  1 kB\n");

    assert_eq!(
        get_host_metrics(&host),
        Err(MonitorError::HostStatisticsUnavailable)
    );
}

#[test]
fn a_root_filesystem_that_cannot_be_measured_fails_the_call() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unmeasurable_filesystem();

    assert_eq!(
        get_host_metrics(&host),
        Err(MonitorError::FilesystemUnavailable)
    );
}

#[test]
fn reading_the_host_spawns_nothing() {
    // Metrics come from the kernel's own files and one filesystem query. A
    // dashboard refresh that spawned a program per panel would be a cost the
    // panel pays on every open page.
    let host = FakeMonitorHost::from_ubuntu_captures();

    get_host_metrics(&host).expect("every capture is readable");

    assert!(host.commands().is_empty());
}
