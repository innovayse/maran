//! Tests for the TLS configuration of the fleet listener.
//!
//! What a TLS 1.2 client or a client without a certificate meets is proved on the wire, in
//! `tests/fleet_listener.rs`; these pin what the configuration itself says.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;

use super::fleet_server_config;
use crate::fleet::fleet_listener_error::FleetListenerError;
use crate::fleet::node_tls_identity::NodeTlsIdentity;
use crate::fleet::pin_slot::PinSlot;
use crate::fleet::test_certificates::{valid_client_certificate, valid_server_certificate};

#[test]
fn a_matching_certificate_and_key_make_a_configuration() {
    let node = valid_server_certificate();
    let identity = NodeTlsIdentity::from_pem(&node.pem, &node.key_pem()).unwrap();

    let config = fleet_server_config(identity, Arc::new(PinSlot::empty()));

    assert!(config.is_ok());
}

#[test]
fn the_configuration_offers_h2_only_and_never_resumes_a_session() {
    let node = valid_server_certificate();
    let identity = NodeTlsIdentity::from_pem(&node.pem, &node.key_pem()).unwrap();

    let config = fleet_server_config(identity, Arc::new(PinSlot::empty())).unwrap();

    assert_eq!(config.alpn_protocols, vec![b"h2".to_vec()]);
    assert_eq!(config.send_tls13_tickets, 0, "no tickets are issued");
    assert_eq!(config.max_early_data_size, 0, "no 0-RTT data is accepted");
    assert!(
        !config.session_storage.can_cache(),
        "the server-side session cache must be the one that stores nothing"
    );
}

#[test]
fn a_key_that_is_not_the_certificates_is_refused() {
    let node = valid_server_certificate();
    let other = valid_client_certificate("someone else");
    let identity = NodeTlsIdentity::from_pem(&node.pem, &other.key_pem()).unwrap();

    let config = fleet_server_config(identity, Arc::new(PinSlot::empty()));

    assert!(matches!(config, Err(FleetListenerError::Identity(_))));
}

#[test]
fn text_that_holds_no_certificate_or_no_key_is_refused_as_pem() {
    let node = valid_server_certificate();

    assert!(matches!(
        NodeTlsIdentity::from_pem("not pem", &node.key_pem()),
        Err(FleetListenerError::Pem(_))
    ));
    assert!(matches!(
        NodeTlsIdentity::from_pem(&node.pem, "not pem"),
        Err(FleetListenerError::Pem(_))
    ));
}

#[test]
fn the_identity_prints_without_its_key() {
    let node = valid_server_certificate();
    let identity = NodeTlsIdentity::from_pem(&node.pem, &node.key_pem()).unwrap();

    let printed = format!("{identity:?}");

    assert!(printed.contains("redacted"));
    assert!(!printed.contains("PrivateKey"), "got {printed}");
}
