//! Tests for `record_rdata`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::caa_flags::CaaFlags;
use maran_agent_core::validation::dns::caa_tag::CaaTag;
use maran_agent_core::validation::dns::caa_value::CaaValue;
use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::record_priority::RecordPriority;
use maran_agent_core::validation::dns::srv_weight::SrvWeight;
use maran_agent_core::validation::dns::txt_text::TxtText;
use maran_agent_core::validation::web::port::Port;

use super::super::model::record_data::RecordData;
use super::record_rdata;

fn name(text: &str) -> DnsName {
    DnsName::parse(text).unwrap()
}

#[test]
fn addresses_are_written_in_their_canonical_form() {
    assert_eq!(
        record_rdata(&RecordData::A("192.0.2.1".parse().unwrap())),
        "192.0.2.1"
    );
    assert_eq!(
        record_rdata(&RecordData::Aaaa("2001:0db8:0:0:0:0:0:1".parse().unwrap())),
        "2001:db8::1"
    );
}

#[test]
fn targets_are_written_absolute() {
    assert_eq!(
        record_rdata(&RecordData::Cname(name("target.example.net"))),
        "target.example.net."
    );
    assert_eq!(
        record_rdata(&RecordData::Ns(name("ns.example.net."))),
        "ns.example.net."
    );
}

#[test]
fn a_mail_exchanger_carries_its_priority() {
    let data = RecordData::Mx {
        priority: RecordPriority::parse(10).unwrap(),
        exchange: name("mail.example.com"),
    };

    assert_eq!(record_rdata(&data), "10 mail.example.com.");
}

#[test]
fn a_service_locator_carries_priority_weight_port_and_target() {
    let data = RecordData::Srv {
        priority: RecordPriority::parse(10).unwrap(),
        weight: SrvWeight::parse(20).unwrap(),
        port: Port::parse(5060).unwrap(),
        target: name("sip.example.com"),
    };

    assert_eq!(record_rdata(&data), "10 20 5060 sip.example.com.");
}

#[test]
fn an_authorization_quotes_only_its_value() {
    let data = RecordData::Caa {
        flags: CaaFlags::parse(128).unwrap(),
        tag: CaaTag::parse("issuewild").unwrap(),
        value: CaaValue::parse("letsencrypt.org").unwrap(),
    };

    assert_eq!(record_rdata(&data), "128 issuewild \"letsencrypt.org\"");
}

#[test]
fn a_short_text_is_one_quoted_string() {
    let data = RecordData::Txt(TxtText::parse("v=DMARC1; p=none").unwrap());

    assert_eq!(record_rdata(&data), "\"v=DMARC1; p=none\"");
}

#[test]
fn a_long_text_is_quoted_pieces_of_255_bytes() {
    let data = RecordData::Txt(TxtText::parse(&"a".repeat(300)).unwrap());

    assert_eq!(
        record_rdata(&data),
        format!("\"{}\" \"{}\"", "a".repeat(255), "a".repeat(45))
    );
}
