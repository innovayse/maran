//! Closing the descriptors a forked child inherited from the daemon.

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

/// Closes every descriptor the child inherited from the daemon, keeping stdio and
/// at most one descriptor the caller names.
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
/// **`spare` is the one exception, and it is ONE.** The two entry points that
/// bring data back from the child ([`super::fork_as_account_for_bytes`] and
/// [`super::fork_as_account_for_descriptor`]) create their channel before the
/// fork and need the child to keep exactly its own end of it. Sparing the
/// descriptor here, in the same sweep, is what keeps the channel from being a
/// second hole: the child still holds nothing else the daemon had open, and the
/// descriptor it keeps is one the parent made for this call and knows the far
/// end of. The parameter is a single descriptor and not a list on purpose — a
/// call that needed two would be a call to review, not one to wave through. A
/// caller with nothing to keep passes `None` and gets exactly the old sweep.
///
/// Runs in the child, after the drop has verified and before `work`, and is
/// async-signal-safe and allocation-free like everything else on that path.
///
/// A failure is not reported: there is no channel out of the child other than an
/// exit status, and refusing to do the work because a descriptor could not be
/// closed would turn a hardening step into an outage. The fallback below is what
/// makes a failure of the fast path a non-event.
pub(super) fn close_inherited_descriptors(spare: Option<libc::c_int>) {
    // The descriptors on either side of the spared one, as `close_range` takes
    // them: both bounds inclusive. With nothing to spare the whole range above
    // stdio is one call, exactly as before. A spare at or below stdio (never the
    // case for a descriptor the daemon created, which sits above 2) leaves the
    // low range empty rather than underflowing.
    let swept = match spare.and_then(|keep| libc::c_uint::try_from(keep).ok()) {
        None => close_range(FIRST_INHERITED_DESCRIPTOR, libc::c_uint::MAX),
        Some(keep) => {
            let below = if keep > FIRST_INHERITED_DESCRIPTOR {
                close_range(FIRST_INHERITED_DESCRIPTOR, keep - 1)
            } else {
                true
            };
            let above = if keep < libc::c_uint::MAX {
                close_range(
                    keep.max(FIRST_INHERITED_DESCRIPTOR - 1) + 1,
                    libc::c_uint::MAX,
                )
            } else {
                true
            };
            below && above
        }
    };
    if swept {
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

    close_one_by_one(ceiling, spare);
}

/// Closes `first..=last` with the `close_range` system call; `true` when the kernel did it.
///
/// The RAW syscall, not glibc's `close_range` wrapper, and the difference decides which
/// distributions Maran can be installed on. The wrapper appeared in glibc 2.34, so linking
/// against it makes the agent unbuildable on anything older — measured on AlmaLinux 8
/// (glibc 2.28): `rust-lld: error: undefined symbol: close_range`. That is EL8 and Rocky 8,
/// which the supported matrix accepts and much of the hosting fleet still runs (issue #50).
///
/// Going through `syscall` costs nothing here and changes no behaviour: the kernel entry point
/// is the same one the wrapper calls, and the fallback was already written for the kernel
/// that lacks it. EL8's kernel is 4.18 and `close_range` arrived in 5.9, so on those hosts this
/// returns ENOSYS at RUNTIME too — the wrapper would have been useless there even if it had
/// linked, and the one-descriptor-at-a-time sweep is what actually protects the customer's
/// child. `syscall` returns -1 on failure, so success is tested as `== 0` exactly as before.
fn close_range(first: libc::c_uint, last: libc::c_uint) -> bool {
    // SAFETY: `SYS_close_range` takes three integers and touches no memory. It is the
    // async-signal-safe way to do this: one syscall, no allocation, no directory read.
    unsafe { libc::syscall(libc::SYS_close_range, first, last, 0) == 0 }
}

/// Closes every descriptor from [`FIRST_INHERITED_DESCRIPTOR`] below `ceiling`, one at a time,
/// except `spare`.
///
/// Split from [`close_inherited_descriptors`] so the fallback — the path a 4.18 kernel takes on
/// every call — can be driven by a test on a kernel that has `close_range`; otherwise the one
/// branch EL8 actually runs would be the one branch with no test.
fn close_one_by_one(ceiling: u32, spare: Option<libc::c_int>) {
    for descriptor in FIRST_INHERITED_DESCRIPTOR..ceiling {
        let descriptor = libc::c_int::try_from(descriptor).unwrap_or(libc::c_int::MAX);
        if spare == Some(descriptor) {
            continue;
        }

        // SAFETY: `close` takes an integer and touches no memory. A descriptor
        // that is not open answers EBADF, which is the expected answer for most
        // of this range and is deliberately ignored.
        unsafe { libc::close(descriptor) };
    }
}

#[cfg(test)]
#[path = "../tests/privs/close_inherited_descriptors_tests.rs"]
mod tests;
