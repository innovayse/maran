//! Tests for gathering and binding the fleet listener.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;

use maran_agent_core::validation::fleet::listen_address::ListenAddress;
use tokio::net::TcpListener;

use super::FleetListener;
use crate::fleet::fleet_limits::FleetLimits;
use crate::fleet::fleet_listener_error::FleetListenerError;
use crate::fleet::node_tls_identity::NodeTlsIdentity;
use crate::fleet::pin_slot::PinSlot;
use crate::fleet::test_certificates::{valid_client_certificate, valid_server_certificate};

/// An identity for a test node.
fn identity() -> NodeTlsIdentity {
    let node = valid_server_certificate();
    NodeTlsIdentity::from_pem(&node.pem, &node.key_pem()).unwrap()
}

#[tokio::test]
async fn a_listener_over_a_bound_socket_reports_that_sockets_address() {
    let socket = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let expected = socket.local_addr().unwrap();

    let bound = FleetListener::on_socket(
        socket,
        identity(),
        Arc::new(PinSlot::empty()),
        FleetLimits::standard(),
    )
    .bind()
    .await
    .unwrap();

    assert_eq!(bound.local_address().unwrap(), expected);
}

#[tokio::test]
async fn an_address_that_is_already_in_use_cannot_be_bound() {
    let taken = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let port = taken.local_addr().unwrap().port();
    let address = ListenAddress::parse(&format!("127.0.0.1:{port}")).unwrap();

    let outcome = FleetListener::on_address(
        address,
        identity(),
        Arc::new(PinSlot::empty()),
        FleetLimits::standard(),
    )
    .bind()
    .await;

    assert!(matches!(outcome, Err(FleetListenerError::Bind { .. })));
}

#[tokio::test]
async fn a_free_address_is_bound_by_the_listener_itself() {
    let probe = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let port = probe.local_addr().unwrap().port();
    drop(probe);
    let address = ListenAddress::parse(&format!("127.0.0.1:{port}")).unwrap();

    let bound = FleetListener::on_address(
        address,
        identity(),
        Arc::new(PinSlot::empty()),
        FleetLimits::standard(),
    )
    .bind()
    .await
    .unwrap();

    assert_eq!(bound.local_address().unwrap().port(), port);
}

#[tokio::test]
async fn an_identity_whose_key_is_not_its_certificates_is_refused_before_anything_is_bound() {
    let node = valid_server_certificate();
    let other = valid_client_certificate("someone else");
    let mismatched = NodeTlsIdentity::from_pem(&node.pem, &other.key_pem()).unwrap();
    let socket = TcpListener::bind("127.0.0.1:0").await.unwrap();

    let outcome = FleetListener::on_socket(
        socket,
        mismatched,
        Arc::new(PinSlot::empty()),
        FleetLimits::standard(),
    )
    .bind()
    .await;

    assert!(matches!(outcome, Err(FleetListenerError::Identity(_))));
}
