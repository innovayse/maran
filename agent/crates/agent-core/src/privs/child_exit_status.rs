//! The exit statuses a forked child uses to tell the parent how it ended.
//!
//! The child's exit status is the only thing that crosses back from it. Each
//! constant names one ending; `wait_for_child` turns them into a [`super::priv_error::PrivError`].

/// Child exit status meaning the work ran as the account and succeeded.
pub(super) const EXIT_OK: i32 = 0;

/// Child exit status meaning `setgroups` returned an error. No work was done.
///
/// One status PER SYSCALL rather than one for the three of them together, and the reason is a
/// day spent on issue #81: "privilege drop failed in child" names a category, and a category
/// cannot be reproduced. Three hypotheses were tested from outside — the capability set, all
/// nineteen of the unit's sandbox directives, and the multithreaded-fork hazard in glibc's
/// setxid broadcast — and every one of them was wrong, because the message could not say which
/// of the three calls had failed. A check must be able to observe what it reports on
/// (rules/testing.md), and this one could not.
///
/// The errno itself is still not carried, and that is deliberate rather than an omission: the
/// only way back from this child is the exit status, because a pipe from an unprivileged child
/// into the root parent is a surface this module exists to avoid (see [`super::fork_as_account`]). The
/// syscall's identity is what makes the failure reproducible by hand; the errno follows from
/// reproducing it.
pub(super) const EXIT_DROP_SETGROUPS_FAILED: i32 = 73;

/// Child exit status meaning `setgid` returned an error. No work was done.
pub(super) const EXIT_DROP_SETGID_FAILED: i32 = 74;

/// Child exit status meaning `setuid` returned an error. No work was done.
pub(super) const EXIT_DROP_SETUID_FAILED: i32 = 75;

/// Child exit status meaning the drop was accepted but did not fully apply.
/// `EX_NOPERM` from `sysexits.h`. No work was done.
pub(super) const EXIT_VERIFICATION_FAILED: i32 = 77;

/// Child exit status meaning the work closure ran as the account and failed.
pub(super) const EXIT_WORK_FAILED: i32 = 78;

#[cfg(test)]
#[path = "../tests/privs/child_exit_status_tests.rs"]
mod tests;
