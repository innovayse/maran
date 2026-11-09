//! Tests for `validated_soa`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_soa;
use crate::proto::{DnsZoneSoa, ErrorCode};

fn soa() -> DnsZoneSoa {
    DnsZoneSoa {
        primary_nameserver: "ns1.example.com".to_owned(),
        hostmaster: "hostmaster.example.com".to_owned(),
        serial: 2_026_110_701,
        refresh: 3600,
        retry: 600,
        expire: 1_209_600,
        negative_ttl: 300,
    }
}

#[test]
fn a_complete_soa_is_accepted() {
    let parsed = validated_soa(Some(&soa())).unwrap();

    assert_eq!(parsed.serial.value(), 2_026_110_701);
    assert_eq!(parsed.primary_nameserver.as_str(), "ns1.example.com");
}

#[test]
fn a_missing_soa_is_refused() {
    assert_eq!(
        validated_soa(None).unwrap_err().code,
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn every_field_is_checked() {
    let cases = [
        DnsZoneSoa {
            primary_nameserver: "ns1".to_owned(),
            ..soa()
        },
        DnsZoneSoa {
            hostmaster: "a\nb.example.com".to_owned(),
            ..soa()
        },
        DnsZoneSoa { serial: 0, ..soa() },
        DnsZoneSoa {
            refresh: 0,
            ..soa()
        },
        DnsZoneSoa { retry: 59, ..soa() },
        DnsZoneSoa {
            expire: 3_000_000,
            ..soa()
        },
        DnsZoneSoa {
            negative_ttl: 10,
            ..soa()
        },
    ];

    for case in cases {
        assert_eq!(
            validated_soa(Some(&case)).unwrap_err().code,
            ErrorCode::InvalidInput as i32,
            "{case:?}"
        );
    }
}
