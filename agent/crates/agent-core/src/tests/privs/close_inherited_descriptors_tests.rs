//! The sweep of inherited descriptors: it closes everything above stdio, and spares exactly one.
//!
//! Observed from INSIDE a forked child, because the sweep's effect is the child's own descriptor
//! table, which nothing in the parent can read. Every test that expects a descriptor to survive is
//! paired with one that expects it to be gone, so a sweep that did nothing, or one that spared
//! everything, cannot pass both.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::os::fd::AsRawFd;
use std::time::Duration;

use super::{close_inherited_descriptors, close_one_by_one};
use crate::privs::fork_and_run::fork_and_run;
use crate::privs::priv_error::PrivError;
use crate::privs::wait_for_child::wait_until;

/// Highest descriptor number a child counts. Far above anything the test binary has open, and
/// far below the sweep ceiling, so a descriptor the sweep missed would be inside the range.
const COUNT_CEILING: libc::c_int = 4096;

/// How long a test waits for a child that does a few syscalls.
const PATIENCE: Duration = Duration::from_secs(20);

/// Whether `descriptor` is open in this process.
fn is_open(descriptor: libc::c_int) -> bool {
    // SAFETY: `fcntl` with `F_GETFD` takes an integer and touches no memory; a descriptor that is
    // not open answers EBADF, which is exactly the answer being asked for.
    unsafe { libc::fcntl(descriptor, libc::F_GETFD) != -1 }
}

/// How many descriptors above stdio this process has open.
fn open_above_stdio() -> usize {
    (3..COUNT_CEILING)
        .filter(|&descriptor| is_open(descriptor))
        .count()
}

/// Three open files, so there are descriptors for a sweep to close and several to choose a spare
/// from. Returned so they stay open in the parent for the whole test.
fn three_open_files() -> [File; 3] {
    [
        File::open("/dev/null").unwrap(),
        File::open("/dev/null").unwrap(),
        File::open("/dev/null").unwrap(),
    ]
}

/// What a child that has been swept should find: only `survivor` open above stdio, or nothing.
fn only_this_survives(survivor: Option<libc::c_int>) -> Result<(), PrivError> {
    let expected = usize::from(survivor.is_some());
    let survivor_open = survivor.is_none_or(is_open);
    if open_above_stdio() == expected && survivor_open {
        Ok(())
    } else {
        Err(PrivError::WorkFailed)
    }
}

/// Forks through the shared entry (which sweeps), runs `check` in the child, returns its verdict.
fn swept_child_says(
    spare: Option<libc::c_int>,
    check: fn(Option<libc::c_int>) -> Result<(), PrivError>,
) -> Result<(), PrivError> {
    let child = fork_and_run(|| Ok(()), spare, || check(spare)).unwrap();
    wait_until(child, PATIENCE)
}

/// Forks WITHOUT the shared entry, so the child's table is the full inherited one, and runs
/// `body` in it. The route to the fallback loop, which the shared entry would only reach on a
/// kernel without `close_range`.
fn unswept_child_says(body: impl FnOnce() -> Result<(), PrivError>) -> Result<(), PrivError> {
    // SAFETY: `fork` takes no arguments. The child runs `body`, which only closes descriptors and
    // reads their state, and always ends in `_exit`, never returning into the test harness.
    let child = unsafe { libc::fork() };
    assert!(child >= 0, "fork failed");
    if child == 0 {
        let status = if body().is_ok() { 0 } else { 1 };
        // SAFETY: `_exit` never returns and runs no destructors.
        unsafe { libc::_exit(status) };
    }

    let mut status: libc::c_int = 0;
    // SAFETY: `status` is a live local `waitpid` writes; `child` is the pid just forked.
    let reaped = unsafe { libc::waitpid(child, &raw mut status, 0) };
    assert_eq!(reaped, child);
    if libc::WIFEXITED(status) && libc::WEXITSTATUS(status) == 0 {
        Ok(())
    } else {
        Err(PrivError::WorkFailed)
    }
}

#[test]
fn a_sweep_with_a_spare_leaves_exactly_that_descriptor_open() {
    let files = three_open_files();
    let spare = files[1].as_raw_fd();

    assert_eq!(swept_child_says(Some(spare), only_this_survives), Ok(()));
}

#[test]
fn a_sweep_with_nothing_to_spare_leaves_nothing_open() {
    // The control for the test above: the same open files, no spare, and the child finds none.
    // Without it a sweep that spared EVERY descriptor passes the test above only when the
    // spare happened to be the sole descriptor open, and this one fails it.
    let _files = three_open_files();

    assert_eq!(swept_child_says(None, only_this_survives), Ok(()));
}

#[test]
fn the_lowest_and_the_highest_descriptor_can_each_be_the_spare() {
    // Either end of the range is where an off-by-one in the two `close_range` calls would leave
    // the spare closed, or a neighbour open.
    let files = three_open_files();
    let numbers = [
        files[0].as_raw_fd(),
        files[1].as_raw_fd(),
        files[2].as_raw_fd(),
    ];
    let lowest = *numbers.iter().min().unwrap();
    let highest = *numbers.iter().max().unwrap();

    assert_eq!(swept_child_says(Some(lowest), only_this_survives), Ok(()));
    assert_eq!(swept_child_says(Some(highest), only_this_survives), Ok(()));
}

#[test]
fn a_sweep_in_the_parent_process_would_have_found_the_files_open() {
    // The inverse control for the whole file: the observation can see an open descriptor. If
    // `open_above_stdio` always answered zero, every test above would pass over a sweep that
    // closed nothing.
    let _files = three_open_files();

    assert!(open_above_stdio() >= 3);
}

#[test]
fn the_one_by_one_fallback_spares_exactly_the_descriptor_it_is_given() {
    let files = three_open_files();
    let spare = files[1].as_raw_fd();

    let verdict = unswept_child_says(|| {
        close_one_by_one(COUNT_CEILING.unsigned_abs(), Some(spare));
        only_this_survives(Some(spare))
    });

    assert_eq!(verdict, Ok(()));
}

#[test]
fn the_one_by_one_fallback_with_nothing_to_spare_closes_everything() {
    let _files = three_open_files();

    let verdict = unswept_child_says(|| {
        close_one_by_one(COUNT_CEILING.unsigned_abs(), None);
        only_this_survives(None)
    });

    assert_eq!(verdict, Ok(()));
}

#[test]
fn the_sweep_function_itself_spares_the_descriptor_in_a_forked_child() {
    // `close_inherited_descriptors` called directly, not through the shared entry, so the
    // two `close_range` calls and their arithmetic are what is under test.
    let files = three_open_files();
    let spare = files[2].as_raw_fd();

    let verdict = unswept_child_says(|| {
        close_inherited_descriptors(Some(spare));
        only_this_survives(Some(spare))
    });

    assert_eq!(verdict, Ok(()));
}

#[test]
fn stdio_is_never_closed_by_the_sweep() {
    let _files = three_open_files();

    let verdict = unswept_child_says(|| {
        close_inherited_descriptors(None);
        if is_open(0) && is_open(1) && is_open(2) {
            Ok(())
        } else {
            Err(PrivError::WorkFailed)
        }
    });

    assert_eq!(verdict, Ok(()));
}
