//! The one and only way the agent does work as a hosting customer.

use std::panic::AssertUnwindSafe;
use std::time::{Duration, Instant};

use super::account_ids::AccountIds;
use super::priv_error::PrivError;

/// Child exit status meaning the work ran as the account and succeeded.
const EXIT_OK: i32 = 0;

/// Child exit status meaning one of the three drop syscalls returned an error.
/// No work was done.
const EXIT_DROP_FAILED: i32 = 76;

/// Child exit status meaning the drop was accepted but did not fully apply.
/// `EX_NOPERM` from `sysexits.h`. No work was done.
const EXIT_VERIFICATION_FAILED: i32 = 77;

/// Child exit status meaning the work closure ran as the account and failed.
const EXIT_WORK_FAILED: i32 = 78;

/// Largest supplementary group list the child will inspect.
///
/// After a correct `setgroups` the list holds exactly one entry, so a list that
/// does not fit here is not a large list — it is evidence that `setgroups` did
/// not take effect, and the child treats it as a verification failure. Sized as a
/// fixed stack array on purpose: see the note on allocation in
/// [`fork_as_account`].
const MAXIMUM_GROUPS: usize = 32;

/// How long the parent waits for a child before killing it.
///
/// The child creates a directory or writes one file, so two minutes is not a
/// budget for slow work — it is the point past which the child is not working at
/// all. Without a ceiling a wedged child costs a blocking-pool thread forever and
/// leaves a process at the customer's uid holding every descriptor the parent had
/// at fork time, which is a hang rather than an error and therefore the worst
/// signal this module could produce.
const CHILD_PATIENCE: Duration = Duration::from_secs(120);

/// How long the parent waits for a killed child to become collectable.
///
/// A ceiling of its own, because the kill and the reap are two separate
/// mechanisms and only one of them is argued by a comment. The reap used to be a
/// blocking `waitpid` justified by "SIGKILL cannot be caught", which is true of
/// the signal this module sends and not enforced anywhere: one edit to a signal
/// number turned the recovery path into the permanent hang [`CHILD_PATIENCE`]
/// exists to prevent, and left every test green. A ceiling whose enforcement arm
/// can itself hang is not a ceiling, so the enforcement arm gets one too.
///
/// Five seconds is not a budget for the kernel to tear a process down — that
/// takes microseconds — it is the point past which the child is not dying at
/// all, which is a state the caller must be told about rather than wait out.
const REAP_PATIENCE: Duration = Duration::from_secs(5);

/// First descriptor the child closes: 0, 1 and 2 are kept.
///
/// stdin, stdout and stderr stay open because the child shares the daemon's, and
/// a process whose first `open` lands on descriptor 1 is a far worse hazard than
/// an inherited terminal.
const FIRST_INHERITED_DESCRIPTOR: libc::c_uint = 3;

/// Highest descriptor the fallback sweep will try to close.
///
/// Only reached when `close_range` is unavailable (a kernel older than 5.9), and
/// bounded so that an `RLIMIT_NOFILE` of "infinity" cannot turn the sweep into
/// two billion syscalls.
const FALLBACK_SWEEP_CEILING: u32 = 65_536;

/// First gap between two `WNOHANG` polls of the child.
///
/// Short, because the overwhelming majority of children finish in well under a
/// millisecond and the first poll after the fork usually collects them.
const FIRST_NAP: Duration = Duration::from_millis(1);

/// Longest gap the poll interval grows to.
///
/// The gap doubles from [`FIRST_NAP`] so that a quick child is collected almost
/// immediately while a long one is not polled tens of thousands of times.
const LONGEST_NAP: Duration = Duration::from_millis(20);

