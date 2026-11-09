//! Byte-for-byte goldens for the zone file the agent writes, and the guards that
//! hold the properties a golden cannot.
//!
//! The golden diff is the review artifact: a reviewer reads exactly what the
//! daemon will be told about a zone, not a template's intention
//! (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::bind::zone_file::ZoneFile;
use maran_templates::bind::zone_record_line::ZoneRecordLine;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// The two apex name servers every golden uses.
fn nameservers() -> Vec<String> {
    vec!["ns1.example.com.".to_owned(), "ns2.example.com.".to_owned()]
}

/// Renders a zone with the header every golden shares and `records` below it.
fn render(nameservers: &[String], records: &[ZoneRecordLine<'_>]) -> String {
    ZoneFile {
        origin: "example.com.",
        default_ttl: 3600,
        primary_nameserver: "ns1.example.com.",
        hostmaster: "hostmaster.example.com.",
        serial: 2_026_110_701,
        refresh: 3600,
        retry: 600,
        expire: 1_209_600,
        negative_ttl: 300,
        nameservers,
        records,
    }
    .render_config()
    .unwrap()
}

/// A line with the TTL most goldens use.
fn line<'a>(owner: &'a str, record_type: &'a str, rdata: &'a str) -> ZoneRecordLine<'a> {
    ZoneRecordLine {
        owner,
        ttl: 3600,
        record_type,
        rdata,
    }
}

#[test]
fn a_zone_with_only_its_apex_renders_its_golden() {
    assert_eq!(
        render(&nameservers(), &[]),
        golden("bind/zone_file_apex_only.db")
    );
}

#[test]
fn a_zone_with_every_record_type_renders_its_golden() {
    let records = [
        line("@", "A", "192.0.2.10"),
        line("@", "AAAA", "2001:db8::10"),
        ZoneRecordLine {
            ttl: 300,
            ..line("www", "CNAME", "example.com.")
        },
        line("@", "MX", "10 mail.example.com."),
        line("@", "TXT", "\"v=spf1 mx ~all\""),
        line(
            "_dmarc",
            "TXT",
            "\"v=DMARC1; p=none; rua=mailto:d@example.com\"",
        ),
        line("_sip._tcp", "SRV", "10 20 5060 sip.example.com."),
        line("@", "CAA", "0 issue \"letsencrypt.org\""),
        line("sub", "NS", "ns.sub.example.net."),
        ZoneRecordLine {
            ttl: 300,
            ..line("*", "A", "192.0.2.11")
        },
    ];

    assert_eq!(
        render(&nameservers(), &records),
        golden("bind/zone_file.db")
    );
}

#[test]
fn a_text_in_two_chunks_renders_its_golden() {
    let rdata = format!("\"{}\" \"{}\"", "a".repeat(255), "b".repeat(45));
    let records = [line("@", "TXT", &rdata)];

    assert_eq!(
        render(&nameservers(), &records),
        golden("bind/zone_file_chunked_txt.db")
    );
}

#[test]
fn every_field_reaches_the_rendered_zone() {
    let rendered = ZoneFile {
        origin: "sentinel-origin.",
        default_ttl: 111,
        primary_nameserver: "sentinel-primary.",
        hostmaster: "sentinel-hostmaster.",
        serial: 222,
        refresh: 333,
        retry: 444,
        expire: 555,
        negative_ttl: 666,
        nameservers: &["sentinel-ns.".to_owned()],
        records: &[ZoneRecordLine {
            owner: "sentinel-owner",
            ttl: 777,
            record_type: "SENTINEL",
            rdata: "sentinel-rdata",
        }],
    }
    .render_config()
    .unwrap();

    for sentinel in [
        "sentinel-origin.",
        "111",
        "sentinel-primary.",
        "sentinel-hostmaster.",
        "222 ; serial",
        "333 ; refresh",
        "444 ; retry",
        "555 ; expire",
        "666 )",
        "sentinel-ns.",
        "sentinel-owner 777 IN SENTINEL sentinel-rdata",
    ] {
        assert!(
            rendered.contains(sentinel),
            "{sentinel:?} missing:\n{rendered}"
        );
    }
}

#[test]
fn a_line_appended_to_the_rendered_zone_is_a_record_of_its_own() {
    let rendered = render(&nameservers(), &[]);

    assert!(rendered.ends_with("@ IN NS ns2.example.com.\n"));
    let with_records = render(&nameservers(), &[line("@", "A", "192.0.2.10")]);
    assert!(with_records.ends_with("IN A 192.0.2.10\n"));
}

#[test]
fn the_only_directives_are_origin_and_ttl() {
    let rendered = render(&nameservers(), &[line("@", "A", "192.0.2.10")]);
    let directives: Vec<&str> = rendered
        .lines()
        .filter(|line| line.starts_with('$'))
        .collect();

    assert_eq!(directives, vec!["$ORIGIN example.com.", "$TTL 3600"]);
}
