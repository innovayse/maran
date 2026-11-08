//! Tests for the guard that every service carries.
//!
//! A unix peer is judged by the uid the kernel reports, so the admitted case uses a real
//! socket pair: the credentials in it are this process's own, read by the kernel.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use tonic::service::Interceptor;
use tonic::transport::server::UdsConnectInfo;
use tonic::{Code, Request};

use super::PeerGuard;
use crate::peercred::{AdmittedFleetPeer, AgentPeer, FleetPeer, PeerPolicy};

/// A request whose connection is a unix socket pair, as tonic would describe it.
async fn unix_request() -> (Request<()>, u32) {
    let (stream, _other_end) = tokio::net::UnixStream::pair().unwrap();
    let credentials = stream.peer_cred().unwrap();
    let uid = credentials.uid();
    let mut request = Request::new(());
    request
        .extensions_mut()
        .insert(AgentPeer::Unix(UdsConnectInfo {
            peer_addr: None,
            peer_cred: Some(credentials),
        }));
    (request, uid)
}

fn fleet_request(admitted: bool) -> Request<()> {
    let mut request = Request::new(());
    request
        .extensions_mut()
        .insert(AgentPeer::Fleet(FleetPeer::new(None, None)));
    if admitted {
        request.extensions_mut().insert(AdmittedFleetPeer::new());
    }
    request
}

#[tokio::test]
async fn a_unix_peer_with_the_allowed_uid_is_admitted() {
    let (request, uid) = unix_request().await;

    let outcome = PeerGuard::new(PeerPolicy::new(uid)).call(request);

    assert!(outcome.is_ok());
}

#[tokio::test]
async fn a_unix_peer_with_another_uid_is_refused() {
    let (request, uid) = unix_request().await;

    let outcome = PeerGuard::new(PeerPolicy::new(uid.wrapping_add(1))).call(request);

    assert_eq!(outcome.unwrap_err().code(), Code::PermissionDenied);
}

#[test]
fn a_unix_peer_whose_credentials_are_unavailable_is_refused() {
    let mut request = Request::new(());
    request
        .extensions_mut()
        .insert(AgentPeer::Unix(UdsConnectInfo {
            peer_addr: None,
            peer_cred: None,
        }));

    let outcome = PeerGuard::new(PeerPolicy::new(0)).call(request);

    assert_eq!(outcome.unwrap_err().code(), Code::PermissionDenied);
}

#[test]
fn a_fleet_peer_the_admission_service_marked_is_admitted() {
    let outcome = PeerGuard::new(PeerPolicy::new(0)).call(fleet_request(true));

    assert!(outcome.is_ok());
}

#[test]
fn a_fleet_peer_without_the_admission_mark_is_refused() {
    let outcome = PeerGuard::new(PeerPolicy::new(0)).call(fleet_request(false));

    assert_eq!(outcome.unwrap_err().code(), Code::PermissionDenied);
}

#[test]
fn a_request_that_says_nothing_about_its_peer_is_refused() {
    let outcome = PeerGuard::new(PeerPolicy::new(0)).call(Request::new(()));

    assert_eq!(outcome.unwrap_err().code(), Code::PermissionDenied);
}

#[test]
fn the_admission_mark_alone_does_not_admit_a_unix_peer() {
    let mut request = Request::new(());
    request
        .extensions_mut()
        .insert(AgentPeer::Unix(UdsConnectInfo {
            peer_addr: None,
            peer_cred: None,
        }));
    request.extensions_mut().insert(AdmittedFleetPeer::new());

    let outcome = PeerGuard::new(PeerPolicy::new(0)).call(request);

    assert_eq!(outcome.unwrap_err().code(), Code::PermissionDenied);
}
