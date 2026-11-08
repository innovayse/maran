//! Waiting, for a bounded time, for freshly started daemons to bind their ports.

use std::time::Duration;

use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::port_binding::PortBinding;
use crate::mail::port_bindings::port_bindings;

/// How long the wait pauses between two reads of the socket table.
const STEP: Duration = Duration::from_millis(250);

/// The longest the wait lasts before a port is declared not listening.
///
/// The daemons bind within a second of the service manager returning; the
/// deadline is well above that and well inside the panel's operation timeout,
/// which also has to hold five restarts and a possible rollback.
const DEADLINE: Duration = Duration::from_secs(20);

/// Waits until every port in `expected` is bound to an address other hosts can
/// reach, or the deadline passes.
///
/// "Bound" means [`PortBinding::Public`]: a daemon that listens on loopback only
/// is not serving the internet, and reporting it as up would be the panel
/// agreeing with itself. The units are `Type=forking` or `notify` and return
/// before the sockets are bound, so a single read right after the start reports a
/// healthy stack as silent.
///
/// The deadline is checked against the host's monotonic clock and not against a
/// count of pauses, so one slow read cannot stretch it. A deadline already past
/// still costs exactly one read.
///
/// # Errors
///
/// Returns [`MailError::NotListening`] naming the first expected port still
/// unbound at the deadline, and [`MailError::SocketsUnreadable`] when the
/// kernel's table cannot be read.
pub fn wait_for_mail_ports(host: &dyn MailHost, expected: &[u16]) -> Result<(), MailError> {
    let started = host.monotonic_now();
    loop {
        let bindings = port_bindings(&host.listening_sockets()?);
        let missing = expected.iter().copied().find(|port| {
            bindings
                .iter()
                .find(|state| state.port == *port)
                .is_none_or(|state| state.binding != PortBinding::Public)
        });
        let Some(port) = missing else {
            return Ok(());
        };
        if host.monotonic_now().saturating_sub(started) >= DEADLINE {
            return Err(MailError::NotListening { port });
        }
        host.pause(STEP);
    }
}

#[cfg(test)]
#[path = "../tests/mail/wait_for_mail_ports_tests.rs"]
mod tests;
