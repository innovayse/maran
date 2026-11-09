//! Tests for `zone_content`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::record_name::RecordName;
use maran_agent_core::validation::dns::txt_text::TxtText;

use super::super::super::fake_zone_host::sample_content;
use super::super::record_data::RecordData;
use super::super::zone_content_fault::ZoneContentFault;
use super::super::zone_limits::ZoneLimits;
use super::super::zone_record::ZoneRecord;
use super::ZoneContent;

fn record(name: &str, ttl: u32, data: RecordData) -> ZoneRecord {
    ZoneRecord {
        name: if name == "@" {
            RecordName::Apex
        } else {
            RecordName::parse(name).unwrap()
        },
        ttl: DnsTtl::parse(ttl).unwrap(),
        data,
    }
}

fn address(text: &str) -> RecordData {
    RecordData::A(text.parse().unwrap())
}

fn cname(target: &str) -> RecordData {
    RecordData::Cname(DnsName::parse(target).unwrap())
}

fn txt(text: &str) -> RecordData {
    RecordData::Txt(TxtText::parse(text).unwrap())
}

/// Builds a zone around `records` with the sample header.
fn build(records: Vec<ZoneRecord>) -> Result<ZoneContent, ZoneContentFault> {
    let base = sample_content("example.com", 1);
    ZoneContent::new(
        base.zone().clone(),
        base.soa().clone(),
        base.nameservers().to_vec(),
        records,
    )
}

fn nameserver(text: &str) -> DnsName {
    DnsName::parse(text).unwrap()
}

#[test]
fn a_sensible_zone_is_accepted() {
    let zone = build(vec![
        record("www", 300, address("192.0.2.1")),
        record("www", 300, address("192.0.2.2")),
        record("blog", 300, cname("www.example.com")),
        record("*", 300, address("192.0.2.3")),
    ]);

    assert!(zone.is_ok());
}

#[test]
fn a_zone_needs_a_name_server() {
    let base = sample_content("example.com", 1);

    let zone = ZoneContent::new(base.zone().clone(), base.soa().clone(), vec![], vec![]);

    assert_eq!(zone, Err(ZoneContentFault::NoNameserver));
}

#[test]
fn nine_name_servers_and_a_repeated_one_are_refused() {
    let base = sample_content("example.com", 1);
    let nine: Vec<DnsName> = (0..9)
        .map(|i| nameserver(&format!("ns{i}.example.net")))
        .collect();
    let twice = vec![nameserver("ns1.example.net"), nameserver("ns1.example.net")];

    assert_eq!(
        ZoneContent::new(base.zone().clone(), base.soa().clone(), nine, vec![]),
        Err(ZoneContentFault::TooManyNameservers { max: 8 })
    );
    assert_eq!(
        ZoneContent::new(base.zone().clone(), base.soa().clone(), twice, vec![]),
        Err(ZoneContentFault::DuplicateNameserver)
    );
}

#[test]
fn more_records_than_the_ceiling_are_refused() {
    let records = (0..=ZoneLimits::MAX_RECORDS)
        .map(|index| record(&format!("n{index}"), 300, address("192.0.2.1")))
        .collect();

    assert_eq!(
        build(records),
        Err(ZoneContentFault::TooManyRecords {
            max: ZoneLimits::MAX_RECORDS
        })
    );
}

#[test]
fn a_name_that_overflows_once_the_zone_is_appended_is_refused() {
    let long = vec!["a".repeat(60); 4].join(".");

    assert_eq!(
        build(vec![
            record("ok", 300, address("192.0.2.1")),
            record(&long, 300, address("192.0.2.1"))
        ]),
        Err(ZoneContentFault::RecordNameTooLong { index: 1 })
    );
}

#[test]
fn an_ns_record_at_the_apex_is_refused_and_one_below_it_is_not() {
    let ns = RecordData::Ns(nameserver("ns.example.net"));

    assert_eq!(
        build(vec![record("@", 300, ns.clone())]),
        Err(ZoneContentFault::ApexNameserverRecord { index: 0 })
    );
    assert!(build(vec![record("sub", 300, ns)]).is_ok());
}

#[test]
fn a_cname_at_the_apex_is_refused() {
    assert_eq!(
        build(vec![record("@", 300, cname("other.example.net"))]),
        Err(ZoneContentFault::CnameAtApex { index: 0 })
    );
}

#[test]
fn a_cname_beside_other_data_is_refused_in_either_order() {
    assert_eq!(
        build(vec![
            record("www", 300, address("192.0.2.1")),
            record("www", 300, cname("a.example.net")),
        ]),
        Err(ZoneContentFault::CnameWithOtherData { index: 1 })
    );
    assert_eq!(
        build(vec![
            record("www", 300, cname("a.example.net")),
            record("www", 300, txt("hello")),
        ]),
        Err(ZoneContentFault::CnameWithOtherData { index: 1 })
    );
}

#[test]
fn two_cnames_at_one_name_are_refused() {
    assert_eq!(
        build(vec![
            record("www", 300, cname("a.example.net")),
            record("www", 300, cname("b.example.net")),
        ]),
        Err(ZoneContentFault::CnameWithOtherData { index: 1 })
    );
}

#[test]
fn a_set_with_two_ttls_is_refused() {
    assert_eq!(
        build(vec![
            record("www", 300, address("192.0.2.1")),
            record("www", 600, address("192.0.2.2")),
        ]),
        Err(ZoneContentFault::MismatchedTtl { index: 1 })
    );
}

#[test]
fn different_types_at_one_name_may_have_different_ttls() {
    assert!(
        build(vec![
            record("www", 300, address("192.0.2.1")),
            record("www", 600, txt("hello")),
        ])
        .is_ok()
    );
}

#[test]
fn a_repeated_record_is_refused() {
    assert_eq!(
        build(vec![
            record("www", 300, address("192.0.2.1")),
            record("www", 300, address("192.0.2.1")),
        ]),
        Err(ZoneContentFault::DuplicateRecord { index: 1 })
    );
}
