//! Tests for `validated_zone`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_zone;
use crate::proto::{DnsRecord, DnsRecordType, DnsZoneSoa, ErrorCode, PutZoneRequest};

fn request() -> PutZoneRequest {
    PutZoneRequest {
        zone_name: "example.com".to_owned(),
        soa: Some(DnsZoneSoa {
            primary_nameserver: "ns1.example.com".to_owned(),
            hostmaster: "hostmaster.example.com".to_owned(),
            serial: 1,
            refresh: 3600,
            retry: 600,
            expire: 1_209_600,
            negative_ttl: 300,
        }),
        nameservers: vec!["ns1.example.com".to_owned(), "ns2.example.com".to_owned()],
        records: vec![DnsRecord {
            name: "www".to_owned(),
            r#type: DnsRecordType::A as i32,
            ttl: 300,
            address: "192.0.2.1".to_owned(),
            ..DnsRecord::default()
        }],
    }
}

#[test]
fn a_complete_request_becomes_a_zone() {
    let zone = validated_zone(&request()).unwrap();

    assert_eq!(zone.zone().as_str(), "example.com");
    assert_eq!(zone.nameservers().len(), 2);
    assert_eq!(zone.records().len(), 1);
}

#[test]
fn a_request_without_name_servers_is_refused() {
    let refused = validated_zone(&PutZoneRequest {
        nameservers: vec![],
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_bad_name_server_is_refused() {
    let refused = validated_zone(&PutZoneRequest {
        nameservers: vec!["ns1".to_owned()],
        ..request()
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_rule_about_neighbouring_records_is_enforced_at_the_agent_too() {
    let mut cname_beside_data = request();
    cname_beside_data.records.push(DnsRecord {
        name: "www".to_owned(),
        r#type: DnsRecordType::Cname as i32,
        ttl: 300,
        target: "other.example.net".to_owned(),
        ..DnsRecord::default()
    });

    let refused = validated_zone(&cname_beside_data).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(refused.message.contains("CNAME"), "{}", refused.message);
}

#[test]
fn a_bad_record_is_refused_by_index() {
    let mut bad = request();
    bad.records.push(DnsRecord {
        name: "x".to_owned(),
        r#type: DnsRecordType::Txt as i32,
        ttl: 300,
        text: "a\"b".to_owned(),
        ..DnsRecord::default()
    });

    let refused = validated_zone(&bad).unwrap_err();

    assert!(refused.message.contains("record 1"), "{}", refused.message);
}
