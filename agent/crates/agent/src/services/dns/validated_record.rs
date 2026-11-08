//! What one record of a request carries, revalidated.

use maran_agent_core::validation::dns::caa_flags::CaaFlags;
use maran_agent_core::validation::dns::caa_tag::CaaTag;
use maran_agent_core::validation::dns::caa_value::CaaValue;
use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::record_name::RecordName;
use maran_agent_core::validation::dns::record_priority::RecordPriority;
use maran_agent_core::validation::dns::srv_weight::SrvWeight;
use maran_agent_core::validation::dns::txt_text::TxtText;
use maran_agent_core::validation::web::port::Port;
use maran_ops::dns::{RecordData, ZoneRecord};

use crate::proto::{AgentError, DnsRecord, DnsRecordType};
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates one record: its owner name, its TTL, and the fields its type
/// reads through the validated type of each.
///
/// A field the type does not read must be at its default. That is not tidiness:
/// the wire carries one flat message for eight types, and a value in a field the
/// type ignores is either a caller that confused two types or a value that was
/// meant to be written somewhere. Either is refused, so nothing sent is silently
/// dropped. `index` is the record's position, named in the refusal so the panel
/// can say which record without the agent repeating its content.
///
/// # Errors
///
/// Returns the wire error for an unspecified or unknown type, a field the type
/// does not read that is not at its default, or a value a validated type refuses.
pub fn validated_record(index: usize, record: &DnsRecord) -> Result<ZoneRecord, AgentError> {
    let refuse =
        |field: &str, reason: String| invalid_input(format!("record {index} {field}: {reason}"));

    let name = RecordName::parse(&record.name).map_err(|e| refuse("name", e.to_string()))?;
    let ttl = DnsTtl::parse(record.ttl).map_err(|e| refuse("ttl", e.to_string()))?;
    let record_type = DnsRecordType::try_from(record.r#type)
        .map_err(|_| refuse("type", "not a known record type".to_owned()))?;

    // The fields each type reads; everything else must be at its default.
    let used = match record_type {
        DnsRecordType::Unspecified => {
            return Err(refuse("type", "a record needs a type".to_owned()));
        }
        DnsRecordType::A | DnsRecordType::Aaaa => &["address"][..],
        DnsRecordType::Cname | DnsRecordType::Ns => &["target"][..],
        DnsRecordType::Mx => &["priority", "target"][..],
        DnsRecordType::Txt => &["text"][..],
        DnsRecordType::Srv => &["priority", "weight", "port", "target"][..],
        DnsRecordType::Caa => &["caa_flags", "caa_tag", "caa_value"][..],
    };
    let strays = [
        ("address", !record.address.is_empty()),
        ("target", !record.target.is_empty()),
        ("priority", record.priority != 0),
        ("weight", record.weight != 0),
        ("port", record.port != 0),
        ("caa_flags", record.caa_flags != 0),
        ("caa_tag", !record.caa_tag.is_empty()),
        ("caa_value", !record.caa_value.is_empty()),
        ("text", !record.text.is_empty()),
    ];
    if let Some((field, _)) = strays
        .iter()
        .find(|(field, set)| *set && !used.contains(field))
    {
        return Err(refuse(field, "not used by this record type".to_owned()));
    }

    let target = || DnsName::parse(&record.target).map_err(|e| refuse("target", e.to_string()));
    let priority =
        || RecordPriority::parse(record.priority).map_err(|e| refuse("priority", e.to_string()));

    let data = match record_type {
        DnsRecordType::Unspecified => {
            return Err(refuse("type", "a record needs a type".to_owned()));
        }
        DnsRecordType::A => RecordData::A(
            record
                .address
                .parse()
                .map_err(|_| refuse("address", "not an IPv4 address".to_owned()))?,
        ),
        DnsRecordType::Aaaa => RecordData::Aaaa(
            record
                .address
                .parse()
                .map_err(|_| refuse("address", "not an IPv6 address".to_owned()))?,
        ),
        DnsRecordType::Cname => RecordData::Cname(target()?),
        DnsRecordType::Ns => RecordData::Ns(target()?),
        DnsRecordType::Mx => RecordData::Mx {
            priority: priority()?,
            exchange: target()?,
        },
        DnsRecordType::Txt => RecordData::Txt(
            TxtText::parse(&record.text).map_err(|e| refuse("text", e.to_string()))?,
        ),
        DnsRecordType::Srv => RecordData::Srv {
            priority: priority()?,
            weight: SrvWeight::parse(record.weight).map_err(|e| refuse("weight", e.to_string()))?,
            port: Port::parse(record.port).map_err(|e| refuse("port", e.to_string()))?,
            target: target()?,
        },
        DnsRecordType::Caa => RecordData::Caa {
            flags: CaaFlags::parse(record.caa_flags)
                .map_err(|e| refuse("caa_flags", e.to_string()))?,
            tag: CaaTag::parse(&record.caa_tag).map_err(|e| refuse("caa_tag", e.to_string()))?,
            value: CaaValue::parse(&record.caa_value)
                .map_err(|e| refuse("caa_value", e.to_string()))?,
        },
    };

    Ok(ZoneRecord { name, ttl, data })
}

#[cfg(test)]
#[path = "../../tests/services/dns/validated_record_tests.rs"]
mod tests;
