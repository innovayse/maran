//! Tests for the per-request check that a fleet peer is still the pinned panel.
//!
//! The router is replaced by a recording stand-in, which is what makes "the router was never
//! called" an observable fact, and the peer is injected the way tonic injects it: as a value in
//! the request's extensions.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::convert::Infallible;
use std::sync::{Arc, Mutex};
use std::task::{Context, Poll};

use http::{Request, Response};
use rustls::pki_types::CertificateDer;
use tonic::body::Body;
use tonic::transport::server::UdsConnectInfo;
use tower::{Service, ServiceExt};

use super::{FleetAdmissionService, judge};
use crate::fleet::fixture_identity::{own_uid, private_directory};
use crate::fleet::panel_pin_file::PanelPinFile;
use crate::fleet::pin_slot::PinSlot;
use crate::fleet::pinned_panel_certificate::PinnedPanelCertificate;
use crate::fleet::test_certificates::{TestCertificate, valid_client_certificate};
use crate::peercred::{AdmittedFleetPeer, AgentPeer, FleetPeer};
use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;

/// A router stand-in: answers 200 and writes down, per call, whether the request carried the
/// admission marker.
#[derive(Clone, Default)]
struct RecordingRouter {
    /// One entry per call: did the request carry [`AdmittedFleetPeer`].
    calls: Arc<Mutex<Vec<bool>>>,
}

impl Service<Request<()>> for RecordingRouter {
    type Response = Response<Body>;
    type Error = Infallible;
    type Future = std::future::Ready<Result<Response<Body>, Infallible>>;

    fn poll_ready(&mut self, _cx: &mut Context<'_>) -> Poll<Result<(), Infallible>> {
        Poll::Ready(Ok(()))
    }

    fn call(&mut self, request: Request<()>) -> Self::Future {
        self.calls
            .lock()
            .unwrap()
            .push(request.extensions().get::<AdmittedFleetPeer>().is_some());
        std::future::ready(Ok(Response::new(Body::default())))
    }
}

/// A request that arrived on the fleet listener from a peer that presented `certificate`.
fn fleet_request(certificate: Option<&TestCertificate>) -> Request<()> {
    let chain = certificate
        .map(|certificate| Arc::new(vec![CertificateDer::from(certificate.der.clone())]));
    let mut request = Request::new(());
    request
        .extensions_mut()
        .insert(AgentPeer::Fleet(FleetPeer::new(None, chain)));
    request
}

/// The service over a recording router, pinning `pinned` (or nothing), and the pin slot.
fn service_pinning(
    pinned: Option<&TestCertificate>,
) -> (
    FleetAdmissionService<RecordingRouter>,
    RecordingRouter,
    Arc<PinSlot>,
) {
    let slot = Arc::new(match pinned {
        Some(certificate) => {
            PinSlot::holding(PinnedPanelCertificate::from_der(certificate.der.clone()).unwrap())
        }
        None => PinSlot::empty(),
    });
    let router = RecordingRouter::default();
    (
        FleetAdmissionService::new(router.clone(), slot.clone()),
        router,
        slot,
    )
}

/// The `grpc-status` of a refusal: 7 is PermissionDenied.
fn grpc_status(response: &Response<Body>) -> Option<String> {
    response
        .headers()
        .get("grpc-status")
        .map(|value| value.to_str().unwrap().to_owned())
}

#[tokio::test]
async fn the_pinned_panel_is_admitted_and_marked() {
    let panel = valid_client_certificate("panel");
    let (service, router, _slot) = service_pinning(Some(&panel));

    let response = service.oneshot(fleet_request(Some(&panel))).await.unwrap();

    assert_eq!(
        grpc_status(&response),
        None,
        "an admitted request is not a refusal"
    );
    assert_eq!(*router.calls.lock().unwrap(), vec![true]);
}

#[tokio::test]
async fn a_different_certificate_is_refused_and_the_router_is_never_called() {
    let panel = valid_client_certificate("panel");
    let stranger = valid_client_certificate("panel");
    let (service, router, _slot) = service_pinning(Some(&panel));

    let response = service
        .oneshot(fleet_request(Some(&stranger)))
        .await
        .unwrap();

    assert_eq!(grpc_status(&response).as_deref(), Some("7"));
    assert!(router.calls.lock().unwrap().is_empty());
}

#[tokio::test]
async fn the_same_key_certified_again_is_refused() {
    let panel = valid_client_certificate("panel");
    let reissued = panel.reissued();
    let (service, router, _slot) = service_pinning(Some(&panel));

    let response = service
        .oneshot(fleet_request(Some(&reissued)))
        .await
        .unwrap();

    assert_eq!(grpc_status(&response).as_deref(), Some("7"));
    assert!(router.calls.lock().unwrap().is_empty());
}

