//! Tests for what a fleet connection reports about its peer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::SocketAddr;
use std::sync::Arc;

use rustls::pki_types::CertificateDer;

use super::FleetPeer;

#[test]
fn the_leaf_is_the_first_certificate_of_the_chain() {
    let chain = Arc::new(vec![
        CertificateDer::from(vec![1_u8, 2, 3]),
        CertificateDer::from(vec![4_u8, 5, 6]),
    ]);

    let peer = FleetPeer::new(None, Some(chain));

    assert_eq!(peer.leaf_certificate().unwrap().as_ref(), &[1_u8, 2, 3]);
}

#[test]
fn a_peer_that_presented_nothing_has_no_leaf() {
    assert!(FleetPeer::new(None, None).leaf_certificate().is_none());
    assert!(
        FleetPeer::new(None, Some(Arc::new(Vec::new())))
            .leaf_certificate()
            .is_none()
    );
}

#[test]
fn the_address_is_reported_as_given() {
    let address: SocketAddr = "192.0.2.7:51000".parse().unwrap();

    assert_eq!(
        FleetPeer::new(Some(address), None).remote_address(),
        Some(address)
    );
    assert_eq!(FleetPeer::new(None, None).remote_address(), None);
}
