//! The stream of connections the server accepts, from both listeners.

use std::io;

use tokio::net::UnixListener;
use tokio::sync::mpsc;
use tokio_stream::wrappers::{ReceiverStream, UnixListenerStream};
use tokio_stream::{Stream, StreamExt};

use super::bound_fleet_listener::BoundFleetListener;
use crate::peercred::AgentConnection;

/// Merges the unix socket's connections with the fleet listener's, if there is one.
///
/// With no fleet listener the second source is a channel whose sender is dropped at once, so it
/// ends immediately and the merged stream is the unix socket alone. One stream type either way
/// is what lets the router be built once, the same way, whether fleet mode is on or off.
///
/// The unix side keeps the error behaviour it always had: an `accept` error on the socket is the
/// server's to handle. The fleet side yields only sessions that completed the mutual handshake;
/// its own errors are logged and absorbed by the accept loop and never stop the server.
pub fn agent_incoming(
    unix: UnixListener,
    fleet: Option<BoundFleetListener>,
) -> impl Stream<Item = io::Result<AgentConnection>> {
    let unix_connections =
        UnixListenerStream::new(unix).map(|connection| connection.map(AgentConnection::Unix));

    let fleet_connections = match fleet {
        Some(listener) => listener.accept_connections(),
        None => ReceiverStream::new(mpsc::channel(1).1),
    }
    .map(Ok::<_, io::Error>);

    unix_connections.merge(fleet_connections)
}

#[cfg(test)]
#[path = "../tests/fleet/agent_incoming_tests.rs"]
mod tests;