/// Runs `work` in a forked child that has dropped to `ids`, and waits for it.
///
/// This is the only place in the workspace that drops privileges, and every
/// customer file operation goes through it (rules/rust.md "Privileges"). The
/// sequence, and why each step is where it is:
///
/// 1. **`fork` first.** `setuid` and its relatives apply to the whole *process*,
///    not to the calling thread. Called on a tokio worker they would drop the
///    privilege of the entire daemon — every request in flight and every request
///    afterwards — with nothing to signal it had happened. Forking first confines
///    the drop to a process that exists only to do this one job.
/// 2. **`setgroups`, then `setgid`, then `setuid`, and never any other order.**
///    Clearing the supplementary groups requires `CAP_SETGID`, which is exactly
///    what `setuid` gives up. Dropping the uid first therefore leaves a process
///    running as the customer while still holding root's supplementary groups —
///    a state that reads as "dropped" everywhere except where it matters.
/// 3. **The child verifies, then acts.** It re-reads its real and effective uid
///    and gid and its supplementary group list, and confirms that root can no
///    longer be regained, because a partially applied drop is indistinguishable
///    from a successful one when viewed from the parent.
/// 4. **Then it closes what it inherited.** `fork` copies the daemon's entire
///    descriptor table, and this module never `exec`s, so `O_CLOEXEC` does not
///    apply: without the sweep `close_inherited_descriptors` performs, a process at
///    the customer's uid would hold the agent's listening socket and every other
///    tenant's connection.
///
/// The child does the narrowest possible unit of work and `_exit`s. It must not
/// be handed a closure that takes a lock or touches the tokio runtime: only the
/// forking thread survives into the child, so a mutex another thread held at
/// fork time stays locked forever in the child. Create one directory, write one
/// file, exit.
///
/// **On allocation, precisely, because this contract used to say "must not
/// allocate freely" and every closure in the workspace broke it.** The hazard is
/// real in general — a mutex is a mutex, and the allocator's arena is one — but
/// it does not apply to the system allocator on the platforms Maran supports,
/// and pretending otherwise made the rule one that was cited and not obeyed:
/// `std::fs::create_dir_all` allocates, `OpenOptions::open` allocates a
/// `CString` for its path inside `std`, and every `*at` wrapper in this module
/// allocates one per name. glibc registers `pthread_atfork` handlers that take
/// the malloc locks in the parent before the fork and reinitialise the arenas in
/// the child (`__malloc_fork_lock_parent` / `__malloc_fork_unlock_child`), and
/// musl's `fork` takes the equivalent lock; so a child that allocates through
/// the system allocator after `fork` cannot deadlock on it. What the rule
/// therefore is:
///
/// - **Allowed**: short-lived allocation through the global allocator, of a size
///   the child chooses — a path, a name, a small buffer.
/// - **Still forbidden**: any lock this program owns; anything that touches the
///   tokio runtime; and unbounded allocation, which is a memory bound rather
///   than a deadlock question and belongs to the caller's own input validation.
/// - **Would break the argument**: replacing the global allocator with one that
///   registers no fork handlers. There is none in this workspace, and adding one
///   would need this paragraph revisited — which is why it names the mechanism
///   rather than only the conclusion.
///
/// The drop-and-verify path itself stays allocation-free regardless (a fixed
/// stack array for `getgroups`, one syscall for the descriptor sweep), because
/// that code runs BEFORE the child has confirmed it is unprivileged and is held
/// to the stricter async-signal-safety standard rather than to this one. Work
/// that can be done in the parent still should be — `ProcessFilesHost` builds
/// its temporary file name before forking — because the smallest child is still
/// the best child.
///
/// **Callers MUST invoke this from `tokio::task::spawn_blocking`, never from a
/// runtime worker.** It forks and then waits for as long as `work` takes, so on a
/// worker thread it stalls every other in-flight command (rules/rust.md "Async
/// and blocking").
///
/// The wait is bounded at `CHILD_PATIENCE` (two minutes), after which the child is SIGKILLed
/// and reaped and the caller is told. A `work` closure that hangs therefore costs
/// one blocking-pool thread for two minutes rather than forever, and leaves no
/// process running at the customer's uid holding the parent's descriptors. An
/// unbounded wait here would turn every hazard in this module into a hang, which
/// is the one outcome an operator cannot act on.
///
/// The child's outcome crosses back as an exit status and nothing else. There is
/// deliberately no pipe carrying a rich error: a channel from an unprivileged
/// child into the root parent is a surface, and this module exists to have as
/// few of those as possible.
///
/// # Errors
///
/// Returns [`PrivError::ForkFailed`] when no child could be created (no work was
/// done); [`PrivError::DropFailed`] when a drop syscall failed in the child;
/// [`PrivError::VerificationFailed`] when the drop did not fully apply and the
/// child refused to continue; [`PrivError::WorkFailed`] when `work` itself
/// returned an error or panicked; [`PrivError::ChildSignalled`] when the child
/// was killed, in which case the work may have been applied in part;
/// [`PrivError::ChildTimedOut`] when the child outlasted `CHILD_PATIENCE` (two minutes) and
/// was killed; [`PrivError::ChildNotCollected`] when it was killed and was still
/// not collectable within `REAP_PATIENCE`; [`PrivError::WaitFailed`] when the
/// child's outcome could not be collected; [`PrivError::ChildDidNotExit`] when the wait status says the child
/// neither exited nor died; and [`PrivError::UnexpectedExit`] for an exit code
/// this module does not produce.
pub fn fork_as_account<F>(ids: &AccountIds, work: F) -> Result<(), PrivError>
where
    F: FnOnce() -> Result<(), PrivError>,
{
    // SAFETY: `fork` takes no arguments and touches no memory of ours. The child
    // it creates runs only the code below, which is confined to async-signal-safe
    // syscalls until the drop is verified, and which always ends in `_exit` — so
    // the child never returns into the caller's stack and never runs a destructor
    // for state the parent also owns.
    let child = unsafe { libc::fork() };

    // Matched, not compared. `fork` returns exactly -1 on failure, 0 in the child
    // and a pid in the parent, and a `<` comparison on that is one edit away from
    // letting -1 through to the parent arm — where `waitpid(-1, …)` waits for ANY
    // child of the root daemon and reports a stranger's exit status as this
    // operation's outcome. The three cases are named so that no fourth exists;
    // [`wait_until`] refuses a non-positive pid as well, because one structural
    // guard for this is not enough (rules/security.md: defense in depth).
    match child {
        -1 => Err(PrivError::ForkFailed {
            errno: last_errno(),
        }),
        0 => {
            // In the child. Nothing below may return: every path ends in `_exit`,
            // which — unlike `exit` — runs no atexit handlers and flushes no stdio
            // buffers. Flushing here would write the parent's buffered bytes a
            // second time, from a process the parent does not know about.
            // `scripts/lib/check-structure.sh` rejects `exit` in this module, so
            // that invariant is a gate rather than a paragraph.
            let status = drop_to(ids).map_or_else(
                |failure| failure,
                |()| {
                    // The drop verified, so this process is the customer — and it
                    // is still holding every descriptor the daemon had open at
                    // fork time. They go before `work` runs, not after.
                    close_inherited_descriptors();

                    // A panic must not unwind past this point: unwinding out of the
                    // fork would let a second copy of the caller's stack keep
                    // running, in a process the caller does not know exists.
                    //
                    // The panic HOOK is deliberately NOT replaced here. Silencing
                    // it would mean calling `std::panic::set_hook` in the child,
                    // which takes the standard library's process-global hook lock
                    // and drops the previous hook's box — a lock and an allocator
                    // round trip on EVERY call, in a forked child of a
                    // multi-threaded process, which is exactly what this
                    // function's own contract forbids its callers from doing. The
                    // default hook is a hazard only on a path that must not be
                    // taken at all: agent code returns errors and never panics
                    // (clippy `panic = "deny"`), the closure here creates one
                    // directory, and if one ever does panic the parent's
                    // [`CHILD_PATIENCE`] turns a wedged child into a killed child
                    // and a typed error rather than a permanent hang.
                    match std::panic::catch_unwind(AssertUnwindSafe(work)) {
                        Ok(Ok(())) => EXIT_OK,
                        Ok(Err(_)) | Err(_) => EXIT_WORK_FAILED,
                    }
                },
            );

            // SAFETY: `_exit` never returns and never touches memory. It is the
            // only correct way out of a forked child that shares the parent's
            // stdio.
            unsafe { libc::_exit(status) };
        }
        _ => wait_for(child),
    }
}

