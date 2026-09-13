//! Tests for the one arithmetic decision `ProcessMonitorHost` makes: which of a
//! filesystem's three block counts the panel reports.
//!
//! The rest of this host is deliberately untested — it reads the real `/proc`,
//! waits a quarter of a second and spawns the real service manager, none of
//! which a unit test can pin without asserting on the build machine. This one
//! piece can be, and had to be: both single-token spellings of the choice
//! (`f_bavail` against `f_bfree`, `f_frsize` against `f_bsize`) survived the
//! entire suite while nothing here existed, and the wrong one reports a full
//! disk as having five percent left.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use rustix::fs::{StatVfs, StatVfsMountFlags};

use super::usage_of;

/// The fragment size every filesystem this panel will meet reports.
const FRAGMENT: u64 = 4096;

/// A filesystem query's answer, with the counts a test cares about.
///
/// The other fields are inodes and mount flags, which this arithmetic never
/// reads. `f_bsize` is deliberately given a DIFFERENT value from `f_frsize`, so
/// that reading the preferred block size instead of the fragment size changes
/// every figure below — on a real ext4 the two agree, which is what let that
/// mistake hide.
fn statistics(blocks: u64, free: u64, available: u64) -> StatVfs {
    StatVfs {
        f_bsize: FRAGMENT * 2,
        f_frsize: FRAGMENT,
        f_blocks: blocks,
        f_bfree: free,
        f_bavail: available,
        f_files: 0,
        f_ffree: 0,
        f_favail: 0,
        f_fsid: 0,
        f_flag: StatVfsMountFlags::empty(),
        f_namemax: 255,
    }
}

#[test]
fn used_space_is_measured_against_what_an_account_can_write() {
    // The numbers are a real 467 GiB ext4 root: a 6,241,651-block reserve that
    // only root may write into, 5.09% of the filesystem.
    let usage = usage_of(&statistics(122_512_118, 99_553_529, 93_311_878));

    assert_eq!(usage.total_bytes, 122_512_118 * FRAGMENT);
    assert_eq!(usage.used_bytes, (122_512_118 - 93_311_878) * FRAGMENT);
    assert_ne!(
        usage.used_bytes,
        (122_512_118 - 99_553_529) * FRAGMENT,
        "used was measured against the free blocks, so the reserve only root \
         may write is being reported to customers as room they have"
    );
}

#[test]
fn a_filesystem_an_account_cannot_write_to_reads_as_full() {
    // The moment that matters: `f_bavail` is zero, every customer write fails
    // with ENOSPC, and the reserve is still 5% of the disk. A free-based
    // reading would report 95% used and call it healthy.
    let usage = usage_of(&statistics(1_000_000, 50_000, 0));

    assert_eq!(usage.used_bytes, usage.total_bytes);
}

#[test]
fn an_empty_filesystem_reads_as_empty() {
    let usage = usage_of(&statistics(1_000_000, 1_000_000, 1_000_000));

    assert_eq!(usage.used_bytes, 0);
    assert_eq!(usage.total_bytes, 1_000_000 * FRAGMENT);
}

#[test]
fn the_block_counts_are_measured_in_fragments_not_in_the_preferred_block_size() {
    // POSIX defines all three counts in units of `f_frsize`, and `df`
    // multiplies by the same field. The two agree on every filesystem this
    // panel will meet, which is precisely why the wrong one hides.
    let usage = usage_of(&statistics(100, 40, 25));

    assert_eq!(usage.total_bytes, 100 * FRAGMENT);
    assert_eq!(usage.used_bytes, 75 * FRAGMENT);
}

#[test]
fn more_available_blocks_than_the_filesystem_holds_does_not_wrap_around() {
    let usage = usage_of(&statistics(100, 200, 200));

    assert_eq!(usage.used_bytes, 0);
}
