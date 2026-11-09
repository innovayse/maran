//! Dropping the forked child to the account's ids, and proving the drop applied.

use super::account_ids::AccountIds;
use super::child_exit_status::{
    EXIT_DROP_SETGID_FAILED, EXIT_DROP_SETGROUPS_FAILED, EXIT_DROP_SETUID_FAILED,
    EXIT_VERIFICATION_FAILED,
};

/// Largest supplementary group list the child will inspect.
///
/// After a correct `setgroups` the list holds exactly one entry, so a list that
/// does not fit here is not a large list — it is evidence that `setgroups` did
/// not take effect, and the child treats it as a verification failure. Sized as a
/// fixed stack array on purpose: see the note on allocation in
/// [`super::fork_as_account`].
const MAXIMUM_GROUPS: usize = 32;

/// Drops the calling process to `ids` and verifies that it really happened.
///
/// Runs only in the forked child. Returns the exit status to use on failure, so
/// the caller does not have to decide what a failure means.
///
/// # The ordering of the three syscalls is NOT observable locally
///
/// Said here because a developer who breaks it sees green. Swapping `setuid`
/// ahead of `setgroups` — the classic privilege-drop defect, which leaves the
/// child in root's supplementary groups with no capability left to leave them —
/// was measured surviving the ENTIRE local Rust suite: 1472 tests, none of which
/// forks as another account, because a local run is not root. It is caught only
/// where a real drop happens, in the polygon suites run as root on both
/// families, where the same break puts one named test plus twenty-five further
/// host tests red (`.github/workflows/agent.yml`).
///
/// So: `cargo test` passing is NOT evidence about the four lines below, and the
/// order they are written in is load-bearing rather than stylistic. Change it
/// only against a polygon run.
///
/// # Errors
///
/// Returns the exit status naming the syscall that failed, and
/// [`EXIT_VERIFICATION_FAILED`] when the credentials afterwards are not the ones
/// that were asked for.
pub(super) fn drop_to(ids: &AccountIds) -> Result<(), i32> {
    let uid = ids.uid();
    let gid = ids.gid();
    let only_group = [gid];

    // SAFETY: `only_group` is a live array of exactly one `gid_t`, and 1 is the
    // length passed. `setgroups` reads that many entries and retains nothing.
    // It is called FIRST because it needs the CAP_SETGID that `setuid` below
    // gives away; see the ordering note on `super::fork_as_account`.
    if unsafe { libc::setgroups(1, only_group.as_ptr()) } != 0 {
        return Err(EXIT_DROP_SETGROUPS_FAILED);
    }

    // SAFETY: `setgid` takes an integer and touches no memory. Called before
    // `setuid` for the same reason: afterwards the process cannot change its gid.
    if unsafe { libc::setgid(gid) } != 0 {
        return Err(EXIT_DROP_SETGID_FAILED);
    }

    // SAFETY: `setuid` takes an integer and touches no memory. It is last, and it
    // is irreversible here — the process is root, so it sets the real, effective
    // and saved uid together, leaving no saved id to return through.
    if unsafe { libc::setuid(uid) } != 0 {
        return Err(EXIT_DROP_SETUID_FAILED);
    }

    verify(uid, gid, &observe())
}

/// What the child saw about itself immediately after the drop.
///
/// A fixed-size struct: no allocation, so gathering it keeps the child's
/// allocation-free guarantee (see the note on [`super::fork_as_account`]).
struct Observed {
    /// The real user id the kernel reports.
    real_uid: libc::uid_t,
    /// The effective user id the kernel reports.
    effective_uid: libc::uid_t,
    /// The real group id the kernel reports.
    real_gid: libc::gid_t,
    /// The effective group id the kernel reports.
    effective_gid: libc::gid_t,
    /// The supplementary group list, valid up to `group_count` entries.
    groups: [libc::gid_t; MAXIMUM_GROUPS],
    /// What `getgroups` returned: a count, or -1 for a list that did not fit.
    group_count: libc::c_int,
    /// Whether asking for uid 0 back SUCCEEDED. True is the catastrophic answer.
    root_regained: bool,
}

/// Reads the child's own credentials. Makes no decision about them.
///
/// Split from [`verify`] for the same reason `wait_for` is split from
/// `outcome_of`: the judgement then becomes a pure function that a test can
/// hand every combination a lying drop can produce, on any machine, without
/// root — while the syscalls stay here, where they contain no decision at all
/// and only six reads. There is deliberately no way to make this function
/// return anything but what the kernel said: an injectable *drop* would be a new
/// attack surface inside the one module that exists to have none.
fn observe() -> Observed {
    // SAFETY: each of these reads an integer credential of the calling process.
    // None takes a pointer, none can fail, none touches memory.
    let (real_uid, effective_uid, real_gid, effective_gid) = unsafe {
        (
            libc::getuid(),
            libc::geteuid(),
            libc::getgid(),
            libc::getegid(),
        )
    };

    let mut groups = [0 as libc::gid_t; MAXIMUM_GROUPS];

    // SAFETY: `groups` is a live, writable array of exactly MAXIMUM_GROUPS
    // `gid_t`, and that is the length passed. `getgroups` writes at most that many
    // entries and returns -1 with EINVAL rather than overflowing if there are
    // more.
    let group_count = unsafe {
        libc::getgroups(
            libc::c_int::try_from(MAXIMUM_GROUPS).unwrap_or(libc::c_int::MAX),
            groups.as_mut_ptr(),
        )
    };

    // The last read is behavioural rather than declarative: ask for root back and
    // record whether the kernel refused. A saved-set uid left behind by an
    // incomplete drop is invisible to `getuid`/`geteuid` and would let anything
    // the child later executes climb straight back to root.
    //
    // SAFETY: `setuid` takes an integer and touches no memory. It is expected to
    // fail; if it were ever to succeed the process would be root again, which is
    // precisely why [`verify`] refuses that observation.
    let root_regained = unsafe { libc::setuid(0) } == 0;

    Observed {
        real_uid,
        effective_uid,
        real_gid,
        effective_gid,
        groups,
        group_count,
        root_regained,
    }
}

/// Confirms that the drop applied completely, before any file is touched.
///
/// Checks the real *and* effective ids of both kinds, the supplementary group
/// list, and that root could not be regained. A partial drop — a `setuid` that
/// moved the effective uid but left a saved one, or a `setgroups` that was
/// skipped — looks exactly like a successful drop from the parent, so it is
/// checked here where the answer is still available.
///
/// Pure: it reads nothing and calls nothing. Every state it refuses is a value
/// its tests construct.
///
/// # Errors
///
/// Returns [`EXIT_VERIFICATION_FAILED`] on any mismatch.
fn verify(uid: libc::uid_t, gid: libc::gid_t, seen: &Observed) -> Result<(), i32> {
    if seen.real_uid != uid
        || seen.effective_uid != uid
        || seen.real_gid != gid
        || seen.effective_gid != gid
    {
        return Err(EXIT_VERIFICATION_FAILED);
    }

    // Exactly the primary group, or nothing at all: both are what a correct
    // `setgroups([gid])` can leave behind. Anything else — including the -1 that
    // means the list did not fit — says the supplementary groups survived, which
    // is the failure this whole ordering exists to prevent.
    let dropped_groups = match seen.group_count {
        0 => true,
        1 => seen.groups[0] == gid,
        _ => false,
    };
    if !dropped_groups {
        return Err(EXIT_VERIFICATION_FAILED);
    }

    if seen.root_regained {
        return Err(EXIT_VERIFICATION_FAILED);
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/privs/drop_to_account_tests.rs"]
mod tests;
