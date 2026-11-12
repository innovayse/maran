//! The loop that runs the check at the start and then at an interval.

use std::sync::{Arc, Mutex, PoisonError};
use std::time::Duration;

use crate::address_watch::host_address_watch::HostAddressWatch;

/// How often the host's addresses are read.
///
/// The check is two small procfs reads and a comparison; a layer is written only on
/// a change. A minute bounds how long an address the host just gained stays outside
/// the lockdown, and costs nothing on a host that does not change.
pub(crate) const CHECK_INTERVAL: Duration = Duration::from_secs(60);

/// Runs `watch` once now and then every [`CHECK_INTERVAL`], until the task is
/// dropped or aborted.
///
/// Each tick runs on the blocking pool (it spawns `nft` and `postfix` and takes the
/// firewall lock, whose `blocking_lock` refuses a runtime worker). A tick that
/// panics is logged and the loop goes on: this check is a repair of the lockdown,
/// and a panic in it must not take the daemon or the check down.
pub(crate) async fn run_host_address_watch(watch: HostAddressWatch) {
    let watch = Arc::new(Mutex::new(watch));
    let mut interval = tokio::time::interval(CHECK_INTERVAL);
    interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);

    loop {
        interval.tick().await;

        let watch = Arc::clone(&watch);
        let ticked = tokio::task::spawn_blocking(move || {
            // A poisoned lock means an earlier tick panicked mid-way; the state it
            // guards (what each layer was brought to) is only ever advanced after
            // a success, so it is safe to go on with.
            watch.lock().unwrap_or_else(PoisonError::into_inner).tick()
        })
        .await;
        if let Err(error) = ticked {
            tracing::error!(%error, "a check of the host's addresses panicked; trying again at the next interval");
        }
    }
}
