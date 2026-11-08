//! The zone files the agent renders, read by the daemon's own zone checker.
//!
//! A golden proves the agent renders what it meant to; it cannot prove the daemon
//! accepts it. This suite hands every shape of rendered zone to the real
//! `named-checkzone` of the platform — the exact program `put_zone` runs as its
//! validator — so a template change that the checker would refuse fails here
//! instead of on a customer's first record.
//!
//! **Positive and negative control.** The refusal case feeds the same checker a
//! file with a malformed address and requires a non-zero status: a checker that
//! accepted everything (a stub, the wrong binary) would otherwise pass every
//! other test in this file.
//!
//! **UNOBSERVED HERE:** the daemon itself. Nothing here starts `named`, adds a
//! zone with `rndc` or asks it a question with `dig`; those need the polygon's
//! daemon fixture of the DNS daemon phase. What is observed is that the checker
//! accepts the bytes `put_zone` would swap in.
//!
//! The suite needs `named-checkzone` installed (`bind9-utils` on the Debian
//! family, `bind-utils` on the RHEL family). Without it every test fails loudly:
//! a checker that was never found is not a checker that passed.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::process::Command;

use maran_agent_core::validation::dns::caa_flags::CaaFlags;
use maran_agent_core::validation::dns::caa_tag::CaaTag;
use maran_agent_core::validation::dns::caa_value::CaaValue;
use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::record_name::RecordName;
use maran_agent_core::validation::dns::record_priority::RecordPriority;
use maran_agent_core::validation::dns::soa_interval::SoaInterval;
use maran_agent_core::validation::dns::srv_weight::SrvWeight;
use maran_agent_core::validation::dns::txt_text::TxtText;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_agent_core::validation::dns::zone_serial::ZoneSerial;
use maran_agent_core::validation::web::port::Port;
use maran_distro::detect;
use maran_ops::dns::{RecordData, ZoneContent, ZoneRecord, ZoneSoa, render_zone};

/// The zone every test renders.
const ZONE: &str = "example.com";

fn name(text: &str) -> DnsName {
    DnsName::parse(text).unwrap()
}

fn record(owner: &str, ttl: u32, data: RecordData) -> ZoneRecord {
    ZoneRecord {
        name: if owner == "@" {
            RecordName::Apex
        } else {
            RecordName::parse(owner).unwrap()
        },
        ttl: DnsTtl::parse(ttl).unwrap(),
        data,
    }
}

/// A zone with one record of every type, a wildcard and a text long enough to be
/// split into two strings, with in-zone name servers that have addresses.
fn zone_with_every_type() -> ZoneContent {
    let soa = ZoneSoa {
        primary_nameserver: name("ns1.example.com"),
        hostmaster: name("hostmaster.example.com"),
        serial: ZoneSerial::parse(2_026_110_701).unwrap(),
        refresh: SoaInterval::parse(3600).unwrap(),
        retry: SoaInterval::parse(600).unwrap(),
        expire: SoaInterval::parse(1_209_600).unwrap(),
        negative_ttl: DnsTtl::parse(300).unwrap(),
    };
    let records = vec![
        record("ns1", 3600, RecordData::A("192.0.2.1".parse().unwrap())),
        record("ns2", 3600, RecordData::A("192.0.2.2".parse().unwrap())),
        record("@", 3600, RecordData::A("192.0.2.10".parse().unwrap())),
        record("@", 3600, RecordData::Aaaa("2001:db8::10".parse().unwrap())),
        record("www", 300, RecordData::Cname(name("example.com"))),
        record(
            "@",
            3600,
            RecordData::Mx {
                priority: RecordPriority::parse(10).unwrap(),
                exchange: name("mail.example.net"),
            },
        ),
        record(
            "@",
            3600,
            RecordData::Txt(TxtText::parse("v=spf1 mx ~all").unwrap()),
        ),
        record(
            "long",
            3600,
            RecordData::Txt(TxtText::parse(&"k".repeat(400)).unwrap()),
        ),
        record(
            "_dmarc",
            3600,
            RecordData::Txt(TxtText::parse("v=DMARC1; p=none").unwrap()),
        ),
        record(
            "_sip._tcp",
            3600,
            RecordData::Srv {
                priority: RecordPriority::parse(10).unwrap(),
                weight: SrvWeight::parse(20).unwrap(),
                port: Port::parse(5060).unwrap(),
                target: name("sip.example.net"),
            },
        ),
        record(
            "@",
            3600,
            RecordData::Caa {
                flags: CaaFlags::parse(0).unwrap(),
                tag: CaaTag::parse("issue").unwrap(),
                value: CaaValue::parse("letsencrypt.org").unwrap(),
            },
        ),
        record("sub", 3600, RecordData::Ns(name("ns.sub.example.net"))),
        record("*", 300, RecordData::A("192.0.2.11".parse().unwrap())),
    ];

    ZoneContent::new(
        ZoneName::parse(ZONE).unwrap(),
        soa,
        vec![name("ns1.example.com"), name("ns2.example.com")],
        records,
    )
    .unwrap()
}

/// Runs the platform's `named-checkzone` on `contents` and answers its exit
/// status and everything it printed.
fn check(contents: &str) -> (Option<i32>, String) {
    let distro = detect().expect("the polygon host is a supported system");
    let adapter = maran_distro::adapter_for(distro.family);
    let directory = tempfile::tempdir().unwrap();
    let file = directory.path().join(format!("{ZONE}.db"));
    std::fs::write(&file, contents).unwrap();

    let output = Command::new(adapter.dns_checkzone_binary().as_path())
        .arg("-q")
        .arg(ZONE)
        .arg(&file)
        .output()
        .expect("named-checkzone could not be started; is the BIND utilities package installed?");

    let printed = format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    (output.status.code(), printed)
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn a_zone_with_every_record_type_is_accepted_by_the_real_checker() {
    let rendered = render_zone(&zone_with_every_type()).unwrap();

    let (status, printed) = check(&rendered);

    assert_eq!(
        status,
        Some(0),
        "named-checkzone refused:\n{printed}\n{rendered}"
    );
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn a_zone_with_no_records_but_its_apex_is_accepted() {
    let base = zone_with_every_type();
    let apex_only = ZoneContent::new(
        base.zone().clone(),
        base.soa().clone(),
        vec![name("ns1.example.net")],
        vec![],
    )
    .unwrap();

    let (status, printed) = check(&render_zone(&apex_only).unwrap());

    assert_eq!(status, Some(0), "named-checkzone refused:\n{printed}");
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn the_checker_refuses_a_malformed_file_so_the_other_results_mean_something() {
    // The inverse control: the same checker, a file it must refuse.
    let rendered = render_zone(&zone_with_every_type())
        .unwrap()
        .replace("192.0.2.10", "192.0.2.300");

    let (status, _printed) = check(&rendered);

    assert_ne!(
        status,
        Some(0),
        "the checker accepted an impossible address"
    );
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn an_in_zone_name_server_without_an_address_is_refused_by_the_checker() {
    // Why the panel must send the glue: the checker refuses a delegation target
    // inside the zone that has no address record.
    let base = zone_with_every_type();
    let without_glue = ZoneContent::new(
        base.zone().clone(),
        base.soa().clone(),
        base.nameservers().to_vec(),
        vec![],
    )
    .unwrap();

    let (status, _printed) = check(&render_zone(&without_glue).unwrap());

    assert_ne!(status, Some(0));
}
