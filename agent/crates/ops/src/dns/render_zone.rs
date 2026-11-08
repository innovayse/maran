//! Turning a checked zone into the text of its file.

use maran_templates::bind::zone_file::ZoneFile;
use maran_templates::bind::zone_record_line::ZoneRecordLine;

use crate::dns::dns_error::DnsError;
use crate::dns::model::zone_content::ZoneContent;
use crate::dns::model::zone_content_fault::ZoneContentFault;
use crate::dns::model::zone_limits::ZoneLimits;
use crate::dns::record_rdata::record_rdata;

/// The `$TTL` of the zone file, which is the TTL of the lines that state none:
/// the apex name servers. The panel offers no setting for it, and every record
/// states its own.
const APEX_TTL: u32 = 3600;

/// Renders `content` into the zone file text.
///
/// # Errors
///
/// Returns [`DnsError::ZoneContent`] with `RenderedTooLarge` when the file would
/// exceed the size ceiling, and [`DnsError::Render`] when the template and its
/// render type have drifted apart.
pub fn render_zone(content: &ZoneContent) -> Result<String, DnsError> {
    let nameservers: Vec<String> = content
        .nameservers()
        .iter()
        .map(|nameserver| nameserver.fully_qualified())
        .collect();
    let rdata: Vec<String> = content
        .records()
        .iter()
        .map(|record| record_rdata(&record.data))
        .collect();
    let lines: Vec<ZoneRecordLine<'_>> = content
        .records()
        .iter()
        .zip(&rdata)
        .map(|(record, rdata)| ZoneRecordLine {
            owner: record.name.owner_text(),
            ttl: record.ttl.seconds(),
            record_type: record.data.type_name(),
            rdata,
        })
        .collect();

    let soa = content.soa();
    let origin = content.zone().fully_qualified();
    let primary_nameserver = soa.primary_nameserver.fully_qualified();
    let hostmaster = soa.hostmaster.fully_qualified();

    let rendered = ZoneFile {
        origin: &origin,
        default_ttl: APEX_TTL,
        primary_nameserver: &primary_nameserver,
        hostmaster: &hostmaster,
        serial: soa.serial.value(),
        refresh: soa.refresh.seconds(),
        retry: soa.retry.seconds(),
        expire: soa.expire.seconds(),
        negative_ttl: soa.negative_ttl.seconds(),
        nameservers: &nameservers,
        records: &lines,
    }
    .render_config()
    .map_err(|_| DnsError::Render)?;

    if rendered.len() > ZoneLimits::MAX_RENDERED_BYTES {
        return Err(ZoneContentFault::RenderedTooLarge {
            max: ZoneLimits::MAX_RENDERED_BYTES,
        }
        .into());
    }

    Ok(rendered)
}

#[cfg(test)]
#[path = "../tests/dns/render_zone_tests.rs"]
mod tests;
