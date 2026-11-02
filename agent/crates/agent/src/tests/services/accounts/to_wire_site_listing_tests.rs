//! Tests for the mapping from a vhost listing onto the wire's flag and list.

use maran_ops::sites::{AccountSiteSuspension, SiteServing, SiteSuspensionFact};

use crate::proto::SiteServing as WireSiteServing;

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

/// Each vhost keeps its domain and what it was observed to serve, and the
/// deprecated flag stays true only for an observed stub.
#[test]
fn a_listed_vhost_keeps_its_domain_and_what_it_serves() {
    let (readable, sites) = to_wire_site_listing(AccountSiteSuspension::Listed(vec![
        SiteSuspensionFact {
            domain: "a.example.test".to_owned(),
            serving: SiteServing::Stub,
        },
        SiteSuspensionFact {
            domain: "b.example.test".to_owned(),
            serving: SiteServing::NotStub,
        },
        SiteSuspensionFact {
            domain: "c.example.test".to_owned(),
            serving: SiteServing::Unobservable,
        },
    ]));

    assert!(readable);
    assert_eq!(sites.len(), 3);
    assert_eq!(sites[0].domain, "a.example.test");
    assert_eq!(sites[0].serving, WireSiteServing::Stub as i32);
    assert!(sites[0].serving_stub);
    assert_eq!(sites[1].domain, "b.example.test");
    assert_eq!(sites[1].serving, WireSiteServing::NotStub as i32);
    assert!(!sites[1].serving_stub);
    assert_eq!(sites[2].domain, "c.example.test");
    assert_eq!(sites[2].serving, WireSiteServing::Unobservable as i32);
    assert!(
        !sites[2].serving_stub,
        "an unobservable vhost must still read as not stubbed to the old flag"
    );
}
