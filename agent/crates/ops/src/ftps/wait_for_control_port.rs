//! Waiting, for a bounded time, for a freshly restarted FTPS daemon to answer.

use std::time::Duration;

use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::get_ftps_status::{CONTROL_PORT, GREETING};

/// How long the wait pauses between two probes of the control port.
///
/// Measured: vsftpd binds its control port 33 to 74 ms after `systemctl restart`
/// returns, so one 100 ms step covers the usual case in one pause while a
/// daemon that is merely slow is still noticed within a tenth of a second.
const STEP: Duration = Duration::from_millis(100);

/// The longest the wait lasts before the daemon is declared not listening.
///
/// Two orders of magnitude over the measured bind time, and deliberately no
/// more: the whole enable runs inside the panel's 30 s operation timeout, which
/// also has to hold the candidate run and a possible rollback restart.
const DEADLINE: Duration = Duration::from_secs(10);

/// Waits until the control port answers with a `220` greeting, or the deadline
/// passes.
///
/// The unit is `Type=simple`, so a restart returns once the process is forked
/// and says nothing about whether the socket is bound yet. A single probe right
/// after it therefore reports a healthy daemon as silent. This probes first,
/// then pauses [`STEP`] and probes again until [`DEADLINE`] has elapsed on the
/// host's monotonic clock.
///
/// The deadline is checked against the clock and not against a number of
/// pauses, because one probe can itself take seconds (a daemon that accepts and
/// says nothing) and a count would not bound that. A deadline that has already
/// passed still costs exactly one probe.
///
/// Returns `true` when a line beginning with `220` was read, and `false` when
/// the deadline passed first. It never errors: "nothing answered" is the one
/// fact the callers act on, and each already owns the error that fact becomes.
pub(crate) fn wait_for_control_port(host: &dyn FtpsHost) -> bool {
    let started = host.monotonic_now();
    loop {
        let answered = host
            .control_port_greeting(CONTROL_PORT)
            .is_some_and(|line| line.starts_with(GREETING));
        if answered {
            return true;
        }
        if host.monotonic_now().saturating_sub(started) >= DEADLINE {
            return false;
        }
        host.pause(STEP);
    }
}

#[cfg(test)]
#[path = "../tests/ftps/wait_for_control_port_tests.rs"]
mod tests;
