//! What a `PutZone` request carries, revalidated as one zone.

use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::web::domain_error::DomainError;
use maran_agent_core::validation::web::panel_host_names::PanelHostNames;
use maran_ops::dns::ZoneContent;

use crate::proto::{AgentError, PutZoneRequest};
use crate::services::dns::validated_record::validated_record;
use crate::services::dns::validated_signing::validated_signing;
use crate::services::dns::validated_soa::validated_soa;
use crate::services::dns::validated_zone_name::validated_zone_name;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the zone name, the SOA, every apex name server and every record,
/// then checks the whole as a zone (the rules about a record's neighbours).
///
/// The agent never trusts the panel's own validation: each value passes through
/// the same validated type the panel's rules mirror, and the cross-record rules —
/// CNAME exclusivity, no apex NS or CNAME, one TTL per set, no repeats, the
/// ceilings — are checked here too, so a request that bypassed the panel cannot
/// make the daemon load a zone the panel would have refused. The signing
/// instruction goes through `validated_signing`: a key for an unsigned zone, no
/// key for a signed one and a key that does not parse are each refused.
///
/// # Errors
///
/// Returns the wire error for the first value or rule the agent will not accept.
pub fn validated_zone(
    request: &PutZoneRequest,
    panel: &PanelHostNames,
) -> Result<ZoneContent, AgentError> {
    let zone = validated_zone_name(&request.zone_name)?;
    if panel.refuses(zone.as_str()) {
        return Err(invalid_input(format!(
            "zone name: {}",
            DomainError::PanelHostName
        )));
    }
    let soa = validated_soa(request.soa.as_ref())?;

    let nameservers = request
        .nameservers
        .iter()
        .enumerate()
        .map(|(index, name)| {
            DnsName::parse(name)
                .map_err(|error| invalid_input(format!("name server {index}: {error}")))
        })
        .collect::<Result<Vec<_>, _>>()?;

    let records = request
        .records
        .iter()
        .enumerate()
        .map(|(index, record)| validated_record(index, record))
        .collect::<Result<Vec<_>, _>>()?;

    let signing = validated_signing(request.signing.as_ref())?;

    ZoneContent::new(zone, soa, nameservers, records)
        .map(|content| content.with_signing(signing))
        .map_err(|fault| invalid_input(fault.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/dns/validated_zone_tests.rs"]
mod tests;