/// Drops the calling process to `ids` and verifies that it really happened.
///
/// Runs only in the forked child. Returns the exit status to use on failure, so
/// the caller does not have to decide what a failure means.
///
/// # Errors
///
/// Returns [`EXIT_DROP_FAILED`] when a syscall failed and
/// [`EXIT_VERIFICATION_FAILED`] when the credentials afterwards are not the ones
/// that were asked for.
fn drop_to(ids: &AccountIds) -> Result<(), i32> {
    let uid = ids.uid();
    let gid = ids.gid();
    let only_group = [gid];

    // SAFETY: `only_group` is a live array of exactly one `gid_t`, and 1 is the
    // length passed. `setgroups` reads that many entries and retains nothing.
    // It is called FIRST because it needs the CAP_SETGID that `setuid` below
    // gives away; see the ordering note on `fork_as_account`.
    if unsafe { libc::setgroups(1, only_group.as_ptr()) } != 0 {
        return Err(EXIT_DROP_FAILED);
    }

    // SAFETY: `setgid` takes an integer and touches no memory. Called before
    // `setuid` for the same reason: afterwards the process cannot change its gid.
    if unsafe { libc::setgid(gid) } != 0 {
        return Err(EXIT_DROP_FAILED);
    }

    // SAFETY: `setuid` takes an integer and touches no memory. It is last, and it
    // is irreversible here — the process is root, so it sets the real, effective
    // and saved uid together, leaving no saved id to return through.
    if unsafe { libc::setuid(uid) } != 0 {
        return Err(EXIT_DROP_FAILED);
    }

    verify(uid, gid, &observe())
}

