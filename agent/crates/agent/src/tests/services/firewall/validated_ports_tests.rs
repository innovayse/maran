//! Tests for the host ports every firewall rpc carries.
//!
//! The refusals are the point of this file, and one of them is the reason the
//! field is a list at all: a host can serve SSH on several ports at once, and
//! a request that names one of them would have the agent open that one and
//! close the rest.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_ports;
use crate::proto::ErrorCode;

/// The panel port a healthy request carries.
const PANEL_PORT: u32 = 8443;

#[test]
fn every_ssh_port_the_request_names_is_kept_in_order() {
    let ports = validated_ports(&[2222, 2022, 22], PANEL_PORT).expect("a complete request");

    // All three, and in the order given. A validator that deduplicated or
    // sorted would be inventing a policy; a validator that kept only the first
    // would close two ports sshd is listening on.
    let kept: Vec<u16> = ports.ssh_ports.iter().map(|port| port.value()).collect();
    assert_eq!(kept, vec![2222, 2022, 22]);
    assert_eq!(ports.panel_port.value(), 8443);
}

#[test]
fn one_ssh_port_is_an_ordinary_request() {
    let ports = validated_ports(&[22], PANEL_PORT).expect("the ordinary host");

    assert_eq!(ports.ssh_ports.len(), 1);
    assert_eq!(ports.ssh_ports[0].value(), 22);
}

#[test]
fn an_empty_ssh_port_list_is_refused_rather_than_defaulted_to_twenty_two() {
    // THE assertion this field's shape exists for. A proto3 `repeated` field
    // nobody set arrives empty, and the rendered ruleset is `policy drop` — so
    // a defaulted [22] on a host whose sshd listens elsewhere renders an accept
    // for a port nothing is listening on and none for the port the operator is
    // connected through. The installer already falls back to 22 and logs it, so
    // an empty list here means something upstream broke. This goes red the day
    // somebody adds `if empty { vec![22] }`.
    let error = validated_ports(&[], PANEL_PORT).expect_err("an empty list must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn one_bad_ssh_port_refuses_the_whole_request_rather_than_being_dropped() {
    // Dropping it would close a port sshd is listening on while reporting
    // success — the same lockout as sending one port, arrived at by a
    // different route. The good ports in the list are what make this test
    // about dropping rather than about rejecting.
    for bad in [0, 65_536, 70_000] {
        let error = validated_ports(&[22, bad, 2222], PANEL_PORT)
            .expect_err("a port outside 1..=65535 must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{bad}");
    }
}

#[test]
fn an_absent_panel_port_is_refused_rather_than_defaulted() {
    // A panel lockout has no remote recovery path at all.
    let error = validated_ports(&[22], 0).expect_err("a zero panel port must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}
