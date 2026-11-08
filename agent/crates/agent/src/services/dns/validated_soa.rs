//! What a request's start-of-authority data carries, revalidated.

use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::soa_interval::SoaInterval;
use maran_agent_core::validation::dns::zone_serial::ZoneSerial;
use maran_ops::dns::ZoneSoa;

use crate::proto::{AgentError, DnsZoneSoa};
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the SOA a `PutZone` request carries: both names, the serial, the
/// three intervals and the negative-caching TTL. A missing SOA is refused, since
/// a zone cannot be loaded without one.
///
/// # Errors
///
/// Returns the wire error for the first field the agent will not accept.
pub fn validated_soa(soa: Option<&DnsZoneSoa>) -> Result<ZoneSoa, AgentError> {
    let soa = soa
        .ok_or_else(|| invalid_input("soa: a zone needs its start-of-authority data".to_owned()))?;
    let refuse = |field: &str, reason: String| invalid_input(format!("soa {field}: {reason}"));

    Ok(ZoneSoa {
        primary_nameserver: DnsName::parse(&soa.primary_nameserver)
            .map_err(|e| refuse("primary_nameserver", e.to_string()))?,
        hostmaster: DnsName::parse(&soa.hostmaster)
            .map_err(|e| refuse("hostmaster", e.to_string()))?,
        serial: ZoneSerial::parse(soa.serial).map_err(|e| refuse("serial", e.to_string()))?,
        refresh: SoaInterval::parse(soa.refresh).map_err(|e| refuse("refresh", e.to_string()))?,
        retry: SoaInterval::parse(soa.retry).map_err(|e| refuse("retry", e.to_string()))?,
        expire: SoaInterval::parse(soa.expire).map_err(|e| refuse("expire", e.to_string()))?,
        negative_ttl: DnsTtl::parse(soa.negative_ttl)
            .map_err(|e| refuse("negative_ttl", e.to_string()))?,
    })
}

#[cfg(test)]
#[path = "../../tests/services/dns/validated_soa_tests.rs"]
mod tests;