#[tokio::test]
async fn a_connection_that_presented_no_certificate_is_refused() {
    let panel = valid_client_certificate("panel");
    let (service, router, _slot) = service_pinning(Some(&panel));

    let response = service.oneshot(fleet_request(None)).await.unwrap();

    assert_eq!(grpc_status(&response).as_deref(), Some("7"));
    assert!(router.calls.lock().unwrap().is_empty());
}

#[tokio::test]
async fn with_nothing_pinned_no_fleet_peer_is_admitted() {
    let panel = valid_client_certificate("panel");
    let (service, router, _slot) = service_pinning(None);

    let response = service.oneshot(fleet_request(Some(&panel))).await.unwrap();

    assert_eq!(grpc_status(&response).as_deref(), Some("7"));
    assert!(router.calls.lock().unwrap().is_empty());
}

#[tokio::test]
async fn a_pin_removed_after_the_connection_was_admitted_refuses_its_next_request() {
    let panel = valid_client_certificate("panel");
    let (mut service, router, slot) = service_pinning(Some(&panel));

    let first = service
        .ready()
        .await
        .unwrap()
        .call(fleet_request(Some(&panel)))
        .await
        .unwrap();
    assert_eq!(grpc_status(&first), None, "control: admitted while pinned");

    slot.replace(None);
    let second = service
        .ready()
        .await
        .unwrap()
        .call(fleet_request(Some(&panel)))
        .await
        .unwrap();

    assert_eq!(grpc_status(&second).as_deref(), Some("7"));
    assert_eq!(
        *router.calls.lock().unwrap(),
        vec![true],
        "only the first reached the router"
    );
}

#[tokio::test]
async fn a_unix_request_passes_through_unmarked_for_the_peer_guard_to_judge() {
    let panel = valid_client_certificate("panel");
    let (service, router, _slot) = service_pinning(Some(&panel));
    let mut request = Request::new(());
    request
        .extensions_mut()
        .insert(AgentPeer::Unix(UdsConnectInfo {
            peer_addr: None,
            peer_cred: None,
        }));

    let response = service.oneshot(request).await.unwrap();

    assert_eq!(grpc_status(&response), None);
    assert_eq!(*router.calls.lock().unwrap(), vec![false]);
}

#[tokio::test]
async fn a_request_with_no_peer_at_all_is_passed_on_unmarked() {
    let (service, router, _slot) = service_pinning(None);

    let response = service.oneshot(Request::new(())).await.unwrap();

    assert_eq!(grpc_status(&response), None);
    assert_eq!(*router.calls.lock().unwrap(), vec![false]);
}

#[test]
fn the_reason_for_each_refusal_is_the_one_that_happened() {
    use crate::fleet::fleet_admission_refusal::FleetAdmissionRefusal;

    let panel = valid_client_certificate("panel");
    let stranger = valid_client_certificate("panel");
    let pin = || Some(PinnedPanelCertificate::from_der(panel.der.clone()).unwrap());
    let fingerprint =
        |certificate: &TestCertificate| Some(CertificateFingerprint::of_der(&certificate.der));

    assert_eq!(judge(pin(), fingerprint(&panel)), Ok(()));
    assert_eq!(
        judge(pin(), fingerprint(&stranger)),
        Err(FleetAdmissionRefusal::CertificateNotPinned)
    );
    assert_eq!(
        judge(pin(), None),
        Err(FleetAdmissionRefusal::NoCertificate)
    );
    assert_eq!(
        judge(None, fingerprint(&panel)),
        Err(FleetAdmissionRefusal::NothingPinned)
    );
}

#[tokio::test]
async fn removing_panel_pem_refuses_the_very_next_request() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    let panel = valid_client_certificate("panel");
    std::fs::write(directory.join(AgentPaths::FLEET_PANEL_PIN_FILE), &panel.pem).unwrap();
    let router = RecordingRouter::default();
    let mut service = FleetAdmissionService::new(
        router.clone(),
        Arc::new(PanelPinFile::new(directory.clone(), own_uid())),
    );

    let first = service
        .ready()
        .await
        .unwrap()
        .call(fleet_request(Some(&panel)))
        .await
        .unwrap();
    assert_eq!(
        grpc_status(&first),
        None,
        "control: admitted while the file exists, with no refresh made by the test"
    );

    std::fs::remove_file(directory.join(AgentPaths::FLEET_PANEL_PIN_FILE)).unwrap();
    let second = service
        .ready()
        .await
        .unwrap()
        .call(fleet_request(Some(&panel)))
        .await
        .unwrap();

    assert_eq!(grpc_status(&second).as_deref(), Some("7"));
    assert_eq!(
        *router.calls.lock().unwrap(),
        vec![true],
        "only the first reached the router"
    );
}
