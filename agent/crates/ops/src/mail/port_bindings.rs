//! Reducing the kernel's listening sockets to the state of each mail port.

use crate::mail::model::listening_socket::ListeningSocket;
use crate::mail::model::mail_ports::MailPorts;
use crate::mail::model::port_binding::PortBinding;
use crate::mail::model::port_state::PortState;

/// The binding of each of the seven mail ports, in [`MailPorts::ALL`] order.
///
/// A port with one loopback socket and one public socket is [`PortBinding::Public`]:
/// the question is whether the world can reach it. Sockets on ports outside the
/// seven are ignored.
#[must_use]
pub fn port_bindings(sockets: &[ListeningSocket]) -> Vec<PortState> {
    MailPorts::ALL
        .iter()
        .map(|&port| {
            let mut on_port = sockets
                .iter()
                .filter(|socket| socket.port == port)
                .peekable();
            let binding = if on_port.peek().is_none() {
                PortBinding::NotBound
            } else if on_port.any(|socket| !socket.loopback_only) {
                PortBinding::Public
            } else {
                PortBinding::LoopbackOnly
            };
            PortState { port, binding }
        })
        .collect()
}

#[cfg(test)]
#[path = "../tests/mail/port_bindings_tests.rs"]
mod tests;
