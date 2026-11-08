//! Reducing listening sockets to the state of each mail port.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::model::listening_socket::ListeningSocket;
use crate::mail::model::port_binding::PortBinding;
use crate::mail::port_bindings::port_bindings;

fn socket(port: u16, loopback_only: bool) -> ListeningSocket {
    ListeningSocket {
        port,
        loopback_only,
    }
}

fn binding_of(sockets: &[ListeningSocket], port: u16) -> PortBinding {
    port_bindings(sockets)
        .into_iter()
        .find(|state| state.port == port)
        .unwrap()
        .binding
}

#[test]
fn every_one_of_the_seven_mail_ports_is_reported_even_when_nothing_listens() {
    let states = port_bindings(&[]);

    assert_eq!(states.len(), 7);
    assert!(
        states
            .iter()
            .all(|state| state.binding == PortBinding::NotBound)
    );
}

#[test]
fn a_public_listener_makes_the_port_public() {
    assert_eq!(binding_of(&[socket(25, false)], 25), PortBinding::Public);
}

#[test]
fn a_loopback_only_listener_is_not_public() {
    assert_eq!(
        binding_of(&[socket(25, true)], 25),
        PortBinding::LoopbackOnly
    );
}

#[test]
fn a_loopback_socket_beside_a_public_one_leaves_the_port_public() {
    let sockets = [socket(143, true), socket(143, false)];

    assert_eq!(binding_of(&sockets, 143), PortBinding::Public);
}

#[test]
fn a_listener_on_another_port_says_nothing_about_a_mail_port() {
    assert_eq!(
        binding_of(&[socket(8080, false)], 25),
        PortBinding::NotBound
    );
}
