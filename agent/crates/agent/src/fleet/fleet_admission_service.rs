//! The per-request check that a fleet peer is still the pinned panel.

use std::future::Future;
use std::pin::Pin;
use std::sync::Arc;
use std::task::{Context, Poll};

use http::{Request, Response};
use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;
use tonic::Status;
use tonic::body::Body;
use tower::Service;

use super::fleet_admission_refusal::FleetAdmissionRefusal;
use super::pin_source::PinSource;
use super::pinned_panel_certificate::PinnedPanelCertificate;
use crate::peercred::{AdmittedFleetPeer, AgentPeer};

/// What a refused fleet peer is told. It names no reason: whether a pin exists, or which
/// certificate was expected, is nothing a stranger should be able to read from an error.
const REFUSAL_MESSAGE: &str = "fleet peer not admitted";

/// Checks every request that arrived on the fleet listener against the pin as it is NOW, and
/// marks the ones that pass with [`AdmittedFleetPeer`]; requests from the unix socket are
/// forwarded untouched, left to `PeerGuard`.
///
/// This is the second check behind the TLS verifier and it is load-bearing, not decoration: the
/// verifier runs once per handshake, and a connection outlives the pin that admitted it. A
/// panel certificate replaced or removed while a session is open is refused here on the next
/// request. It compares the SHA-256 fingerprint of the certificate the connection presented
/// with the pinned one, in constant time, and it does not look at the request at all: the
/// decision is about the peer.
///
/// What it does not do is re-check the certificate's validity window: a session admitted before
/// the certificate expired is admitted until it closes. The verifier refuses the next handshake.
#[derive(Debug, Clone)]
pub struct FleetAdmissionService<S> {
    /// The router being wrapped.
    inner: S,
    /// Where the pin is read from, refreshed on every request.
    pin_source: Arc<dyn PinSource>,
}

impl<S> FleetAdmissionService<S> {
    /// Wraps `inner`, admitting what `pin_source` pins.
    #[must_use]
    pub fn new(inner: S, pin_source: Arc<dyn PinSource>) -> Self {
        Self { inner, pin_source }
    }
}

/// Whether the certificate a peer presented is the one pinned: `pin` is the pin as it is now and
/// `presented` the fingerprint of the peer's certificate, or `None` when it sent none.
fn judge(
    pin: Option<PinnedPanelCertificate>,
    presented: Option<CertificateFingerprint>,
) -> Result<(), FleetAdmissionRefusal> {
    let pin = pin.ok_or(FleetAdmissionRefusal::NothingPinned)?;
    let presented = presented.ok_or(FleetAdmissionRefusal::NoCertificate)?;

    if presented == *pin.fingerprint() {
        Ok(())
    } else {
        Err(FleetAdmissionRefusal::CertificateNotPinned)
    }
}

impl<S, ReqBody> Service<Request<ReqBody>> for FleetAdmissionService<S>
where
    S: Service<Request<ReqBody>, Response = Response<Body>> + Clone + Send + 'static,
    S::Future: Send + 'static,
    S::Error: Send + 'static,
    ReqBody: Send + 'static,
{
    type Response = Response<Body>;
    type Error = S::Error;
    type Future = Pin<Box<dyn Future<Output = Result<Self::Response, Self::Error>> + Send>>;

    /// Readiness is the inner service's.
    fn poll_ready(&mut self, cx: &mut Context<'_>) -> Poll<Result<(), Self::Error>> {
        self.inner.poll_ready(cx)
    }

    /// Judges a fleet request before the router sees it: the pin is refreshed first (off the
    /// async workers), so a pin removed since the last request is gone from this one. A pass
    /// leaves the marker; a refusal is answered here with `PermissionDenied` and the router is
    /// never called.
    fn call(&mut self, mut request: Request<ReqBody>) -> Self::Future {
        // What the check needs from the peer is taken now, while the request is still in hand:
        // who it is, and the fingerprint of the certificate it presented.
        let fleet_peer = match request.extensions().get::<AgentPeer>() {
            Some(AgentPeer::Fleet(peer)) => Some((
                peer.remote_address(),
                peer.leaf_certificate()
                    .map(|presented| CertificateFingerprint::of_der(presented.as_ref())),
            )),
            _ => None,
        };

        // The standard tower swap: the clone is the one that was polled ready.
        let clone = self.inner.clone();
        let mut inner = std::mem::replace(&mut self.inner, clone);
        let pin_source = Arc::clone(&self.pin_source);

        Box::pin(async move {
            if let Some((address, presented)) = fleet_peer {
                pin_source.refresh().await;
                match judge(pin_source.current(), presented) {
                    Ok(()) => {
                        request.extensions_mut().insert(AdmittedFleetPeer::new());
                    }
                    Err(refusal) => {
                        tracing::warn!(
                            peer = ?address,
                            reason = %refusal,
                            "a fleet request was refused"
                        );
                        return Ok(Status::permission_denied(REFUSAL_MESSAGE).into_http::<Body>());
                    }
                }
            }

            inner.call(request).await
        })
    }
}

#[cfg(test)]
#[path = "../tests/fleet/fleet_admission_service_tests.rs"]
mod tests;
