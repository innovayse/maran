//! The far end of an accepted connection, whichever transport it arrived on.

use tonic::transport::server::UdsConnectInfo;

use super::fleet_peer::FleetPeer;

/// What the agent knows about the peer of one connection.
///
/// tonic stores the connection's `Connected::ConnectInfo` in every request's extensions, and the
/// agent now serves two transports from one router, so there is one type that says which
/// transport a request came in on. The guard reads it to pick the check: the kernel-reported
/// uid for a unix peer, the admission of the pinned certificate for a fleet peer. A request
/// carrying neither is refused by [`super::peer_guard::PeerGuard`].
#[derive(Debug, Clone)]
pub enum AgentPeer {
    /// A process on this host, connected through the unix socket.
    Unix(UdsConnectInfo),
    /// A machine across the network, connected through the fleet listener.
    Fleet(FleetPeer),
}
