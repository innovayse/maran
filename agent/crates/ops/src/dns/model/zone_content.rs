//! The complete content of one zone, checked as a whole.

use std::collections::HashMap;

use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::record_name::RecordName;
use maran_agent_core::validation::dns::zone_name::ZoneName;

use crate::dns::model::record_data::RecordData;
use crate::dns::model::zone_content_fault::ZoneContentFault;
use crate::dns::model::zone_limits::ZoneLimits;
use crate::dns::model::zone_record::ZoneRecord;
use crate::dns::model::zone_soa::ZoneSoa;

/// Everything a zone file is rendered from: the apex, the SOA, the apex name
/// servers and every other record.
///
/// The fields are private and [`Self::new`] is the only constructor, so a value
/// of this type is the proof that the rules that concern a record's neighbours
/// held: the CNAME exclusivity that keeps a resolver from receiving two answers
/// for one name, no CNAME and no NS record at the apex, one TTL per set, no
/// repeated record, and the count and name-length ceilings. The rendered size is
/// checked by the renderer, which is the only place it is known.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ZoneContent {
    /// The zone apex.
    zone: ZoneName,
    /// The start-of-authority data.
    soa: ZoneSoa,
    /// The apex name servers, in the order written.
    nameservers: Vec<DnsName>,
    /// The other records, in the order written.
    records: Vec<ZoneRecord>,
}

impl ZoneContent {
    /// Checks the whole and wraps it.
    ///
    /// # Errors
    ///
    /// Every variant of [`ZoneContentFault`] except `RenderedTooLarge`: no or too
    /// many or repeated name servers, too many records, a name too long once the
    /// zone is appended, an NS record or a CNAME at the apex, a CNAME that shares
    /// its name or a repeated CNAME, a set whose records state different TTLs,
    /// and a repeated record. The index in each is the record's position.
    pub fn new(
        zone: ZoneName,
        soa: ZoneSoa,
        nameservers: Vec<DnsName>,
        records: Vec<ZoneRecord>,
    ) -> Result<Self, ZoneContentFault> {
        if nameservers.is_empty() {
            return Err(ZoneContentFault::NoNameserver);
        }

        if nameservers.len() > ZoneLimits::MAX_NAMESERVERS {
            return Err(ZoneContentFault::TooManyNameservers {
                max: ZoneLimits::MAX_NAMESERVERS,
            });
        }

        for (index, nameserver) in nameservers.iter().enumerate() {
            if nameservers[..index].contains(nameserver) {
                return Err(ZoneContentFault::DuplicateNameserver);
            }
        }

        if records.len() > ZoneLimits::MAX_RECORDS {
            return Err(ZoneContentFault::TooManyRecords {
                max: ZoneLimits::MAX_RECORDS,
            });
        }

        let mut seen: HashMap<&RecordName, Vec<&ZoneRecord>> = HashMap::new();
        for (index, record) in records.iter().enumerate() {
            record
                .name
                .fits_within(&zone)
                .map_err(|_| ZoneContentFault::RecordNameTooLong { index })?;

            if record.name.is_apex() {
                match record.data {
                    RecordData::Ns(_) => {
                        return Err(ZoneContentFault::ApexNameserverRecord { index });
                    }
                    RecordData::Cname(_) => {
                        return Err(ZoneContentFault::CnameAtApex { index });
                    }
                    _ => {}
                }
            }

            let earlier = seen.entry(&record.name).or_default();
            for other in earlier.iter() {
                if other.data == record.data {
                    return Err(ZoneContentFault::DuplicateRecord { index });
                }
                let either_is_cname = matches!(other.data, RecordData::Cname(_))
                    || matches!(record.data, RecordData::Cname(_));
                if either_is_cname {
                    return Err(ZoneContentFault::CnameWithOtherData { index });
                }
                if other.data.type_name() == record.data.type_name() && other.ttl != record.ttl {
                    return Err(ZoneContentFault::MismatchedTtl { index });
                }
            }
            earlier.push(record);
        }

        Ok(Self {
            zone,
            soa,
            nameservers,
            records,
        })
    }

    /// The zone apex.
    #[must_use]
    pub fn zone(&self) -> &ZoneName {
        &self.zone
    }

    /// The start-of-authority data.
    #[must_use]
    pub fn soa(&self) -> &ZoneSoa {
        &self.soa
    }

    /// The apex name servers, in the order written.
    #[must_use]
    pub fn nameservers(&self) -> &[DnsName] {
        &self.nameservers
    }

    /// The other records, in the order written.
    #[must_use]
    pub fn records(&self) -> &[ZoneRecord] {
        &self.records
    }
}

#[cfg(test)]
#[path = "../../tests/dns/model/zone_content_tests.rs"]
mod tests;
