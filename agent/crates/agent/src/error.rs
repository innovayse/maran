//! Fatal startup failures of the agent process.

/// Why the agent could not start serving.
///
/// Every variant is fatal: the agent either owns its socket and knows its host,
/// or it exits. A half-started root daemon is worse than an absent one, because
/// the panel would report it as reachable.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum StartupError {
    /// The socket, or the directory holding it, could not be prepared.
    #[error("cannot prepare the agent socket: {0}")]
    Socket(#[from] std::io::Error),
    /// The host distribution is unsupported or undetectable.
    #[error("cannot detect the host distribution: {0}")]
    Distro(#[from] maran_distro::DetectError),
    /// The fleet listener (issue #180) could not be set up: its address cannot be bound or its
    /// TLS identity is unusable. Only possible when fleet mode is configured.
    #[error("cannot start the fleet listener: {0}")]
    Fleet(#[from] crate::fleet::fleet_listener_error::FleetListenerError),
    /// The gRPC server stopped with an error.
    #[error("the agent server failed: {0}")]
    Server(#[from] tonic::transport::Error),
}
