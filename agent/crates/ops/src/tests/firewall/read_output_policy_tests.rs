//! The host facts the output chain is rendered from, read through the host seam.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::host_addresses_error::HostAddressesError;

use crate::firewall::allow_port::allow_port;
use crate::firewall::fake_firewall_host::{
    FakeFirewallHost, distro, open_rule, ports, ruleset_path,
};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::read_output_policy::read_output_policy;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;

const LOGIN_DEFS: &str = "/etc/login.defs";
const FIB_TRIE: &str = "/proc/net/fib_trie";
const IF_INET6: &str = "/proc/net/if_inet6";

/// The floor comes from the host's `login.defs`, and the addresses from the two
/// kernel tables.
#[test]
fn the_floor_and_the_addresses_are_read_from_the_host() {
    let host = FakeFirewallHost::new();
    host.put_file(LOGIN_DEFS, "UID_MIN 500\n");
    host.put_file(
        FIB_TRIE,
        "Local:\n     |-- 203.0.113.7\n        /32 host LOCAL\n     |-- 127.0.0.1\n        /32 host LOCAL\n",
    );
    host.put_file(
        IF_INET6,
        "20010db8000000000000000000000001 02 40 00 80 eth0\n",
    );

    let policy = read_output_policy(&host, distro()).unwrap();

    assert_eq!(policy.customer_uid_min(), 500);
    assert_eq!(policy.self_v4().len(), 1, "loopback is the template's");
    assert_eq!(policy.self_v4()[0].to_string(), "203.0.113.7");
    assert_eq!(policy.self_v6()[0].to_string(), "2001:db8::1");
}

/// Inverse control: a host whose `login.defs` does not set the floor, and whose
/// tables are absent, gets the documented default and loopback only.
#[test]
fn a_host_that_sets_nothing_gets_the_default_floor_and_no_extra_addresses() {
    let host = FakeFirewallHost::new();
    host.put_file(LOGIN_DEFS, "# nothing here\n");

    let policy = read_output_policy(&host, distro()).unwrap();

    assert_eq!(policy.customer_uid_min(), 1000);
    assert!(policy.self_v4().is_empty());
    assert!(policy.self_v6().is_empty());
}

/// A host whose IPv4 address table is missing is refused: every host has a
/// loopback address, so rendering the output chain without the host's addresses
/// would look applied while matching nothing, where the mail layer refuses.
#[test]
fn a_missing_ipv4_table_is_refused_not_read_as_no_addresses() {
    let host = FakeFirewallHost::new();
    host.remove_file(FIB_TRIE);

    assert_eq!(
        read_output_policy(&host, distro()),
        Err(FirewallError::HostAddressesUnreadable(
            HostAddressesError::Ipv4Missing
        ))
    );
}

/// Inverse control: a missing IPv6 table is IPv6 turned off and is not a fault.
#[test]
fn a_missing_ipv6_table_is_ipv6_turned_off() {
    let host = FakeFirewallHost::new();

    assert!(
        read_output_policy(&host, distro())
            .unwrap()
            .self_v6()
            .is_empty()
    );
}

/// A table over the bound is refused here as it is by the mail layer's reader.
#[test]
fn a_table_over_the_bound_is_refused() {
    let host = FakeFirewallHost::new();
    host.put_file(IF_INET6, &"x".repeat(16 * 1024 * 1024 + 1));

    assert_eq!(
        read_output_policy(&host, distro()),
        Err(FirewallError::HostAddressesUnreadable(
            HostAddressesError::Ipv6OverBound
        ))
    );
}

/// A floor that is present and unusable is refused, never guessed.
#[test]
fn an_unusable_floor_is_refused() {
    let host = FakeFirewallHost::new();
    host.put_file(LOGIN_DEFS, "UID_MIN 0\n");

    assert_eq!(
        read_output_policy(&host, distro()),
        Err(FirewallError::CustomerUidFloorUnusable)
    );
}

/// The applied file carries the host's floor: an apply restates the host facts,
/// and the output chain only ever names port 25.
#[test]
fn an_apply_writes_the_hosts_floor_into_the_output_chain() {
    let host = FakeFirewallHost::with_rules(&[]);
    host.put_file(LOGIN_DEFS, "UID_MIN 700\n");

    allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(8080, NftablesProtocol::Tcp),
    )
    .unwrap();

    let live = host.file(&ruleset_path()).expect("a ruleset file");
    let skuid_lines: Vec<&str> = live.lines().filter(|line| line.contains("skuid")).collect();
    assert_eq!(skuid_lines.len(), 2);
    assert!(
        skuid_lines
            .iter()
            .all(|line| line.contains(">= 700") && line.contains("tcp dport 25 reject")),
        "{skuid_lines:?}"
    );
}
