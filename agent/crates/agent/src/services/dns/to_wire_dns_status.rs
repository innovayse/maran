//! A DNS observation onto its wire payload.

use maran_ops::dns::{DnsPolicyObservation, DnsState};

use crate::proto::{DnsDaemonStatus, DnsPolicyObservation as WirePolicyObservation};

/// Builds the `DnsDaemonStatus` that `EnableDns`, `DisableDns` and
/// `GetDnsStatus` all answer with, so the reading of an observed daemon happens
/// once for all three.
#[must_use]
pub fn to_wire_dns_status(state: &DnsState) -> DnsDaemonStatus {
    DnsDaemonStatus {
        running: state.running,
        control_channel_answered: state.control_channel_answered,
        port_53_answering: state.port_53_answering,
        recursion_refused: to_wire_observation(state.recursion_refused) as i32,
        response_rate_limiting: to_wire_observation(state.response_rate_limiting) as i32,
        enabled_at_boot: state.enabled_at_boot,
        dnssec_supported: state.dnssec_supported,
    }
}

/// One three-valued reading onto the wire enum. An unreadable configuration is
/// `NOT_OBSERVED`, which certifies nothing, and is never folded into either of
/// the other two.
fn to_wire_observation(observation: DnsPolicyObservation) -> WirePolicyObservation {
    match observation {
        DnsPolicyObservation::NotObserved => WirePolicyObservation::NotObserved,
        DnsPolicyObservation::InForce => WirePolicyObservation::InForce,
        DnsPolicyObservation::NotInForce => WirePolicyObservation::NotInForce,
    }
}

#[cfg(test)]
#[path = "../../tests/services/dns/to_wire_dns_status_tests.rs"]
mod tests;
