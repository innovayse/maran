//! The data of one record, by type.

use std::net::{Ipv4Addr, Ipv6Addr};

use maran_agent_core::validation::dns::caa_flags::CaaFlags;
use maran_agent_core::validation::dns::caa_tag::CaaTag;
use maran_agent_core::validation::dns::caa_value::CaaValue;
use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::record_priority::RecordPriority;
use maran_agent_core::validation::dns::srv_weight::SrvWeight;
use maran_agent_core::validation::dns::txt_text::TxtText;
use maran_agent_core::validation::web::port::Port;

/// What a record says, as the closed set of eight types and the validated type of
/// every field of each. There is no variant that holds text to be written as it
/// is: a record that is not one of these cannot be expressed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RecordData {
    /// An IPv4 address.
    A(Ipv4Addr),
    /// An IPv6 address.
    Aaaa(Ipv6Addr),
    /// An alias to another name.
    Cname(DnsName),
    /// A mail exchanger.
    Mx {
        /// Lower is tried first.
        priority: RecordPriority,
        /// The mail host.
        exchange: DnsName,
    },
    /// Free text.
    Txt(TxtText),
    /// A service locator.
    Srv {
        /// Lower is tried first.
        priority: RecordPriority,
        /// Shares load among equal priorities.
        weight: SrvWeight,
        /// The service's port.
        port: Port,
        /// The host serving it.
        target: DnsName,
    },
    /// A certification authority authorization.
    Caa {
        /// The flags byte.
        flags: CaaFlags,
        /// The property.
        tag: CaaTag,
        /// The property's value.
        value: CaaValue,
    },
    /// A delegation of a name below the apex.
    Ns(DnsName),
}

impl RecordData {
    /// The type mnemonic as the zone file spells it.
    #[must_use]
    pub fn type_name(&self) -> &'static str {
        match self {
            Self::A(_) => "A",
            Self::Aaaa(_) => "AAAA",
            Self::Cname(_) => "CNAME",
            Self::Mx { .. } => "MX",
            Self::Txt(_) => "TXT",
            Self::Srv { .. } => "SRV",
            Self::Caa { .. } => "CAA",
            Self::Ns(_) => "NS",
        }
    }
}
