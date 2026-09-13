//! The bound on how long a stop waits for work already in flight.

use std::time::Duration;

use tokio::sync::oneshot::Receiver;

/// How long in-flight requests are given to finish once a stop has been asked
/// for.
///
/// Chosen against the operations, not as a round number. Every account, site,
/// SSL, PHP, cron and firewall operation is a handful of process spawns and
/// completes well inside this; those are the operations a panel upgrade's
/// `systemctl restart` actually interrupts, and the class this budget exists to
/// save — `create_account` is three steps (`useradd`, open the home to the web
/// server's group, `setquota`) and a kill between them leaves an account whose
/// sites answer 403 and which the agent deliberately refuses to repair.
///
/// It is emphatically NOT enough for a backup or a restore, which are measured
/// in minutes to hours, and no budget an operator would tolerate would be. That
/// is stated here rather than papered over with a bigger number: those two are
/// made survivable by how they are written — an artifact published by `rename`
/// after a full write, a staging tree the account cannot see — and by what the
/// NEXT start does about what a kill left behind.
///
/// That last clause used to be missing, and its absence was a defect in its own
/// right (privileges audit F-2). This comment said only that a restore between
/// its first `DROP DATABASE` and its last load "is not made safe by any
/// shutdown path, because a power cut does the same thing", and stopped there —
/// which was true of the databases and silently untrue of the HOME. A kill
/// between the two renames that swap an account's home left `/home/<account>`
/// absent with the customer's home parked under
/// `AgentPaths::RESTORE_STAGING_ROOT` and nothing anywhere to put it back. It is
/// recoverable, and it is now recovered: `ops::backup::recover_restores` runs
/// from `server::serve` before the socket is bound and finishes or reverses the
/// swap.
///
/// What is still NOT made safe by any shutdown path, unchanged: a database
/// estate left half-replaced. The pre-restore dumps now survive the restart —
/// the agent's unit used to delete them with `ExecStartPre=` — and where they
/// are is logged, but reloading them is an operator's decision and not a root
/// daemon's.
///
/// It must stay below the unit's `TimeoutStopSec=`, or systemd's own `SIGKILL`
/// arrives first and this whole path is decoration. That relationship is now
/// asserted rather than asked for: `the_drain_budget_stays_below_the_unit_files_stop_timeout`
/// reads the shipped unit file and fails if the two numbers ever cross.
///
/// # What expiring this budget does NOT do
///
/// It does not stop anything. Every unit of host work runs inside
/// `spawn_blocking`, and a dropped `JoinHandle` detaches such a task rather
/// than cancelling it — there is no tokio API that cancels one, which is why
/// three of the five streaming rpcs are documented as "run to completion"
/// (rules/rust.md, "Async and blocking"). Expiry abandons the REQUEST: the
/// panel's call goes unanswered and the socket is unlinked. The work continues,
/// and because dropping the runtime joins every blocking thread with no
/// timeout, the process cannot exit until it ends. Two properties of this file's
/// tests pin that, so the paragraph cannot quietly become false.
///
/// That last sentence holds only while `serve` returns `Ok` here. `main` turns a
/// `serve` error into `std::process::exit`, and `process::exit` runs NO
/// destructors: the runtime is never dropped, the blocking pool is never joined,
/// and the detached work dies mid-syscall — the abrupt death this module
/// replaced, reached through a different door one character wide. Measured with
/// a probe shaped like `serve`: the graceful route exited after 2.00 s having
/// let 2000 ms of blocking work finish, the `process::exit` route after 0.20 s
/// with the work abandoned. A third property,
/// `the_expiry_arm_returns_ok_rather_than_the_error_that_would_exit_abruptly`,
/// holds the door shut.
///
/// # What therefore actually kills work halfway, and what it costs
///
/// Not this budget: systemd's `SIGKILL` at `TimeoutStopSec=45`, with
/// `KillMode=control-group`, so the forked setuid children die with it. Only
/// work still running fifteen seconds after the budget reaches it — which is
/// every account, site, SSL, PHP, cron and firewall operation almost never, and
/// a backup, a restore or a PHP install almost always.
///
/// - A restore: the home swap is repaired by `ops::backup::recover_restores` at
///   the next start; the database estate is not, as the paragraph above says.
/// - A backup: the artifact is published by `rename` after a full write, so
///   nothing is corrupt — the artifact and the panel's row are orphaned, which
///   is the reclamation `services::backup::BackupServiceImpl::create_backup`'s
///   own doc records as unmet. The obligation is written down at the rpc, not at
///   `ops::backup::create_backup`, which says nothing about it.
/// - A config write: the sharpest of the short ones. `ops::safe_write` renames
///   new content over the live target BEFORE validating it, so a kill inside
///   that window leaves unvalidated content live with the rollback guard gone
///   and the running server still on the old config. Nothing at the next start
///   looks: `server::serve` runs exactly one reconciler, and it is for restores.
/// - A firewall apply: bounded by construction — `nft --check` runs on the
///   staged file, so the live path and the kernel are either both old or the
///   file is one step ahead, which the same operation retried converges on.
///
/// # The locks whose holder detached
///
/// The guard lives in the blocking closure, so the detached work KEEPS its lock
/// for as long as it runs — correct, since no new work is being accepted — and
/// every lock vanishes with the process at the kill. They are process-local by
/// design (rules/rust.md), so nothing is leaked and the next start contends
/// with nobody; what is lost is the critical section's guarantee about whatever
/// half-applied state is now on disk. `recover_restores` may repair a home
/// without taking the account lock for one reason only: it runs before the
/// socket is bound, so no second writer can exist.
pub const DRAIN_BUDGET: Duration = Duration::from_secs(30);

/// Completes [`DRAIN_BUDGET`] after the stop signal fires, and never before it.
///
/// The clock starts at the SIGNAL, not at the call: a deadline started when the
/// server was built would fire on a daemon that has been up for a month and had
/// never been asked to stop.
///
/// A closed channel is treated as "the stop happened": the sender lives inside
/// the shutdown future handed to the server, so the only way it is dropped
/// without sending is that future being cancelled — which happens when the
/// server is going away regardless. Erring towards starting the deadline is the
/// safe direction; erring the other way would leave the daemon with no bound at
/// all, which is the failure this unit exists to prevent.
pub async fn drain_deadline(signalled: Receiver<()>) {
    let _ = signalled.await;
    tokio::time::sleep(DRAIN_BUDGET).await;
}

#[cfg(test)]
#[path = "../tests/shutdown/drain_deadline_tests.rs"]
mod tests;
