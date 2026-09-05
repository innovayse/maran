//! What `allow_port` writes, what it refuses to write twice, and the two SSH
//! cases a lockout would come out of.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::slice;

use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::allow_port::allow_port;
use crate::firewall::fake_firewall_host::{
    FakeFirewallHost, distro, open_rule, ports, rendered, restricted_rule, ruleset_path,
};
use crate::firewall::firewall_error::FirewallError;

/// The ruleset file the fake holds now.
fn live(host: &FakeFirewallHost) -> String {
    host.file(&ruleset_path()).expect("a ruleset file")
}

/// An allow that is already installed is reported rather than written again.
#[test]
fn an_identical_allow_reports_already_exists() {
    let rule = restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8");
    let host = FakeFirewallHost::with_rules(slice::from_ref(&rule));
    let before = live(&host);

    let outcome = allow_port(&host, distro(), &ports(), &rule);

    assert_eq!(outcome, Err(FirewallError::AlreadyExists));
    assert_eq!(live(&host), before, "nothing may be rewritten");
    assert!(host.applies().is_empty());
}

/// An allow for the SSH port from every source is already granted by the
/// unconditional fallback, so it is reported rather than recorded.
///
/// It has to be caught by comparing the RENDER rather than the rule list: the
/// rule is not in the list, and adding it would render the byte-identical
/// file. Recording it would create a rule that vanishes on the next read,
/// because the fallback and an any-source SSH rule are the same line.
#[test]
fn an_allow_the_ssh_fallback_already_grants_reports_already_exists() {
    let host = FakeFirewallHost::with_rules(&[]);

    let outcome = allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(22, NftablesProtocol::Tcp),
    );

    assert_eq!(outcome, Err(FirewallError::AlreadyExists));
    assert!(host.applies().is_empty());
}

/// A new allow reaches the file and the kernel.
#[test]
fn a_new_allow_is_rendered_and_loaded() {
    let host = FakeFirewallHost::with_rules(&[]);

    allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(80, NftablesProtocol::Tcp),
    )
    .expect("allowed");

    assert!(live(&host).contains("tcp dport 80 accept"));
    assert_eq!(host.applies(), vec![ruleset_path()]);
}

/// A source-restricted allow renders the family keyword its network needs.
#[test]
fn a_source_restricted_allow_renders_its_address_family() {
    let host = FakeFirewallHost::with_rules(&[]);

    allow_port(
        &host,
        distro(),
        &ports(),
        &restricted_rule(5432, NftablesProtocol::Tcp, "2001:db8::/32"),
    )
    .expect("allowed");

    assert!(live(&host).contains("tcp dport 5432 ip6 saddr 2001:db8::/32 accept"));
}

/// An operator's own TCP rule for the SSH port replaces the unconditional
/// accept, which is what lets SSH be source-restricted at all (R2).
#[test]
fn a_tcp_rule_for_the_ssh_port_displaces_the_fallback() {
    let host = FakeFirewallHost::with_rules(&[]);

    allow_port(
        &host,
        distro(),
        &ports(),
        &restricted_rule(22, NftablesProtocol::Tcp, "203.0.113.0/24"),
    )
    .expect("allowed");

    let text = live(&host);
    assert!(text.contains("tcp dport 22 ip saddr 203.0.113.0/24 accept"));
    assert!(
        !text
            .lines()
            .any(|line| line.trim() == "tcp dport 22 accept"),
        "the accept-from-anywhere fallback must be gone: {text}"
    );
}

/// A UDP rule for the SSH port's NUMBER is an ordinary allow and leaves the
/// fallback alone.
///
/// This is a reviewed lockout hole, pinned. SSH is TCP: a UDP rule that took
/// the fallback's place would close the TCP port the operator is connected
/// on, from a request that never mentioned TCP.
#[test]
fn a_udp_rule_for_the_ssh_port_does_not_displace_the_fallback() {
    let host = FakeFirewallHost::with_rules(&[]);

    allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(22, NftablesProtocol::Udp),
    )
    .expect("allowed");

    let text = live(&host);
    assert!(
        text.lines()
            .any(|line| line.trim() == "tcp dport 22 accept"),
        "the ssh fallback must survive a udp rule for the same number: {text}"
    );
    assert!(text.contains("udp dport 22 accept"));
}

/// A file this agent did not write is never overwritten, whatever is asked
/// of it.
#[test]
fn a_foreign_ruleset_is_never_overwritten() {
    let host = FakeFirewallHost::new();
    host.put_file(
        &ruleset_path(),
        "# the operator's own ruleset\ntable inet maran {\n}\n",
    );

    let outcome = allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(80, NftablesProtocol::Tcp),
    );

    assert_eq!(outcome, Err(FirewallError::ForeignRuleset));
    assert_eq!(
        live(&host),
        "# the operator's own ruleset\ntable inet maran {\n}\n"
    );
    assert!(host.applies().is_empty());
    assert!(host.steps().is_empty(), "nothing may even be staged");
}

/// A rule `nft` refuses leaves the live ruleset exactly as it was.
#[test]
fn a_rule_nft_refuses_leaves_the_live_ruleset_untouched() {
    let host = FakeFirewallHost::with_rules(&[]);
    let before = live(&host);
    host.refuse_check_with("Error: Could not process rule: Operation not supported");

    let outcome = allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(9999, NftablesProtocol::Tcp),
    );

    assert_eq!(
        outcome,
        Err(FirewallError::RuleRefusedByNft {
            stderr: String::from("Error: Could not process rule: Operation not supported"),
        })
    );
    assert_eq!(live(&host), before);
    assert!(host.applies().is_empty());
}

/// A host with no ruleset file yet gets one, rather than an error.
#[test]
fn an_allow_on_a_host_with_no_ruleset_file_writes_one() {
    let host = FakeFirewallHost::new();

    allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(443, NftablesProtocol::Tcp),
    )
    .expect("allowed");

    assert_eq!(
        live(&host),
        rendered(&[open_rule(443, NftablesProtocol::Tcp)])
    );
}

/// A ruleset file that is there and will not be read stops the operation
/// rather than being replaced.
#[test]
fn a_ruleset_that_cannot_be_read_is_not_replaced() {
    let host = FakeFirewallHost::with_rules(&[]);
    host.refuse_reads();

    let outcome = allow_port(
        &host,
        distro(),
        &ports(),
        &open_rule(80, NftablesProtocol::Tcp),
    );

    assert_eq!(outcome, Err(FirewallError::RulesetUnreadable));
    assert!(host.applies().is_empty());
}
