//! Socket peer checks: who is allowed to talk to the agent at all.
//!
//! Authorisation starts below the RPC layer. The agent runs as root and its
//! command set is the whole attack surface of the panel, so a caller that is not
//! the panel user never reaches a handler, whatever it asks for.
//!
//! Two kinds of peer exist: a process on this host, identified by the uid the kernel reports,
//! and (only when fleet mode is configured, issue #180) a machine across the network,
//! identified by the certificate it proved the key of. [`agent_connection::AgentConnection`]
//! and [`agent_peer::AgentPeer`] carry which one a request came from.

pub mod admitted_fleet_peer;
pub mod agent_connection;
pub mod agent_peer;
pub mod fleet_peer;
pub mod peer_guard;
pub mod peer_policy;

pub use admitted_fleet_peer::AdmittedFleetPeer;
pub use agent_connection::AgentConnection;
pub use agent_peer::AgentPeer;
pub use fleet_peer::FleetPeer;
pub use peer_guard::PeerGuard;
pub use peer_policy::PeerPolicy;
