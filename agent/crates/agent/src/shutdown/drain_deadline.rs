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
/// after a full write, a staging tree the account cannot see — and the one part
/// that is not (a restore between its first `DROP DATABASE` and its last load)
/// is not made safe by any shutdown path, because a power cut does the same
/// thing.
///
/// It must stay below the unit's `TimeoutStopSec=`, or systemd's own `SIGKILL`
/// arrives first and this whole path is decoration.
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
