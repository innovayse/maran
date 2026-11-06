//! The tower layer that puts the audit service in front of the router.

use tower::Layer;

use super::audit_service::AuditService;

/// Installed once with `Server::builder().layer(AuditLayer)`, so it wraps the whole
/// router and runs BEFORE each service's `PeerGuard`: a peer the guard refuses still
/// leaves a line for a mutating path (threat note, section 7). It authorizes nothing
/// and forwards every request unchanged.
#[derive(Debug, Clone, Copy, Default)]
pub struct AuditLayer;

impl<S> Layer<S> for AuditLayer {
    type Service = AuditService<S>;

    /// Wraps the router in the audit service.
    fn layer(&self, inner: S) -> Self::Service {
        AuditService::new(inner)
    }
}
