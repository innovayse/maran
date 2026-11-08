//! One record of a zone.

use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::record_name::RecordName;

use crate::dns::model::record_data::RecordData;

/// An owner name, a time to live and the data, each already validated.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ZoneRecord {
    /// Where in the zone the record sits.
    pub name: RecordName,
    /// How long a resolver may cache it.
    pub ttl: DnsTtl,
    /// What it says.
    pub data: RecordData,
}
