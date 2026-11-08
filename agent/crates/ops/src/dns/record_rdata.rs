//! The text of a record's data, as a zone file writes it.

use crate::dns::model::record_data::RecordData;

/// Spells `data` the way the data column of a zone file does.
///
/// Built only from validated types, each of which has a closed alphabet, so the
/// text contains nothing a zone-file parser reads as a directive, a comment
/// outside quotes or a line end. Every name is written absolute, with its
/// trailing dot. A TXT body is written as quoted pieces of at most 255 bytes,
/// separated by one space; the CAA value is quoted whole; neither alphabet
/// contains a quote or a backslash, so no escaping exists to get wrong.
#[must_use]
pub fn record_rdata(data: &RecordData) -> String {
    match data {
        RecordData::A(address) => address.to_string(),
        RecordData::Aaaa(address) => address.to_string(),
        RecordData::Cname(target) | RecordData::Ns(target) => target.fully_qualified(),
        RecordData::Mx { priority, exchange } => {
            format!("{} {}", priority.value(), exchange.fully_qualified())
        }
        RecordData::Txt(text) => text
            .chunks()
            .iter()
            .map(|chunk| format!("\"{chunk}\""))
            .collect::<Vec<_>>()
            .join(" "),
        RecordData::Srv {
            priority,
            weight,
            port,
            target,
        } => format!(
            "{} {} {} {}",
            priority.value(),
            weight.value(),
            port.value(),
            target.fully_qualified()
        ),
        RecordData::Caa { flags, tag, value } => {
            format!("{} {} \"{}\"", flags.value(), tag.as_str(), value.as_str())
        }
    }
}

#[cfg(test)]
#[path = "../tests/dns/record_rdata_tests.rs"]
mod tests;
