//! Tests for the `ip_access` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::source_cidr::SourceCidr;

use super::{IpAccess, MAXIMUM_NETWORKS};
use crate::sites::model::ip_access_mode::IpAccessMode;
use crate::sites::model::site_options_error::SiteOptionsError;

/// The network `10.<index>.0.0/16`.
fn network(index: usize) -> SourceCidr {
    SourceCidr::parse(&format!("10.{index}.0.0/16")).unwrap()
}

#[test]
fn a_list_of_distinct_networks_keeps_its_mode_and_order() {
    let access = IpAccess::new(IpAccessMode::DenyList, vec![network(2), network(1)]).unwrap();

    assert_eq!(access.mode(), IpAccessMode::DenyList);
    assert_eq!(access.networks(), [network(2), network(1)]);
}

#[test]
fn an_empty_list_is_refused() {
    assert_eq!(
        IpAccess::new(IpAccessMode::AllowList, vec![]),
        Err(SiteOptionsError::EmptyIpList)
    );
}

#[test]
fn a_list_over_the_cap_is_refused() {
    let at_cap: Vec<SourceCidr> = (0..MAXIMUM_NETWORKS).map(network).collect();
    assert!(IpAccess::new(IpAccessMode::AllowList, at_cap).is_ok());

    let over: Vec<SourceCidr> = (0..=MAXIMUM_NETWORKS).map(network).collect();
    assert_eq!(
        IpAccess::new(IpAccessMode::AllowList, over),
        Err(SiteOptionsError::TooManyNetworks {
            count: MAXIMUM_NETWORKS + 1,
            maximum: MAXIMUM_NETWORKS,
        })
    );
}

#[test]
fn a_network_listed_twice_is_refused() {
    assert_eq!(
        IpAccess::new(
            IpAccessMode::AllowList,
            vec![network(1), network(2), network(1)]
        ),
        Err(SiteOptionsError::DuplicateNetwork)
    );
}
