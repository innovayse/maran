//! Tests for what removing a deleted site's logs takes, and above all what it leaves.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::remove_site_logs;
use crate::sites::SitesOpError;
use crate::sites::fake_site_log_host::FakeSiteLogHost;

fn remove(host: &FakeSiteLogHost, domain: &str) -> Result<u32, SitesOpError> {
    remove_site_logs(
        host,
        &AccountName::parse("acme").unwrap(),
        &Domain::parse(domain).unwrap(),
    )
}

fn site_files(host: &FakeSiteLogHost, site: &str) {
    host.put(&format!("{site}.access.log"), b"203.0.113.9 GET /?sid=1");
    host.put(&format!("{site}.error.log"), b"error");
    host.put(&format!("{site}.access.log.1"), b"one");
    host.put(&format!("{site}.access.log.2.gz"), b"two");
    host.put(&format!("{site}.error.log.1"), b"one");
}

#[test]
fn every_live_and_rotated_log_of_the_site_is_removed() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "shop.example");

    let removed = remove(&host, "shop.example").unwrap();

    assert_eq!(removed, 5);
    assert!(host.names().is_empty());
}

#[test]
fn a_recreated_site_finds_no_history_from_the_previous_tenant() {
    // The attack in issue #477: delete, recreate the same pair, read the first lines.
    let host = FakeSiteLogHost::new();
    site_files(&host, "shop.example");
    remove(&host, "shop.example").unwrap();

    assert!(host.open_if_present("shop.example.access.log").is_none());
}

#[test]
fn other_sites_and_foreign_entries_stay_where_they_are() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "a.com");
    site_files(&host, "a.com.access");
    site_files(&host, "b.com");
    host.put("notes.txt", b"operator");
    host.put_directory("a.com.access.log.9");

    let removed = remove(&host, "a.com").unwrap();

    assert_eq!(removed, 5);
    let names = host.names();
    assert!(names.contains(&"a.com.access.access.log".to_owned()));
    assert!(names.contains(&"b.com.access.log".to_owned()));
    assert!(names.contains(&"notes.txt".to_owned()));
    assert!(names.contains(&"a.com.access.log.9".to_owned()));
    assert!(!names.contains(&"a.com.access.log".to_owned()));
}

#[test]
fn an_account_without_a_log_directory_is_success_and_removes_nothing() {
    let host = FakeSiteLogHost::new();
    host.have_no_directory();

    assert_eq!(remove(&host, "shop.example").unwrap(), 0);
}

#[test]
fn a_directory_root_does_not_own_is_refused_and_not_read_as_nothing_to_remove() {
    let host = FakeSiteLogHost::new();
    host.refuse_the_directory();

    assert!(matches!(
        remove(&host, "shop.example"),
        Err(SitesOpError::LogUnreadable { .. })
    ));
}

#[test]
fn a_second_removal_converges() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "shop.example");
    remove(&host, "shop.example").unwrap();

    assert_eq!(remove(&host, "shop.example").unwrap(), 0);
}
