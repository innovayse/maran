//! What the panel is told the host currently allows.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::fake_firewall_host::{
    FakeFirewallHost, open_rule, ports, restricted_rule, ruleset_path,
};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::list_rules::list_rules;

/// A host the installer has not seeded yet has no rules, which is an answer
/// and not a failure.
#[test]
fn a_host_with_no_ruleset_file_has_no_rules() {
    let host = FakeFirewallHost::new();

    assert!(list_rules(&host, &ports()).expect("listed").is_empty());
}

/// Every operator rule is reported, in the order the file holds it.
#[test]
fn every_operator_rule_is_reported_in_file_order() {
    let rules = [
        open_rule(80, NftablesProtocol::Tcp),
        open_rule(443, NftablesProtocol::Tcp),
        restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8"),
    ];
    let host = FakeFirewallHost::with_rules(&rules);

    assert_eq!(list_rules(&host, &ports()).expect("listed"), rules);
}

/// The two unconditional accepts are not rules and are not listed: nothing
/// created them, and `deny_port` cannot take them away.
#[test]
fn the_unconditional_accepts_are_not_listed_as_rules() {
    let host = FakeFirewallHost::with_rules(&[]);

    assert!(list_rules(&host, &ports()).expect("listed").is_empty());
}

/// A file this agent did not write is refused rather than reported as an
/// empty rule set — "there are no rules" and "I cannot read the rules" are
/// different answers, and only one of them invites a panel to write over it.
#[test]
fn a_foreign_ruleset_is_refused_rather_than_reported_as_empty() {
    let host = FakeFirewallHost::new();
    host.put_file(&ruleset_path(), "# the operator's own ruleset\n");

    assert_eq!(
        list_rules(&host, &ports()),
        Err(FirewallError::ForeignRuleset)
    );
}

/// A ruleset file that will not be read is an error, not an empty list.
#[test]
fn a_ruleset_that_cannot_be_read_is_an_error() {
    let host = FakeFirewallHost::with_rules(&[]);
    host.refuse_reads();

    assert_eq!(
        list_rules(&host, &ports()),
        Err(FirewallError::RulesetUnreadable)
    );
}
