//! Tests for `MemoryUsage`: reading the kernel's memory accounting.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MemoryUsage;
use crate::monitor::fake_monitor_host::{ALMA_MEMINFO, UBUNTU_MEMINFO};

/// One kibibyte, the unit the file's `kB` label means.
const KIB: u64 = 1024;

#[test]
fn the_captured_ubuntu_meminfo_is_read() {
    let memory = MemoryUsage::parse(UBUNTU_MEMINFO).expect("the capture holds both fields");

    assert_eq!(memory.total_bytes, 16_049_416 * KIB);
    assert_eq!(memory.used_bytes, (16_049_416 - 10_460_836) * KIB);
}

#[test]
fn the_captured_alma_meminfo_is_read() {
    let memory = MemoryUsage::parse(ALMA_MEMINFO).expect("the capture holds both fields");

    assert_eq!(memory.total_bytes, 16_049_416 * KIB);
    assert_eq!(memory.used_bytes, (16_049_416 - 10_445_224) * KIB);
}

#[test]
fn memory_used_is_total_minus_available_not_total_minus_free() {
    // Linux spends every byte it is not otherwise using on page cache, so
    // MemFree on a healthy long-running server is small and a panel built on it
    // reports every host as nearly out of memory. MemAvailable is the kernel's
    // own estimate of what a new process could get.
    let memory = MemoryUsage::parse(UBUNTU_MEMINFO).expect("the capture holds both fields");

    assert_eq!(memory.total_bytes, 16_049_416 * KIB);
    assert_eq!(memory.used_bytes, 5_722_705_920);
    assert_ne!(
        memory.used_bytes,
        (16_049_416 - 2_175_776) * KIB,
        "used was measured against MemFree"
    );
}

#[test]
fn the_fields_are_found_by_name_and_not_by_position() {
    // Neither their order in the file nor which fields sit between them is part
    // of any interface.
    let memory = MemoryUsage::parse(
        "MemAvailable:    400 kB\nSwapTotal:      1 kB\nMemTotal:       1000 kB\n",
    )
    .expect("both fields are present");

    assert_eq!(memory.total_bytes, 1000 * KIB);
    assert_eq!(memory.used_bytes, 600 * KIB);
}

#[test]
fn a_name_that_merely_starts_the_same_is_not_the_field() {
    // `MemTotal:` and `MemAvailable:` are matched whole, so a kernel that grows
    // a `MemTotalHigh:` does not silently answer for one of them.
    assert!(MemoryUsage::parse("MemTotalHigh:   1 kB\nMemAvailable:   1 kB\n").is_none());
}

#[test]
fn meminfo_without_the_fields_is_not_understood() {
    // A host with no memory is not a fact this agent should ever assert.
    assert!(MemoryUsage::parse("Buffers:  993964 kB\n").is_none());
}

#[test]
fn a_field_that_is_not_a_number_is_not_understood() {
    assert!(MemoryUsage::parse("MemTotal:   lots kB\nMemAvailable:  1 kB\n").is_none());
}

#[test]
fn an_available_figure_larger_than_the_total_does_not_wrap_around() {
    let memory = MemoryUsage::parse("MemTotal:  10 kB\nMemAvailable:  20 kB\n")
        .expect("both fields are present");

    assert_eq!(memory.used_bytes, 0);
}
