//! Tests for `render_zone`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::record_name::RecordName;
use maran_agent_core::validation::dns::txt_text::TxtText;

use super::super::dns_error::DnsError;
use super::super::fake_zone_host::sample_content;
use super::super::model::record_data::RecordData;
use super::super::model::zone_content::ZoneContent;
use super::super::model::zone_content_fault::ZoneContentFault;
use super::super::model::zone_limits::ZoneLimits;
use super::super::model::zone_record::ZoneRecord;
use super::render_zone;

#[test]
fn a_zone_renders_the_header_the_name_servers_and_every_record_in_order() {
    let rendered = render_zone(&sample_content("example.com", 2_026_110_701)).unwrap();

    let expected = [
        "; Maran DNS zone. Rendered by maran-agent from the panel's records and replaced",
        "; whole on every change; an edit made here does not survive.",
        "$ORIGIN example.com.",
        "$TTL 3600",
        "@ IN SOA ns1.example.com. hostmaster.example.com. (",
        "        2026110701 ; serial",
        "        3600 ; refresh",
        "        600 ; retry",
        "        1209600 ; expire",
        "        300 ) ; negative caching ttl",
        "@ IN NS ns1.example.com.",
        "@ IN NS ns2.example.com.",
        "www 300 IN A 192.0.2.10",
        "@ 3600 IN MX 10 mail.example.com.",
        "",
    ]
    .join("\n");
    assert_eq!(rendered, expected);
}

#[test]
fn a_zone_larger_than_the_ceiling_is_refused() {
    let base = sample_content("example.com", 1);
    let records: Vec<ZoneRecord> = (0..1_100)
        .map(|index| ZoneRecord {
            name: RecordName::parse(&format!("n{index}")).unwrap(),
            ttl: DnsTtl::parse(300).unwrap(),
            data: RecordData::Txt(TxtText::parse(&"a".repeat(TxtText::MAX_LENGTH)).unwrap()),
        })
        .collect();
    let nameservers: Vec<DnsName> = base.nameservers().to_vec();
    let content = ZoneContent::new(
        base.zone().clone(),
        base.soa().clone(),
        nameservers,
        records,
    )
    .unwrap();

    assert_eq!(
        render_zone(&content),
        Err(DnsError::ZoneContent(ZoneContentFault::RenderedTooLarge {
            max: ZoneLimits::MAX_RENDERED_BYTES
        }))
    );
}
