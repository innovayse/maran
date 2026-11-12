//! The soft form of the output chain's host facts, used by the installer's seed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::firewall::fake_firewall_host::{FakeFirewallHost, distro};
use crate::firewall::model::output_policy::OutputPolicy;
use crate::firewall::read_output_policy_or_inert::read_output_policy_or_inert;

const LOGIN_DEFS: &str = "/etc/login.defs";
const FIB_TRIE: &str = "/proc/net/fib_trie";

/// An unreadable IPv4 table (an unsupported kernel, no procfs) does not stop the seed.
#[test]
fn an_unreadable_address_table_gives_the_inert_policy() {
    let host = FakeFirewallHost::new();
    host.remove_file(FIB_TRIE);

    let policy = read_output_policy_or_inert(&host, distro());

    assert_eq!(policy, OutputPolicy::inert());
    assert!(policy.self_v4().is_empty());
    assert!(policy.self_v6().is_empty());
}

/// An unusable `UID_MIN` does not stop the seed either.
#[test]
fn an_unusable_uid_floor_gives_the_inert_policy() {
    let host = FakeFirewallHost::new();
    host.put_file(LOGIN_DEFS, "UID_MIN banana\n");

    assert_eq!(
        read_output_policy_or_inert(&host, distro()),
        OutputPolicy::inert()
    );
}

/// Inverse control: readable facts are used as they are, not replaced by the inert policy.
#[test]
fn readable_facts_are_used() {
    let host = FakeFirewallHost::new();
    host.put_file(LOGIN_DEFS, "UID_MIN 500\n");
    host.put_file(
        FIB_TRIE,
        "Local:\n     |-- 203.0.113.7\n        /32 host LOCAL\n",
    );

    let policy = read_output_policy_or_inert(&host, distro());

    assert_eq!(policy.customer_uid_min(), 500);
    assert_eq!(policy.self_v4()[0].to_string(), "203.0.113.7");
}
