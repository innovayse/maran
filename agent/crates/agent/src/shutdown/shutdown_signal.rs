//! The future that completes when the operator asks the daemon to stop.

use tokio::signal::unix::{SignalKind, signal};

/// Completes on the first `SIGTERM` or `SIGINT`, and never otherwise.
///
/// Both, because the daemon is stopped two ways and the difference must not
/// change the outcome: systemd sends `SIGTERM` (`KillSignal=` defaults to it),
/// and a developer running the binary in a terminal sends `SIGINT`. A handler
/// for only one of them would leave the other on the kernel's default action,
/// which is exactly the instant, mid-syscall death this module exists to end —
/// and it would be the developer's path that kept it, so nobody would notice.
///
/// `SIGHUP` is deliberately NOT handled. It conventionally means "reload your
/// configuration", the agent has none to reload, and treating it as a stop would
/// make a disconnecting terminal shut down the root daemon.
///
/// A signal stream that cannot be registered is reported and then awaited on
/// forever rather than turned into an exit: failing to install a handler is not
/// a reason to refuse to serve, and the process still dies on the signal's
/// default action, which is where it was before this module existed.
pub async fn shutdown_signal() {
    let mut terminate = match signal(SignalKind::terminate()) {
        Ok(stream) => stream,
        Err(error) => {
            tracing::error!(%error, "cannot handle SIGTERM; the daemon will stop abruptly");
            return std::future::pending().await;
        }
    };
    let mut interrupt = match signal(SignalKind::interrupt()) {
        Ok(stream) => stream,
        Err(error) => {
            tracing::error!(%error, "cannot handle SIGINT; the daemon will stop abruptly");
            return std::future::pending().await;
        }
    };

    let signal_name = tokio::select! {
        _ = terminate.recv() => "SIGTERM",
        _ = interrupt.recv() => "SIGINT",
    };

    tracing::info!(
        signal = signal_name,
        "stopping: no new work will be accepted"
    );
}
