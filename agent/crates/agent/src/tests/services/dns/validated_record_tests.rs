//! Tests for `validated_record`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::dns::RecordData;

use super::validated_record;
use crate::proto::{DnsRecord, DnsRecordType, ErrorCode};

fn record(record_type: DnsRecordType) -> DnsRecord {
    DnsRecord {
        name: "www".to_owned(),
        r#type: record_type as i32,
        ttl: 300,
        ..DnsRecord::default()
    }
}

fn refused(record: &DnsRecord) -> String {
    let error = validated_record(3, record).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    error.message
}

#[test]
fn each_of_the_eight_types_is_accepted_with_its_own_fields() {
    let cases = [
        DnsRecord {
            address: "192.0.2.1".to_owned(),
            ..record(DnsRecordType::A)
        },
        DnsRecord {
            address: "2001:db8::1".to_owned(),
            ..record(DnsRecordType::Aaaa)
        },
        DnsRecord {
            target: "a.example.net".to_owned(),
            ..record(DnsRecordType::Cname)
        },
        DnsRecord {
            priority: 10,
            target: "mail.example.com".to_owned(),
            ..record(DnsRecordType::Mx)
        },
        DnsRecord {
            text: "v=spf1 mx ~all".to_owned(),
            ..record(DnsRecordType::Txt)
        },
        DnsRecord {
            priority: 1,
            weight: 2,
            port: 5060,
            target: "sip.example.com".to_owned(),
            ..record(DnsRecordType::Srv)
        },
        DnsRecord {
            caa_flags: 128,
            caa_tag: "issue".to_owned(),
            caa_value: "letsencrypt.org".to_owned(),
            ..record(DnsRecordType::Caa)
        },
        DnsRecord {
            target: "ns.example.net".to_owned(),
            ..record(DnsRecordType::Ns)
        },
    ];

    for case in cases {
        validated_record(0, &case).unwrap();
    }
}

#[test]
fn an_address_record_carries_the_parsed_address() {
    let parsed = validated_record(
        0,
        &DnsRecord {
            address: "192.0.2.1".to_owned(),
            ..record(DnsRecordType::A)
        },
    )
    .unwrap();

    assert_eq!(parsed.data, RecordData::A("192.0.2.1".parse().unwrap()));
}

#[test]
fn an_unspecified_or_unknown_type_is_refused() {
    assert!(refused(&record(DnsRecordType::Unspecified)).contains("record 3 type"));
    assert!(
        refused(&DnsRecord {
            r#type: 99,
            ..record(DnsRecordType::A)
        })
        .contains("type")
    );
}

#[test]
fn a_field_the_type_does_not_read_is_refused_and_named() {
    let message = refused(&DnsRecord {
        address: "192.0.2.1".to_owned(),
        text: "smuggled".to_owned(),
        ..record(DnsRecordType::A)
    });

    assert!(message.contains("text"), "{message}");
    assert!(!message.contains("smuggled"), "{message}");
}

#[test]
fn an_address_of_the_wrong_family_is_refused() {
    refused(&DnsRecord {
        address: "2001:db8::1".to_owned(),
        ..record(DnsRecordType::A)
    });
    refused(&DnsRecord {
        address: "192.0.2.1".to_owned(),
        ..record(DnsRecordType::Aaaa)
    });
    refused(&DnsRecord {
        address: "192.0.2.1\n".to_owned(),
        ..record(DnsRecordType::A)
    });
}

#[test]
fn the_injection_set_is_refused_in_every_free_field() {
    for hostile in ["a\nb", "a\rb", "a\0b", "a\"b", "a\\\\b"] {
        refused(&DnsRecord {
            text: hostile.to_owned(),
            ..record(DnsRecordType::Txt)
        });
        refused(&DnsRecord {
            caa_tag: "issue".to_owned(),
            caa_value: hostile.to_owned(),
            ..record(DnsRecordType::Caa)
        });
        refused(&DnsRecord {
            name: hostile.to_owned(),
            address: "192.0.2.1".to_owned(),
            ..record(DnsRecordType::A)
        });
        refused(&DnsRecord {
            target: format!("{hostile}.example.com"),
            ..record(DnsRecordType::Cname)
        });
    }
    for hostile in ["$INCLUDE", "$GENERATE", "a;b", "a/b", "..", "a..b"] {
        refused(&DnsRecord {
            name: hostile.to_owned(),
            address: "192.0.2.1".to_owned(),
            ..record(DnsRecordType::A)
        });
        refused(&DnsRecord {
            target: format!("{hostile}.example.com"),
            ..record(DnsRecordType::Cname)
        });
    }
}

#[test]
fn numbers_out_of_range_are_refused() {
    let mx = DnsRecord {
        target: "m.example.com".to_owned(),
        ..record(DnsRecordType::Mx)
    };
    refused(&DnsRecord {
        priority: 65_536,
        ..mx
    });
    let srv = DnsRecord {
        target: "s.example.com".to_owned(),
        port: 80,
        ..record(DnsRecordType::Srv)
    };
    refused(&DnsRecord {
        weight: 65_536,
        ..srv.clone()
    });
    refused(&DnsRecord {
        port: 0,
        ..srv.clone()
    });
    refused(&DnsRecord {
        port: 65_536,
        ..srv
    });
    refused(&DnsRecord {
        ttl: 59,
        address: "192.0.2.1".to_owned(),
        ..record(DnsRecordType::A)
    });
    refused(&DnsRecord {
        ttl: 604_801,
        address: "192.0.2.1".to_owned(),
        ..record(DnsRecordType::A)
    });
}

#[test]
fn caa_flags_and_tags_come_from_a_closed_set() {
    let caa = DnsRecord {
        caa_flags: 0,
        caa_tag: "issue".to_owned(),
        caa_value: "x.example".to_owned(),
        ..record(DnsRecordType::Caa)
    };
    refused(&DnsRecord {
        caa_flags: 1,
        ..caa.clone()
    });
    refused(&DnsRecord {
        caa_tag: "contactemail".to_owned(),
        ..caa
    });
}

#[test]
fn an_absolute_owner_name_is_refused() {
    refused(&DnsRecord {
        name: "www.example.com.".to_owned(),
        address: "192.0.2.1".to_owned(),
        ..record(DnsRecordType::A)
    });
}
