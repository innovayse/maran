//! Tests for the check that turns an allow or a deny into a rule.
//!
//! The refusals are the point of this file. Every one of them is a value the
//! agent will NOT act on, and the two that matter most are the ones a defaulted
//! field would have papered over: an absent ssh port and an absent protocol.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::firewall::NftablesProtocol;

use super::validated_rule;
use crate::proto::{ErrorCode, Protocol};

/// The ssh ports a healthy request carries — deliberately not 22, so a
/// defaulted value could not pass for the real one.
const SSH_PORTS: [u32; 1] = [2222];

/// The panel port a healthy request carries.
const PANEL_PORT: u32 = 8443;

/// Every source in this file is unrestricted unless a test says otherwise.
const ANY_SOURCE: &str = "0.0.0.0/0";

#[test]
fn a_complete_request_becomes_a_rule_and_the_two_host_ports() {
    let (ports, rule) = validated_rule(
        3306,
        Protocol::Tcp as i32,
        "10.0.0.0/8",
        &SSH_PORTS,
        PANEL_PORT,
    )
    .expect("a complete request must be accepted");

    // Both ports asserted by FIELD, and with different numbers: equal numbers
    // would pass just as well if the two were swapped, and that swap renders
    // SSH's hard allow for the panel's port and the panel's for SSH's.
    assert_eq!(ports.ssh_ports.len(), 1);
    assert_eq!(ports.ssh_ports[0].value(), 2222);
    assert_eq!(ports.panel_port.value(), 8443);
    assert_eq!(rule.port.value(), 3306);
    assert_eq!(rule.protocol, NftablesProtocol::Tcp);
    assert_eq!(rule.source.to_string(), "10.0.0.0/8");
    assert!(!rule.is_open_to_anyone());
}

#[test]
fn an_absent_ssh_port_is_refused_rather_than_defaulted_to_twenty_two() {
    // THE assertion this whole field exists for. A proto3 `repeated` field
    // nobody set arrives EMPTY, and the ruleset the agent renders is
    // `policy drop` — so a defaulted [22] on a host whose sshd listens on 2222
    // renders an accept for a port nothing is listening on and no accept for
    // the one the operator is connected through. This goes red the day somebody
    // "helpfully" defaults it.
    let error = validated_rule(80, Protocol::Tcp as i32, ANY_SOURCE, &[], PANEL_PORT)
        .expect_err("a zero ssh port must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_absent_panel_port_is_refused_rather_than_defaulted() {
    // Same reasoning, and the consequence is worse: a panel lockout has no
    // remote recovery path at all.
    let error = validated_rule(80, Protocol::Tcp as i32, ANY_SOURCE, &SSH_PORTS, 0)
        .expect_err("a zero panel port must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_port_outside_the_sixteen_bit_range_is_refused() {
    for (port, ssh, panel) in [
        (0, SSH_PORTS[0], PANEL_PORT),
        (65_536, SSH_PORTS[0], PANEL_PORT),
        (80, 65_536, PANEL_PORT),
        (80, SSH_PORTS[0], 70_000),
    ] {
        let error = validated_rule(port, Protocol::Tcp as i32, ANY_SOURCE, &[ssh], panel)
            .expect_err("a value outside 1..=65535 must be refused");

        assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "{port}/{ssh}/{panel}"
        );
    }
}

#[test]
fn an_unset_protocol_is_refused_rather_than_becoming_tcp() {
    // Under a drop policy a rule that silently became TCP does two wrong things
    // at once: the port the operator asked about stays closed, and one they
    // never asked about opens.
    for protocol in [Protocol::Unspecified as i32, 7, -1] {
        let error = validated_rule(80, protocol, ANY_SOURCE, &SSH_PORTS, PANEL_PORT)
            .expect_err("a protocol outside the contract must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{protocol}");
    }
}

#[test]
fn udp_is_carried_through_as_udp() {
    // The other half of the protocol check: refusing the unknown is only useful
    // if the known one is not quietly collapsed into TCP as well.
    let (_, rule) = validated_rule(
        443,
        Protocol::Udp as i32,
        ANY_SOURCE,
        &SSH_PORTS,
        PANEL_PORT,
    )
    .expect("a udp rule must be accepted");

    assert_eq!(rule.protocol, NftablesProtocol::Udp);
}

#[test]
fn a_source_that_is_not_a_network_is_refused() {
    // The one field of a rule whose bytes a caller composes. A newline in it
    // would append a rule of somebody else's choosing to a file applied as
    // root, so it is validated rather than escaped — there is no escaping in
    // nft's grammar to fall back on.
    for source in [
        "",
        "10.0.0.1",
        "not-a-network",
        "10.0.0.0/8\naccept",
        "10.0.0.0/33",
    ] {
        let error = validated_rule(80, Protocol::Tcp as i32, source, &SSH_PORTS, PANEL_PORT)
            .expect_err("a source that is not a CIDR network must be refused");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{source:?}");
    }
}

#[test]
fn an_unrestricted_rule_is_recognised_as_open_to_everyone() {
    let (_, rule) = validated_rule(80, Protocol::Tcp as i32, ANY_SOURCE, &SSH_PORTS, PANEL_PORT)
        .expect("an unrestricted rule must be accepted");

    assert!(rule.is_open_to_anyone());
}

#[test]
fn an_ipv6_any_source_is_a_restriction_and_not_a_second_spelling_of_everyone() {
    // `::/0` parses and is legitimate, but it renders as `ip6 saddr ::/0`,
    // which matches IPv6 traffic and nothing else. Reading it as "everyone"
    // would silently close a port to every IPv4 client.
    let (_, rule) = validated_rule(80, Protocol::Tcp as i32, "::/0", &SSH_PORTS, PANEL_PORT)
        .expect("an ipv6 default route must be accepted as a source");

    assert!(!rule.is_open_to_anyone());
}
