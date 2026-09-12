//! Tests for the rule message a rule listing puts on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_ops::firewall::{FirewallRule, NftablesProtocol, PortSpan};

use crate::proto::Protocol;

use super::listed_rule;

/// One rule of this agent's ruleset over the given ports.
fn rule(ports: PortSpan, protocol: NftablesProtocol, source: &str) -> FirewallRule {
    FirewallRule {
        ports,
        protocol,
        source: SourceCidr::parse(source).expect("a valid source network"),
    }
}

/// A validated port, which is the only thing a span can be built from.
fn port(value: u32) -> Port {
    Port::parse(value).expect("a valid port")
}

#[test]
fn a_single_port_rule_reports_an_absent_upper_bound_and_not_a_zero() {
    // The compatibility claim, in the direction the panel reads. `port_to` is
    // `optional` on the wire, so absent is a value a newer panel can tell from
    // a bound of 0 — and it means "the single port `port`", which is the same
    // rule the panel showed before the field existed. A 0 would be a bound
    // below every port there is, and the panel refuses the whole listing on
    // one it cannot show honestly.
    let wire = listed_rule(&rule(
        PortSpan::single(port(8080)),
        NftablesProtocol::Tcp,
        "0.0.0.0/0",
    ));

    assert_eq!(wire.port, 8080);
    assert_eq!(wire.port_to, None);
    assert_eq!(wire.protocol, Protocol::Tcp as i32);
    assert_eq!(wire.source_cidr, "0.0.0.0/0");
}

#[test]
fn a_range_rule_reports_both_of_its_bounds() {
    // The value and not a bound: `port` alone passes against a listing that
    // dropped the upper one, and a panel reading that listing would offer a
    // removal for a rule the firewall does not hold while ninety-nine ports
    // stayed open behind it.
    let wire = listed_rule(&rule(
        PortSpan::new(port(30000), Some(port(30099))).expect("a valid range"),
        NftablesProtocol::Tcp,
        "0.0.0.0/0",
    ));

    assert_eq!(wire.port, 30000);
    assert_eq!(wire.port_to, Some(30099));
}

#[test]
fn a_scoped_udp_range_reports_its_protocol_and_its_canonical_source_beside_the_bounds() {
    // The other three fields are asserted here as well, because a listing is
    // read as one rule: a bound reported beside the wrong protocol or a
    // re-spelled source is a rule the panel cannot send back to remove.
    let wire = listed_rule(&rule(
        PortSpan::new(port(52000), Some(port(52049))).expect("a valid range"),
        NftablesProtocol::Udp,
        "2001:db8::/32",
    ));

    assert_eq!(wire.port, 52000);
    assert_eq!(wire.port_to, Some(52049));
    assert_eq!(wire.protocol, Protocol::Udp as i32);
    assert_eq!(wire.source_cidr, "2001:db8::/32");
}
