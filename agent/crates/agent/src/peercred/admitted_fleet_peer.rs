//! The mark a request carries once its fleet peer has been checked.

/// Left in a request's extensions by [`crate::fleet::fleet_admission_service::FleetAdmissionService`]
/// when the certificate the request's connection presented is the pinned panel certificate.
///
/// Request extensions are filled only by code inside this process (tonic puts the connection's
/// peer there, a layer may add to it); nothing a client sends can create one, which is what
/// lets [`super::peer_guard::PeerGuard`] treat the marker as proof. The constructor is
/// crate-visible because the service that mints it lives in another module, and the doc of
/// that service is the statement of who may call it: nothing else constructs one outside tests.
#[derive(Debug, Clone, Copy)]
pub struct AdmittedFleetPeer(());

impl AdmittedFleetPeer {
    /// The marker. Called by the admission service after the pin comparison, and by nothing else.
    #[must_use]
    pub(crate) fn new() -> Self {
        Self(())
    }
}
