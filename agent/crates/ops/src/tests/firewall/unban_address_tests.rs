//! Lifting a ban, and what an unban for an address nobody banned answers.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::ban_address::BanAddress;

use crate::firewall::ensure_bans_table::BANNED_V4_SET;
use crate::firewall::fake_firewall_host::{FakeFirewallHost, distro};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::unban_address::unban_address;

/// A validated address.
fn address(text: &str) -> BanAddress {
    BanAddress::parse(text).expect("a valid address")
}

/// An unban takes the element out of the set for its family.
#[test]
fn an_unban_removes_the_element() {
    let host = FakeFirewallHost::new().with_bans_table().with_element(
        BANNED_V4_SET,
        "198.51.100.7",
        Some(3600),
    );

    unban_address(&host, distro(), &address("198.51.100.7")).expect("unbanned");

    assert!(host.elements().is_empty());
}

/// An address with no ban in force is the idempotent answer to a repeated
/// unban.
#[test]
fn an_unban_for_an_address_that_is_not_banned_reports_not_found() {
    let host = FakeFirewallHost::new().with_bans_table();

    let outcome = unban_address(&host, distro(), &address("198.51.100.7"));

    assert_eq!(outcome, Err(FirewallError::NotFound));
}

/// A host with no bans table has no bans, so an unban answers NotFound rather
/// than loading a table nobody asked for.
#[test]
fn an_unban_on_a_host_with_no_bans_table_reports_not_found() {
    let host = FakeFirewallHost::new();

    let outcome = unban_address(&host, distro(), &address("198.51.100.7"));

    assert_eq!(outcome, Err(FirewallError::NotFound));
    assert!(
        host.applies().is_empty(),
        "an unban must not write a firewall file"
    );
}

/// An `nft` that will not run at all is a failure, not a NotFound: "there is
/// no such ban" and "I could not find out" are different answers.
#[test]
fn an_unban_that_nft_cannot_run_is_reported() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.lose_nft();

    let outcome = unban_address(&host, distro(), &address("198.51.100.7"));

    assert_eq!(
        outcome,
        Err(FirewallError::NftFailed {
            stderr: String::from("could not run nft"),
        })
    );
}
