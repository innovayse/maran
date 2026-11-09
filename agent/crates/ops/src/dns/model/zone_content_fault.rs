//! Why a complete record set was refused although every value in it was valid.

/// The cross-record rules a zone must satisfy that no single value can: they
/// concern a record's neighbours. Each carries the INDEX of the record in the
/// request, never caller text, so an error message can say which record without
/// repeating what it held.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum ZoneContentFault {
    /// The request carried no apex name server; a zone without an NS set cannot
    /// be loaded.
    #[error("a zone needs at least one name server")]
    NoNameserver,

    /// More apex name servers than a zone may have.
    #[error("a zone cannot have more than {max} name servers")]
    TooManyNameservers {
        /// The ceiling.
        max: usize,
    },

    /// The same name server twice.
    #[error("a name server is listed twice")]
    DuplicateNameserver,

    /// More records than a zone may hold.
    #[error("a zone cannot hold more than {max} records")]
    TooManyRecords {
        /// The ceiling.
        max: usize,
    },

    /// The rendered file would exceed the size ceiling.
    #[error("the zone would be larger than {max} bytes")]
    RenderedTooLarge {
        /// The ceiling, in bytes.
        max: usize,
    },

    /// A record's name, with the zone appended, exceeds 253 characters.
    #[error("record {index} has a name too long for this zone")]
    RecordNameTooLong {
        /// Position of the record in the request.
        index: usize,
    },

    /// An NS record at the apex: the apex name servers are the request's
    /// `nameservers` and nothing else.
    #[error("record {index} is an NS record at the apex; set the name servers instead")]
    ApexNameserverRecord {
        /// Position of the record in the request.
        index: usize,
    },

    /// A CNAME at the apex, where the SOA and NS records must live.
    #[error("record {index} is a CNAME at the apex")]
    CnameAtApex {
        /// Position of the record in the request.
        index: usize,
    },

    /// A CNAME at a name that has any other record, or a second CNAME.
    #[error("record {index} shares its name with a CNAME")]
    CnameWithOtherData {
        /// Position of the record that conflicts.
        index: usize,
    },

    /// Two records of one name and type with different TTLs; a set has one.
    #[error("record {index} has a different TTL from the rest of its set")]
    MismatchedTtl {
        /// Position of the record in the request.
        index: usize,
    },

    /// A record identical to an earlier one.
    #[error("record {index} repeats an earlier record")]
    DuplicateRecord {
        /// Position of the record in the request.
        index: usize,
    },
}
