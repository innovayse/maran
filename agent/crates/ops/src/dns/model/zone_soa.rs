//! The start-of-authority data of a zone.

use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::soa_interval::SoaInterval;
use maran_agent_core::validation::dns::zone_serial::ZoneSerial;

/// The SOA record's fields. The serial is the panel's: the agent never derives
/// one, so two agents given the same request render the same zone.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ZoneSoa {
    /// The primary name server.
    pub primary_nameserver: DnsName,
    /// The hostmaster mailbox in name form (`hostmaster.example.com`).
    pub hostmaster: DnsName,
    /// The zone serial.
    pub serial: ZoneSerial,
    /// How often a secondary refreshes.
    pub refresh: SoaInterval,
    /// How soon a secondary retries after a failed refresh.
    pub retry: SoaInterval,
    /// How long a secondary serves without a refresh.
    pub expire: SoaInterval,
    /// How long a resolver caches a negative answer.
    pub negative_ttl: DnsTtl,
}
