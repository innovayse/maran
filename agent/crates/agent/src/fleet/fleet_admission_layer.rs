//! The tower layer that puts the admission service in front of the router.

use std::sync::Arc;

use tower::Layer;

use super::fleet_admission_service::FleetAdmissionService;
use super::pin_slot::PinSlot;
use super::pin_source::PinSource;

/// Installed once on the server builder, inside the audit layer and outside every service's
/// `PeerGuard`.
///
/// Being a layer over the whole router and not an interceptor per service is deliberate: the
/// check runs for every request of every service, including one registered tomorrow, and cannot
/// be forgotten on a single service the way a per-service interceptor could. The audit layer
/// stays outside it, so a refused fleet peer still leaves its line.
#[derive(Debug, Clone)]
pub struct FleetAdmissionLayer {
    /// Where the pinned panel certificate is read from, on every request.
    pin_source: Arc<dyn PinSource>,
}

impl FleetAdmissionLayer {
    /// A layer admitting the fleet peers `pin_source` currently pins.
    #[must_use]
    pub fn new(pin_source: Arc<dyn PinSource>) -> Self {
        Self { pin_source }
    }

    /// The layer a daemon without a fleet listener installs: it pins nothing, so a request
    /// carrying fleet peer information, which cannot occur without a listener, would be
    /// refused. It keeps the router's type the same whether fleet mode is on or off.
    #[must_use]
    pub fn refusing_every_fleet_peer() -> Self {
        Self::new(Arc::new(PinSlot::empty()))
    }
}

impl<S> Layer<S> for FleetAdmissionLayer {
    type Service = FleetAdmissionService<S>;

    /// Wraps the router in the admission service.
    fn layer(&self, inner: S) -> Self::Service {
        FleetAdmissionService::new(inner, Arc::clone(&self.pin_source))
    }
}
