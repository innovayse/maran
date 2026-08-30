//! Enforcement of [`super::peer_policy::PeerPolicy`] on every incoming request.

use tonic::transport::server::UdsConnectInfo;
use tonic::{Request, Status, service::Interceptor};

use super::peer_policy::PeerPolicy;

/// Enforces [`PeerPolicy`] on every request before it reaches a service.
#[derive(Debug, Clone, Copy)]
pub struct PeerGuard {
    /// The policy this guard enforces.
    policy: PeerPolicy,
}

impl PeerGuard {
    /// Wraps `policy` as a tonic interceptor.
    #[must_use]
    pub fn new(policy: PeerPolicy) -> Self {
        Self { policy }
    }
}

impl Interceptor for PeerGuard {
    /// Reads the peer's credentials from the connection and applies the policy.
    ///
    /// The uid comes from `SO_PEERCRED`, which the kernel fills in at connect
    /// time: it cannot be set by the caller, unlike anything carried in the
    /// request itself. Credentials being absent is treated as a denial rather
    /// than as a reason to fall back to something weaker.
    fn call(&mut self, request: Request<()>) -> Result<Request<()>, Status> {
        let peer_uid = request
            .extensions()
            .get::<UdsConnectInfo>()
            .and_then(|info| info.peer_cred)
            .map(|credentials| credentials.uid());

        match peer_uid {
            Some(uid) if self.policy.permits(uid) => Ok(request),
            Some(uid) => Err(Status::permission_denied(format!(
                "uid {uid} is not permitted"
            ))),
            None => Err(Status::permission_denied("peer credentials unavailable")),
        }
    }
}
