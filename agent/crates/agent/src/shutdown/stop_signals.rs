//! The handlers for the two signals that mean "stop", installed on construction.

use tokio::signal::unix::{Signal, SignalKind, signal};

/// Name logged for a stop asked for by `SIGTERM`.
const TERMINATE_NAME: &str = "SIGTERM";

/// Name logged for a stop asked for by `SIGINT`.
const INTERRUPT_NAME: &str = "SIGINT";

/// The `SIGTERM` and `SIGINT` handlers, replacing the kernel's default action
/// from the moment [`StopSignals::install`] returns.
///
/// This is a TYPE and not a bare `async fn` because of when the handlers are
/// installed. A `signal()` call inside an async body runs when that body is
/// first polled, and the daemon's shutdown future is not polled until the
/// server it was handed to is — which is after `UnixListener::bind`. That left a
/// window in which the socket existed, the process had printed `agent
/// listening`, and `SIGTERM` still took its DEFAULT action: instant death,
/// exit by signal, and the socket file left on disk for the panel to dial.
/// Measured on this branch at 24 concurrent CPU-bound processes on a 12-core
/// host: 11 of 60 runs of
/// `a_running_agent_exits_cleanly_on_sigterm_and_takes_its_socket_with_it` died
/// that way, 25–33 ms after the socket appeared, and a production-shaped probe
/// of the shipped binary lost the socket file in every one of the 5 of 40 runs
/// that died by signal. Splitting installation from awaiting is what closes it:
/// the caller installs before it binds, and awaits afterwards.
///
/// Both signals, because the daemon is stopped two ways and the difference must
/// not change the outcome: systemd sends `SIGTERM` (`KillSignal=` defaults to
/// it), and a developer running the binary in a terminal sends `SIGINT`. A
/// handler for only one of them would leave the other on the kernel's default
/// action — and it would be the developer's path that kept it, so nobody would
/// notice.
///
/// `SIGHUP` is deliberately NOT handled. It conventionally means "reload your
/// configuration", the agent has none to reload, and treating it as a stop would
/// make a disconnecting terminal shut down the root daemon.
pub struct StopSignals {
    /// The `SIGTERM` and `SIGINT` streams, in that order, or `None` when either
    /// handler could not be registered — see [`StopSignals::install`] for why a
    /// failure is not an exit, and [`StopSignals::stopped`] for what it means
    /// for the wait.
    installed: Option<(Signal, Signal)>,
}

impl StopSignals {
    /// Installs both handlers now.
    ///
    /// Call this BEFORE anything an operator or the panel can observe — before
    /// the socket is bound above all. Between the bind and the first poll of the
    /// returned wait there is no window: the handlers are already in place, so a
    /// signal arriving in it is recorded and delivered to
    /// [`StopSignals::stopped`] rather than killing the process.
    ///
    /// What it still does not cover, stated rather than implied: a signal
    /// arriving before this call — during distro detection or the restore
    /// reconciliation that runs ahead of it — keeps the default action. Nothing
    /// outside this process can be waiting on the daemon at that point, because
    /// no socket exists yet.
    ///
    /// A stream that cannot be registered is reported and the wait then never
    /// completes, rather than being turned into an exit: failing to install a
    /// handler is not a reason to refuse to serve, and the process still dies on
    /// the signal's default action, which is where it was before this module
    /// existed.
    ///
    /// # Panics
    ///
    /// Never. `signal` is fallible and every failure is handled above.
    #[must_use]
    pub fn install() -> Self {
        let terminate = match signal(SignalKind::terminate()) {
            Ok(stream) => stream,
            Err(error) => {
                tracing::error!(%error, "cannot handle SIGTERM; the daemon will stop abruptly");
                return Self { installed: None };
            }
        };
        let interrupt = match signal(SignalKind::interrupt()) {
            Ok(stream) => stream,
            Err(error) => {
                tracing::error!(%error, "cannot handle SIGINT; the daemon will stop abruptly");
                return Self { installed: None };
            }
        };

        Self {
            installed: Some((terminate, interrupt)),
        }
    }

    /// Completes on the first `SIGTERM` or `SIGINT` to arrive since
    /// [`StopSignals::install`], and never otherwise.
    ///
    /// "Since install" is the whole point: a signal delivered before this future
    /// is first polled is already held by the registered stream, so it completes
    /// immediately instead of being missed.
    pub async fn stopped(self) {
        let Some((mut terminate, mut interrupt)) = self.installed else {
            return std::future::pending().await;
        };

        let signal_name = tokio::select! {
            _ = terminate.recv() => TERMINATE_NAME,
            _ = interrupt.recv() => INTERRUPT_NAME,
        };

        tracing::info!(
            signal = signal_name,
            "stopping: no new work will be accepted"
        );
    }
}
