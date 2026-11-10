//! Tests for an account found on a source, on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_ops::transfer::inventory::model::source_account_record::SourceAccountRecord;
use maran_ops::transfer::inventory::model::source_database_record::SourceDatabaseRecord;
use maran_ops::transfer::inventory::model::source_site_backend::SourceSiteBackend;
use maran_ops::transfer::inventory::model::source_site_record::SourceSiteRecord;

use super::to_wire_source_account;
use crate::proto;

fn site(backend: SourceSiteBackend, php_version: Option<&str>) -> SourceSiteRecord {
    SourceSiteRecord {
        domain: Domain::parse("example.com").unwrap(),
        aliases: vec![Domain::parse("www.example.com").unwrap()],
        backend,
        php_version: php_version.map(str::to_owned),
        document_root: "/home/alice/public_html".to_owned(),
    }
}

fn account(disk: Option<u64>, size: Option<u64>) -> SourceAccountRecord {
    SourceAccountRecord {
        name: AccountName::parse("alice").unwrap(),
        home: "/home/alice".to_owned(),
        disk_bytes: disk,
        sites: vec![site(SourceSiteBackend::Php, Some("8.3"))],
        databases: vec![SourceDatabaseRecord {
            name: DatabaseName::for_account(&AccountName::parse("alice").unwrap(), "shop").unwrap(),
            size_bytes: size,
        }],
        cron_entry_count: 3,
        mailbox_count: 2,
    }
}

#[test]
fn a_measured_account_carries_every_figure_and_name() {
    let wire = to_wire_source_account(&account(Some(1000), Some(50)));

    assert_eq!(wire.name, "alice");
    assert_eq!(wire.home, "/home/alice");
    assert_eq!((wire.disk_bytes, wire.disk_known), (1000, true));
    assert_eq!((wire.cron_entry_count, wire.mailbox_count), (3, 2));
    assert_eq!(wire.sites.len(), 1);
    assert_eq!(wire.sites[0].domain, "example.com");
    assert_eq!(wire.sites[0].aliases, vec!["www.example.com".to_owned()]);
    assert_eq!(wire.sites[0].backend, proto::SourceSiteBackend::Php as i32);
    assert_eq!(wire.sites[0].php_version, "8.3");
    assert_eq!(wire.sites[0].document_root, "/home/alice/public_html");
    assert_eq!(wire.databases.len(), 1);
    assert_eq!(wire.databases[0].name, "alice_shop");
    assert_eq!(
        (wire.databases[0].size_bytes, wire.databases[0].size_known),
        (50, true)
    );
}

#[test]
fn an_unmeasured_size_is_zero_with_its_known_flag_false() {
    let wire = to_wire_source_account(&account(None, None));

    assert_eq!((wire.disk_bytes, wire.disk_known), (0, false));
    assert_eq!(
        (wire.databases[0].size_bytes, wire.databases[0].size_known),
        (0, false)
    );
}

#[test]
fn each_site_backend_has_its_own_member_and_no_php_version_is_empty_text() {
    for (backend, expected) in [
        (SourceSiteBackend::Static, proto::SourceSiteBackend::Static),
        (SourceSiteBackend::Php, proto::SourceSiteBackend::Php),
        (SourceSiteBackend::Proxy, proto::SourceSiteBackend::Proxy),
    ] {
        let mut record = account(None, None);
        record.sites = vec![site(backend, None)];

        let wire = to_wire_source_account(&record);

        assert_eq!(wire.sites[0].backend, expected as i32);
        assert!(wire.sites[0].php_version.is_empty());
    }
}