/// What the child saw about itself immediately after the drop.
///
/// A fixed-size struct: no allocation, so gathering it keeps the child's
/// allocation-free guarantee (see the note on [`fork_as_account`]).
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
/// Split from [`verify`] for the same reason [`wait_for`] is split from
/// [`outcome_of`]: the judgement then becomes a pure function that a test can
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

/// Closes every descriptor the child inherited from the daemon, keeping stdio.
///
/// `fork` duplicates the parent's whole descriptor table into the child, and
/// this module never `exec`s — so `O_CLOEXEC`, which is what the standard
/// library sets on the descriptors it opens, buys nothing here. Without this
/// sweep a process running at a hosting customer's uid holds the agent's
/// listening unix socket, every accepted connection of every other tenant, the
/// log file, and the runtime's epoll and signal descriptors. Those were
/// permission-checked once, at `open` time, as root; nothing re-checks them
/// against the new uid, so a child that could be steered into writing to one
/// would be writing into the panel's own control channel.
///
/// Runs in the child, after the drop has verified and before `work`, and is
/// async-signal-safe and allocation-free like everything else on that path.
///
/// A failure is not reported: there is no channel out of the child other than an
/// exit status, and refusing to do the work because a descriptor could not be
/// closed would turn a hardening step into an outage. The fallback below is what
/// makes a failure of the fast path a non-event.
fn close_inherited_descriptors() {
    // SAFETY: `close_range` takes three integers and touches no memory. It is
    // the async-signal-safe way to do this: one syscall, no allocation, no
    // directory read. Descriptors 0-2 are excluded by the first argument.
    if unsafe { libc::close_range(FIRST_INHERITED_DESCRIPTOR, libc::c_uint::MAX, 0) } == 0 {
        return;
    }

    // `close_range` landed in Linux 5.9. On an older kernel it answers ENOSYS,
    // and the sweep is done one descriptor at a time instead — still no
    // allocation, still no directory read, just more syscalls on a path taken
    // once per customer file operation.
    let mut limit = libc::rlimit {
        rlim_cur: 0,
        rlim_max: 0,
    };

    // SAFETY: `limit` is a live, writable `rlimit`, which is what `getrlimit`
    // expects; it writes nothing else and cannot fail for RLIMIT_NOFILE.
    let ceiling = if unsafe { libc::getrlimit(libc::RLIMIT_NOFILE, &raw mut limit) } == 0 {
        u32::try_from(limit.rlim_cur).unwrap_or(FALLBACK_SWEEP_CEILING)
    } else {
        FALLBACK_SWEEP_CEILING
    }
    .min(FALLBACK_SWEEP_CEILING);

    for descriptor in FIRST_INHERITED_DESCRIPTOR..ceiling {
        // SAFETY: `close` takes an integer and touches no memory. A descriptor
        // that is not open answers EBADF, which is the expected answer for most
        // of this range and is deliberately ignored.
        unsafe { libc::close(libc::c_int::try_from(descriptor).unwrap_or(libc::c_int::MAX)) };
    }
}

