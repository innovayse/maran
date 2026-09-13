//! The one lock every system-configuration write is serialised by.

use tokio::sync::{Mutex, MutexGuard};

/// The lock itself: one per process, and a process is one host's whole agent.
static CONFIG_WRITES: Mutex<()> = Mutex::const_new(());

/// Takes the lock the whole config-write protocol runs under, and blocks until
/// it has it.
///
/// # Why the config-write protocol needs a lock at all
///
/// The protocol renames rendered content into the LIVE tree and only then asks
/// a validator whether the tree is valid, because the validating tool reads
/// the tree by path and cannot see a temporary file
/// (`super::render_validate_swap::write_config` explains that ordering). That
/// makes the window between the rename and the commit a window in a shared
/// resource, and three interleavings fall out of it — every one of them
/// established by reading the code, and the first of them reproduced against a
/// real nginx — driven rather than described by the tests in
/// `crate::tests::safe_write::config_tree_lock_tests` and, on a real nginx, by
/// `a_neighbours_rejected_vhost_cannot_fail_this_tenants_valid_write` in the
/// polygon suite `agent/crates/agent/tests/sites_on_a_real_host.rs`:
///
/// - **A neighbour's in-flight content decides your validation.** `nginx -t`
///   carries no file argument: it parses `nginx.conf` and everything its
///   includes glob in, so it answers about the WHOLE host. While one tenant's
///   invalid vhost is renamed in and not yet rolled back, an unrelated
///   tenant's perfectly valid write validates at exit 1, is rolled back, and
///   is reported to its owner as invalid. It does not even need a syntax
///   error: removing a log directory is enough — nginx answers `[emerg]
///   open() … failed` and refuses the tree.
/// - **Two writers of one target both capture the same previous content and
///   both commit.** One of them is silently lost while returning `Ok`.
/// - **A rollback recreates a file another operation legitimately deleted.**
///   A certificate installation that fails after a concurrent site deletion
///   committed would put the deleted site's vhost back — live, unowned, and
///   removable by no rpc.
///
/// # Why the unit of serialisation is the host
///
/// Per-site is the obvious reach and it is the wrong one, because the
/// validator is not per-site: there is one config tree, one validator that
/// reads all of it, and one daemon that reloads all of it. A per-site or
/// per-tenant lock would leave the first interleaving above exactly as it was.
/// The unit of serialisation IS the host, whether or not that is convenient —
/// the same argument `super::super::firewall::firewall_lock` makes about one
/// kernel table, reached here by a different route.
///
/// # Why the lock is here and not in the operations
///
/// The firewall's lock is taken by each of its five operations. That placement
/// deadlocks here, and the two nestings that make it deadlock are in the tree
/// today: `ssl::delete_site_with_certificate` calls `sites::delete_site`, and
/// `ssl::generate_self_signed` calls `ssl::install_certificate` — while
/// `delete_site` and `install_certificate` are themselves rpc entry points and
/// would have to take the lock in their own right. A mutex that is taken twice
/// on one thread either deadlocks or is re-entrant, and a re-entrant lock in a
/// root process is a lock whose critical section nobody can state.
///
/// Inside the protocol there is no such nesting: `write_config`,
/// `write_config_set` and `remove_config` never call one another, so the lock
/// is taken exactly once per config write. It also means no writer can forget
/// it — `safe_write` is the ONE implementation of this protocol
/// (rules/rust.md "Config writes"), so every area that writes a system config
/// is covered by construction rather than by each area remembering.
///
/// # What it costs
///
/// Every config write on the host serialises: one rename, one `-t`, one
/// reload. The queue is short because each holder is short, and the
/// alternative is the three interleavings above. What it does NOT serialise is
/// the read-and-decide an operation does before it calls in — see
/// "What this lock does not cover" below.
///
/// # Wait rather than refuse
///
/// This lock waits. `crate::accounts::account_lock` refuses instead
/// (`try_lock_owned`), and the difference is the length of the critical
/// section and who is asking. A backup runs for minutes, so a second one
/// queueing behind it would hold a blocking-pool thread for minutes and the
/// caller is better told to come back. A config write holds the lock for a
/// rename plus a validation plus a reload. Refusing one would turn a correct,
/// authorised change into a failure the caller must notice and repeat — and
/// several of these callers are the panel's own background schedulers, whose
/// retry is a whole scheduling cycle away, so a refused certificate renewal is
/// a certificate that expires. Refusing to fix a cross-tenant availability
/// defect by introducing a different one is not a fix.
///
/// # The blocking pool
///
/// A waiting caller occupies one `spawn_blocking` thread. The agent runs
/// `#[tokio::main]` with no `max_blocking_threads` override
/// (`agent/src/main.rs`), so the pool's ceiling is tokio's default of 512
/// threads, against a panel that issues config writes from interactive
/// requests and two schedulers. The queue length is therefore bounded by the
/// panel's own request concurrency, far below the ceiling, and each waiter
/// leaves after one reload rather than after an archive. This is stated rather
/// than measured under load: the audit asked for the question to be addressed,
/// and the honest answer is the arithmetic plus the fact that the same pool
/// already carries backups, which hold a thread for far longer.
///
/// # The requirement this places on every caller
///
/// **An operation that reaches this lock MUST be invoked from
/// `tokio::task::spawn_blocking`, and MUST NOT be awaited on a runtime
/// worker.** The guard is taken with `blocking_lock`, which is what
/// [`tokio::sync::Mutex`] provides for synchronous code sharing a lock with
/// asynchronous code — correct on the blocking pool, where blocking is the
/// point, and correct nowhere else. Every service that reaches `safe_write`
/// carries that requirement today through
/// `agent/src/services/wire/run_blocking.rs`, the one `spawn_blocking`
/// wrapper.
///
/// # Its lifetime, stated where a reader needs it
///
/// **Process-local.** It serialises the config writes of ONE running agent
/// against each other and against nothing else. A second agent binary started
/// by an operator, an installer step that writes `/etc/nginx`, or a hand-run
/// `nginx -s reload` contends with none of it. That is sound for the product's
/// shape — one root daemon per host, systemd-managed, its socket bound
/// exclusively — but it is a comfort rather than a guarantee, and it is the
/// reason [`super::RollbackGuard`] refuses to restore over content it did not
/// swap in: the guard holds where this lock cannot reach.
///
/// # What this lock does not cover
///
/// The critical section starts when the protocol is entered, not when the
/// caller decided what to write. An operation that reads the current vhost,
/// renders from it and then writes still has an unserialised gap between its
/// read and its call, so two operations targeting one domain can still finish
/// last-writer-wins. That is a narrower defect than the three above, and the
/// harm the audit names for it — a suspension undone by a certificate renewal
/// — is closed on the other axis, by `sites::is_suspended_vhost`, which every
/// writer of an enabled vhost now consults.
///
/// # Panics
///
/// `blocking_lock` panics when called from inside an asynchronous context,
/// which is tokio refusing to let a runtime worker be blocked. **That panic is
/// the enforcement of the requirement above, not a hazard to work around**: it
/// is a programming error rather than an input, it fires on the first call
/// with a message naming the cause, and it is not gated by `debug_assertions`,
/// so it holds in a release binary too. It does not fire on the blocking pool,
/// where blocking is the point.
pub(crate) fn config_tree_lock() -> MutexGuard<'static, ()> {
    CONFIG_WRITES.blocking_lock()
}

#[cfg(test)]
#[path = "../tests/safe_write/config_tree_lock_tests.rs"]
mod tests;
