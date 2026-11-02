//! Tests for the mapping from a vhost listing onto the wire's flag and list.

use maran_ops::sites::{AccountSiteSuspension, SiteSuspensionFact};

use crate::services::accounts::to_wire_site_listing::to_wire_site_listing;

/// A directory that could not be listed travels as an unreadable flag, never as
/// a readable empty list.
#[test]
fn an_unreadable_directory_travels_as_not_readable_with_no_sites() {
    let (readable, sites) = to_wire_site_listing(AccountSiteSuspension::Unreadable);

    assert!(!readable);
    assert!(sites.is_empty());
}

/// The inverse control: a listed directory with no vhost is readable and empty.
#[test]
fn a_listed_directory_with_no_vhost_travels_as_readable_and_empty() {
    let (readable, sites) = to_wire_site_listing(AccountSiteSuspension::Listed(Vec::new()));

    assert!(readable);
    assert!(sites.is_empty());
}

/// Each vhost keeps its domain and whether it serves the stub.
#[test]
fn a_listed_vhost_keeps_its_domain_and_its_stub_flag() {
    let (readable, sites) = to_wire_site_listing(AccountSiteSuspension::Listed(vec![
        SiteSuspensionFact {
            domain: "a.example.test".to_owned(),
            serving_stub: true,
        },
        SiteSuspensionFact {
            domain: "b.example.test".to_owned(),
            serving_stub: false,
        },
    ]));

    assert!(readable);
    assert_eq!(sites.len(), 2);
    assert_eq!(sites[0].domain, "a.example.test");
    assert!(sites[0].serving_stub);
    assert_eq!(sites[1].domain, "b.example.test");
    assert!(!sites[1].serving_stub);
}
