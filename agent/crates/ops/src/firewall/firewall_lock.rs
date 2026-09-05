//! The one lock every firewall mutation is serialised by.

use tokio::sync::{Mutex, MutexGuard};

/// The lock itself: one per process, and a process is one host's whole agent.
static FIREWALL_MUTATIONS: Mutex<()> = Mutex::const_new(());

/// Takes the lock every mutating firewall operation runs under, and blocks
/// until it has it.
///
/// # Why the firewall needs a lock at all
///
/// Two of this area's operations are check-then-act against state that lives
/// in the kernel, and both lose a race:
///
/// - `ensure_bans_table` asks whether `table inet maran_bans` is loaded and
///   applies the bans file only when it is not. **Re-applying that file over
///   an existing table ERASES its elements** — the file carries the same
///   create-delete-redeclare idiom the ruleset does, verified on nftables
///   v1.0.9, which is exactly why the table is applied once and never again.
///   Two concurrent first-bans without this lock both see an absent table,
///   both apply the file, and the second apply erases the first's ban. The
///   panel would record a ban that the kernel is not holding.
/// - `allow_port` and `deny_port` read the rendered ruleset, change one rule
///   and write the whole file back. Two concurrent changes without this lock
///   both read the same file, and whichever renames last silently discards
///   the other's rule.
///
/// # Why a process-wide lock is the honest fix
///
/// A root daemon has exactly one instance on a host, and this file and this
/// kernel table are single, shared, host-wide resources. There is no tenant
/// to scope a lock to and nothing finer-grained to lock: the unit of
/// serialisation IS the host's firewall. A lock per operation, or per file,
/// would let a ban race a rule change over the same `nft` process and the
/// same directory.
///
/// # The requirement this places on every caller
///
/// **An operation that takes this lock MUST be invoked from
/// `tokio::task::spawn_blocking`, and MUST NOT be awaited on a runtime
/// worker.** The guard is taken with `blocking_lock`, which is what
/// [`tokio::sync::Mutex`] provides for synchronous code sharing a lock with
/// asynchronous code — it is correct on the blocking pool, where blocking is
/// the point, and it is not correct anywhere else.
///
/// `services/firewall/firewall_service.rs` carries that requirement today: it
/// wraps every one of the six operations, the way every other service wraps
/// its own, in a private `run` helper that calls `spawn_blocking` and maps the
/// error. Calling an operation directly from an async handler is a defect even
/// on the runs where it appears to work — including the two, `list_rules` and
/// `list_bans`, that take no lock and so cannot lean on the `# Panics` section
/// below.
///
/// # Why nothing in this crate checks that at run time
///
/// It cannot. A closure handed to `spawn_blocking` runs on a blocking-pool
/// thread that still belongs to the runtime, and tokio exposes nothing stable
/// that tells the two threads apart: measured on tokio 1.53 across both
/// runtime flavours, a call awaited on a worker and the same call inside
/// `spawn_blocking` agree on every observable — `Handle::try_current()` is
/// `Ok` for both, `Handle::runtime_flavor()` describes the runtime rather than
/// the thread, `task::try_id()` is `Some` for both, `task::block_in_place`
/// succeeds for both on a multi-threaded runtime, and both threads are even
/// named `tokio-rt-worker`.
///
/// This crate did once carry a `debug_assert!` on
/// `Handle::try_current().is_err()`, and that premise is false in exactly the
/// place it was meant to bless: it fired inside `spawn_blocking`, so every
/// debug build answered `ListRules` and `ListBans` with a panic while release
/// builds compiled the assertion away. What holds the requirement instead is a
/// check of the CALL SITES, which are a static fact rather than a runtime
/// guess: `tests/services/firewall/firewall_service_tests.rs` in the `agent`
/// crate asserts that every call into this area's six operations sits inside
/// `Self::run(move || …)`, and that `run` is `spawn_blocking`.
///
/// # Panics
///
/// `blocking_lock` panics when it is called from inside an asynchronous
/// context, which is tokio refusing to let a runtime worker be blocked. **That
/// panic is the enforcement of the requirement above, not a hazard to work
/// around** — it is a programming error rather than an input, and it fails on
/// the first call, loudly and with a message that names the cause, instead of
/// silently stalling every other in-flight command on the same worker.
/// Suppressing it by reaching for `try_lock` would trade a loud defect for a
/// firewall that stops serialising. It is tokio's own check inside
/// `blocking_lock`, it is not gated by `debug_assertions`, and it fires the
/// same way in a release binary — so the five operations that take this lock
/// are covered at run time in every build. It does NOT fire on the blocking
/// pool, where blocking is the point, which is why it refuses only the callers
/// it should. `list_rules` and `list_bans` take no lock and get none of this;
/// the call-site test named above is the whole of their cover.
pub(crate) fn firewall_lock() -> MutexGuard<'static, ()> {
    FIREWALL_MUTATIONS.blocking_lock()
}
