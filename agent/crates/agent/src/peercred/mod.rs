//! Socket peer checks: who is allowed to talk to the agent at all.
//!
//! Authorisation starts below the RPC layer. The agent runs as root and its
//! command set is the whole attack surface of the panel, so a caller that is not
//! the panel user never reaches a handler, whatever it asks for.

pub mod peer_guard;
pub mod peer_policy;

pub use peer_guard::PeerGuard;
pub use peer_policy::PeerPolicy;