/// Collects the child's outcome.
///
/// Split from [`outcome_of`], which decides what a raw wait status means, for
/// the reason every seam in this workspace exists: the decision can then be
/// tested against every status the child can produce — including the ones that
/// require a partially applied privilege drop or a kill, which no test may
/// arrange for real — while this function keeps only the `waitpid` loop that
/// needs a live child.
///
/// # Errors
///
/// As documented on [`fork_as_account`]; `Ok(())` only for [`EXIT_OK`].
fn wait_for(child: libc::pid_t) -> Result<(), PrivError> {
    wait_until(child, CHILD_PATIENCE)
}

/// The wait, with its ceiling supplied.
///
/// The seam exists so the give-up path can be tested rather than reasoned about,
/// which is the same argument `follow_log::follow_with_patience` was built on:
/// a test that waited out [`CHILD_PATIENCE`] would never be written, so the one
/// branch that saves a blocking-pool thread would be the one branch with no test.
///
/// # Errors
///
/// As documented on [`fork_as_account`].
fn wait_until(child: libc::pid_t, patience: Duration) -> Result<(), PrivError> {
    // A pid this function was given rather than one it forked itself. 0 means
    // "every process in my group" and -1 means "any child at all", and either
    // would make this call reap a process it knows nothing about and report that
    // stranger's exit status as this operation's outcome. Refused before the
    // syscall rather than trusted (rules/rust.md "Validation first").
    if child <= 0 {
        return Err(PrivError::WaitFailed {
            errno: libc::EINVAL,
        });
    }

    let deadline = Instant::now() + patience;
    let mut status: libc::c_int = 0;
    let mut nap = FIRST_NAP;

    loop {
        // SAFETY: `status` is a live, writable local `c_int`, which is what
        // `waitpid` expects; it writes nothing else. `child` is positive and is a
        // pid this function's caller just forked and has not reaped, so it cannot
        // name an unrelated process — and because neither an EINTR return nor a
        // WNOHANG "not yet" reaps anything, the pid cannot be recycled between
        // iterations of this loop either.
        let reaped = unsafe { libc::waitpid(child, &raw mut status, libc::WNOHANG) };

        if reaped == child {
            return outcome_of(status);
        }

        if reaped < 0 {
            // Restarted on EINTR, which is a path this daemon will actually take:
            // the agent registers tokio signal handlers, and `systemctl stop`
            // signals every process in the cgroup. Returning on EINTR would leave
            // the child running and never reaped — a zombie per occurrence, and a
            // caller told the outcome is unknown for work that then completes
            // anyway. Every OTHER errno must return, or a real failure becomes an
            // infinite loop, which is a worse signal than any error.
            let errno = last_errno();
            if errno != libc::EINTR {
                return Err(PrivError::WaitFailed { errno });
            }
            continue;
        }

        // `reaped == 0`: still running. The child does one narrow unit of work, so
        // reaching the deadline means it is wedged — a lock frozen at fork time, a
        // `work` closure that hangs — and waiting on it forever would cost a
        // blocking-pool thread permanently while a process at the customer's uid
        // holds every descriptor the parent had. It is killed and reaped instead.
        if Instant::now() >= deadline {
            return give_up_on(child);
        }

        std::thread::sleep(nap);
        nap = (nap * 2).min(LONGEST_NAP);
    }
}

/// Kills a child that outlasted its patience and reaps it, so nothing is left
/// running and nothing is left a zombie.
///
/// The reap has a ceiling of its own ([`REAP_PATIENCE`]) and polls rather than
/// blocking. The old shape blocked in `waitpid` forever on the reasoning that
/// SIGKILL cannot be caught — true of the signal, but nothing enforced that this
/// is the signal sent, and a catchable one turned this recovery path into the
/// permanent hang [`CHILD_PATIENCE`] was written to remove. The bound is
/// therefore in the code rather than in the argument, and a child that survives
/// what was sent to it produces a typed error instead of a stuck thread.
///
/// # Errors
///
/// [`PrivError::ChildTimedOut`] when the child was killed and collected;
/// [`PrivError::ChildNotCollected`] when it was still not collectable after
/// [`REAP_PATIENCE`]; [`PrivError::WaitFailed`] when the wait itself failed.
fn give_up_on(child: libc::pid_t) -> Result<(), PrivError> {
    give_up_on_with(child, libc::SIGKILL, REAP_PATIENCE)
}

