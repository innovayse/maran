//! Enforcement of [`super::peer_policy::PeerPolicy`] on every incoming request.

use tonic::{Request, Status, service::Interceptor};

use super::admitted_fleet_peer::AdmittedFleetPeer;
use super::agent_peer::AgentPeer;
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
    /// Reads who the peer is from the connection and applies the check its transport has.
    ///
    /// A unix peer is judged by the uid in `SO_PEERCRED`, which the kernel fills in at connect
    /// time: it cannot be set by the caller, unlike anything carried in the request itself.
    /// Credentials being absent is treated as a denial rather than as a reason to fall back to
    /// something weaker.
    ///
    /// A fleet peer has no uid. It is admitted only when the admission service in front of the
    /// router has compared the certificate it presented with the pinned panel certificate and
    /// left its marker on the request ([`AdmittedFleetPeer`]); a request from a fleet
    /// connection without the marker is refused here, so a router built without that service
    /// admits no network peer at all.
    ///
    /// A request that carries no peer information at all is refused.
    fn call(&mut self, request: Request<()>) -> Result<Request<()>, Status> {
        let verdict = match request.extensions().get::<AgentPeer>() {
            Some(AgentPeer::Unix(info)) => {
                match info.peer_cred.map(|credentials| credentials.uid()) {
                    Some(uid) if self.policy.permits(uid) => Ok(()),
                    Some(uid) => Err(Status::permission_denied(format!(
                        "uid {uid} is not permitted"
                    ))),
                    None => Err(Status::permission_denied("peer credentials unavailable")),
                }
            }
            Some(AgentPeer::Fleet(_)) => {
                if request.extensions().get::<AdmittedFleetPeer>().is_some() {
                    Ok(())
                } else {
                    Err(Status::permission_denied("fleet peer not admitted"))
                }
            }
            None => Err(Status::permission_denied("peer credentials unavailable")),
        };

        verdict.map(|()| request)
    }
}

#[cfg(test)]
#[path = "../tests/peercred/peer_guard_tests.rs"]
mod tests;
