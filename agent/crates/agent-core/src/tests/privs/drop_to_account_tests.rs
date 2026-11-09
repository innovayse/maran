//! The judgement on a child's credentials after the drop: every lying drop a test can build.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{EXIT_VERIFICATION_FAILED, MAXIMUM_GROUPS, Observed, verify};

/// The credentials of a child whose drop applied completely.
fn a_clean_drop(uid: libc::uid_t, gid: libc::gid_t) -> Observed {
    let mut groups = [0 as libc::gid_t; MAXIMUM_GROUPS];
    groups[0] = gid;

    Observed {
        real_uid: uid,
        effective_uid: uid,
        real_gid: gid,
        effective_gid: gid,
        groups,
        group_count: 1,
        root_regained: false,
    }
}

#[test]
fn a_drop_that_applied_completely_is_accepted() {
    assert_eq!(verify(1000, 1000, &a_clean_drop(1000, 1000)), Ok(()));
}

#[test]
fn a_drop_that_moved_only_the_effective_uid_is_refused() {
    // What `seteuid` leaves behind, and what a `setuid` that returned success
    // without applying looks like: the process reads as the customer to anything
    // that asks `geteuid`, and is root to the kernel.
    let mut seen = a_clean_drop(1000, 1000);
    seen.real_uid = 0;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_drop_that_moved_only_the_effective_gid_is_refused() {
    let mut seen = a_clean_drop(1000, 1000);
    seen.real_gid = 0;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_supplementary_group_that_survived_the_drop_is_refused() {
    // The whole reason `setgroups` runs first: an extra group here means the
    // child is running as the customer while still in one of root's groups.
    let mut seen = a_clean_drop(1000, 1000);
    seen.groups[1] = 0;
    seen.group_count = 2;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_group_list_that_did_not_fit_is_refused_rather_than_ignored() {
    // -1 from `getgroups` means the list is longer than the child will look at,
    // which after a correct `setgroups([gid])` is impossible — so it is evidence
    // the call did not take effect, not a large but acceptable list.
    let mut seen = a_clean_drop(1000, 1000);
    seen.group_count = -1;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn an_empty_group_list_is_accepted_because_setgroups_may_leave_one() {
    let mut seen = a_clean_drop(1000, 1000);
    seen.group_count = 0;

    assert_eq!(verify(1000, 1000, &seen), Ok(()));
}

#[test]
fn a_single_group_that_is_not_the_accounts_own_is_refused() {
    let mut seen = a_clean_drop(1000, 1000);
    seen.groups[0] = 27;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}

#[test]
fn a_child_that_could_ask_for_root_and_get_it_is_refused_though_every_id_reads_correctly() {
    // Every declarative check passes here: this is the saved-set uid case, which
    // `getuid` and `geteuid` cannot see and which lets anything the child later
    // executes climb straight back to root.
    let mut seen = a_clean_drop(1000, 1000);
    seen.root_regained = true;

    assert_eq!(verify(1000, 1000, &seen), Err(EXIT_VERIFICATION_FAILED));
}
