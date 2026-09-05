//! Which set a ban lands in, how it is spelled to `nft`, and what a repeated
//! ban does.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;

use crate::firewall::ban_address::ban_address;
use crate::firewall::ensure_bans_table::{BANNED_V4_SET, BANNED_V6_SET};
use crate::firewall::fake_firewall_host::{FakeFirewallHost, bans_path, distro};
use crate::firewall::firewall_error::FirewallError;

/// A validated address.
fn address(text: &str) -> BanAddress {
    BanAddress::parse(text).expect("a valid address")
}

/// A ban goes into the set for its own address family.
///
/// The two sets are typed (`ipv4_addr`, `ipv6_addr`), so an address put in
/// the wrong one matches no packet at all — a ban that silently bans nothing,
/// which is worse than an error because nobody looks for the cause of an
/// attack that was supposedly stopped.
#[test]
fn a_ban_targets_the_family_matching_its_address() {
    let host = FakeFirewallHost::new().with_bans_table();

    ban_address(&host, distro(), &address("198.51.100.7"), None).expect("banned");
    ban_address(&host, distro(), &address("2001:db8::1"), None).expect("banned");

    let elements = host.elements();
    let v4 = elements
        .iter()
        .find(|element| element.address == "198.51.100.7")
        .expect("the v4 ban");
    let v6 = elements
        .iter()
        .find(|element| element.address == "2001:db8::1")
        .expect("the v6 ban");

    assert_eq!(v4.set, BANNED_V4_SET);
    assert_eq!(v6.set, BANNED_V6_SET);
}

/// A ban with a lifetime carries `timeout <n>s`, and each token of the
/// element list is its own argument.
///
/// The braces are arguments rather than part of the address text: `nft` joins
/// its argument vector with spaces and lexes the result in its own grammar,
/// and passing every token separately was verified working against real
/// nftables during review. It is also what keeps the command a vector this
/// agent assembled rather than a string it formatted.
#[test]
fn a_timed_ban_asks_nft_for_a_timeout_in_seconds() {
    let host = FakeFirewallHost::new().with_bans_table();

    ban_address(
        &host,
        distro(),
        &address("198.51.100.7"),
        Some(Duration::from_secs(900)),
    )
    .expect("banned");

    let argv = host
        .nft_call_starting_with("add")
        .expect("nft was asked to add an element");
    assert_eq!(
        argv,
        vec![
            distro().nft_binary().to_owned(),
            String::from("add"),
            String::from("element"),
            String::from("inet"),
            String::from("maran_bans"),
            String::from(BANNED_V4_SET),
            String::from("{"),
            String::from("198.51.100.7"),
            String::from("timeout"),
            String::from("900s"),
            String::from("}"),
        ]
    );
}

/// A permanent ban carries no timeout clause at all.
#[test]
fn a_permanent_ban_carries_no_timeout_clause() {
    let host = FakeFirewallHost::new().with_bans_table();

    ban_address(&host, distro(), &address("198.51.100.7"), None).expect("banned");

    let argv = host
        .nft_call_starting_with("add")
        .expect("nft was asked to add an element");
    assert!(
        !argv.iter().any(|argument| argument == "timeout"),
        "a permanent ban has no timeout: {argv:?}"
    );
    assert_eq!(host.elements().first().expect("the ban").seconds, None);
}

/// A zero lifetime is a permanent ban, because `nft` reads `timeout 0s` as no
/// timeout — writing the clause would say the same thing in a form that reads
/// like a mistake.
#[test]
fn a_zero_lifetime_is_a_permanent_ban() {
    let host = FakeFirewallHost::new().with_bans_table();

    ban_address(
        &host,
        distro(),
        &address("198.51.100.7"),
        Some(Duration::ZERO),
    )
    .expect("banned");

    assert_eq!(host.elements().first().expect("the ban").seconds, None);
}

/// Banning an address that is already banned replaces the element, so the new
/// lifetime is the one in force.
///
/// `nft add element` on an address the set already holds REPLACES it and
/// refreshes the timeout — measured on real nftables v1.0.9, where 900s → 2h
/// extends, 2h → 1m shortens and both conversions between timed and permanent
/// take effect, all exiting 0. The fake models that, so this test now passes
/// for the reason the kernel does rather than for a reason the fake invented.
#[test]
fn banning_twice_extends_rather_than_erroring() {
    let host = FakeFirewallHost::new().with_bans_table();
    let banned = address("198.51.100.7");

    ban_address(&host, distro(), &banned, Some(Duration::from_secs(900))).expect("banned");
    ban_address(&host, distro(), &banned, Some(Duration::from_secs(86_400))).expect("re-banned");

    let elements = host.elements();
    assert_eq!(elements.len(), 1, "one element, not two: {elements:?}");
    assert_eq!(elements.first().expect("the ban").seconds, Some(86_400));
}

/// Re-banning never takes the element out first, so the address is not
/// unbanned for a moment in the middle of being re-banned.
///
/// This is the property that replaced a delete-then-add. That sequence was
/// justified by a belief about `nft` that a live kernel contradicts, and it
/// was not free: between the two spawns the address was genuinely not banned,
/// and the module lock serialises this agent's own callers rather than
/// packets. One `add` does the whole job, so nothing here may issue a delete.
#[test]
fn re_banning_never_removes_the_element_first() {
    let host = FakeFirewallHost::new().with_bans_table();
    let banned = address("198.51.100.7");

    ban_address(&host, distro(), &banned, Some(Duration::from_secs(900))).expect("banned");
    ban_address(&host, distro(), &banned, Some(Duration::from_secs(86_400))).expect("re-banned");

    assert!(
        host.nft_call_starting_with("delete").is_none(),
        "a re-ban must never unban first: {:?}",
        host.steps()
    );
    assert_eq!(
        host.elements().first().expect("the ban").seconds,
        Some(86_400)
    );
}

/// A ban on a host with no bans table loads the table first, then bans.
#[test]
fn a_ban_loads_the_bans_table_when_the_host_has_none() {
    let host = FakeFirewallHost::new();

    ban_address(&host, distro(), &address("198.51.100.7"), None).expect("banned");

    assert_eq!(host.applies(), vec![bans_path()]);
    assert_eq!(host.elements().len(), 1);
}

/// An `nft` that will not run at all is a failure rather than a silent
/// non-ban.
#[test]
fn a_ban_that_nft_cannot_run_is_reported() {
    let host = FakeFirewallHost::new().with_bans_table();
    host.lose_nft();

    let outcome = ban_address(&host, distro(), &address("198.51.100.7"), None);

    assert_eq!(
        outcome,
        Err(FirewallError::NftFailed {
            stderr: String::from("could not run nft"),
        })
    );
}