/// The give-up, with its signal and its reap ceiling supplied.
///
/// The seam exists for the same reason [`wait_until`]'s does: the outcome of a
/// child that does NOT die of what it was sent is a real branch, and without a
/// way to send a catchable signal it would be the one branch no test could
/// reach — so it would be the one branch free to become a permanent hang again.
/// Production always calls it through [`give_up_on`], with SIGKILL.
///
/// # Errors
///
/// As documented on [`give_up_on`].
fn give_up_on_with(
    child: libc::pid_t,
    signal: libc::c_int,
    patience: Duration,
) -> Result<(), PrivError> {
    // SAFETY: `kill` takes two integers and touches no memory. `child` is
    // positive — checked at the top of `wait_until` — so this cannot address a
    // process group.
    unsafe { libc::kill(child, signal) };

    let deadline = Instant::now() + patience;
    let mut status: libc::c_int = 0;
    let mut nap = FIRST_NAP;

    loop {
        // SAFETY: as in `wait_until`. `WNOHANG` rather than a blocking wait, so
        // that the deadline below can be reached at all.
        let reaped = unsafe { libc::waitpid(child, &raw mut status, libc::WNOHANG) };

        if reaped == child {
            return Err(PrivError::ChildTimedOut);
        }

        if reaped < 0 {
            let errno = last_errno();
            if errno != libc::EINTR {
                return Err(PrivError::WaitFailed { errno });
            }
            continue;
        }

        // `reaped == 0`: killed and still not collectable. Either the signal did
        // not end it or the kernel has not finished, and the caller learns which
        // outcome it has rather than waiting for one that may never come.
        if Instant::now() >= deadline {
            return Err(PrivError::ChildNotCollected);
        }

        std::thread::sleep(nap);
        nap = (nap * 2).min(LONGEST_NAP);
    }
}

/// Turns the raw status `waitpid` wrote into this module's typed outcome.
///
/// Takes the status rather than reading it, so that every ending a child can
/// have — including the two that require a privilege drop to have gone wrong —
/// is a value a test can hand it.
///
/// # Errors
///
/// As documented on [`fork_as_account`]; `Ok(())` only for [`EXIT_OK`].
fn outcome_of(status: libc::c_int) -> Result<(), PrivError> {
    if libc::WIFSIGNALED(status) {
        // Killed part-way. The caller is told, because half a write is a state the
        // operation above this one has to converge from on its next attempt.
        return Err(PrivError::ChildSignalled {
            signal: libc::WTERMSIG(status),
        });
    }

    if !libc::WIFEXITED(status) {
        // A separate variant, not `UnexpectedExit`, because the number means
        // something different: here it is the RAW wait status, below it is an exit
        // code. One variant carrying both would print
        // "unexpected status 4991" for a child that exited 19 and for one that
        // never exited at all, and an operator could not tell which they had.
        return Err(PrivError::ChildDidNotExit { status });
    }

    match libc::WEXITSTATUS(status) {
        EXIT_OK => Ok(()),
        EXIT_DROP_FAILED => Err(PrivError::DropFailed),
        EXIT_VERIFICATION_FAILED => Err(PrivError::VerificationFailed),
        EXIT_WORK_FAILED => Err(PrivError::WorkFailed),
        other => Err(PrivError::UnexpectedExit { status: other }),
    }
}

/// The current thread's `errno`.
///
/// Read through `std::io::Error` rather than through a second `unsafe` block: the
/// standard library already owns a correct, per-thread reader for it, and this
/// module's `unsafe` budget is spent only where nothing safe exists.
fn last_errno() -> i32 {
    std::io::Error::last_os_error().raw_os_error().unwrap_or(0)
}

#[cfg(test)]
#[path = "../tests/privs/fork_as_account_tests.rs"]
mod tests;
