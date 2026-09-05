//! What `deny_port` removes, what it refuses, and where it deliberately
//! fails open.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::slice;

use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::deny_port::deny_port;
use crate::firewall::fake_firewall_host::{
    FakeFirewallHost, distro, open_rule, ports, rendered, restricted_rule, ruleset_path,
};
use crate::firewall::firewall_error::FirewallError;

/// The ruleset file the fake holds now.
fn live(host: &FakeFirewallHost) -> String {
    host.file(&ruleset_path()).expect("a ruleset file")
}

/// Denying a rule nobody installed is the idempotent answer, not a failure.
#[test]
fn a_deny_for_an_absent_rule_reports_not_found() {
    let host = FakeFirewallHost::with_rules(&[open_rule(80, NftablesProtocol::Tcp)]);
    let before = live(&host);

    let outcome = deny_port(
        &host,
        distro(),
        &ports(),
        &open_rule(3306, NftablesProtocol::Tcp),
    );

    assert_eq!(outcome, Err(FirewallError::NotFound));
    assert_eq!(live(&host), before);
    assert!(host.applies().is_empty());
}

/// Denying a rule that differs only in its source network is NotFound: a
/// source-restricted allow and an open one are different rules.
#[test]
fn a_deny_for_a_different_source_reports_not_found() {
    let host =
        FakeFirewallHost::with_rules(&[restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8")]);

    let outcome = deny_port(
        &host,
        distro(),
        &ports(),
        &open_rule(3306, NftablesProtocol::Tcp),
    );

    assert_eq!(outcome, Err(FirewallError::NotFound));
}

/// A denied rule is gone from the rendered file, and the file is loaded.
///
/// The rendered file is what makes the removal real: `nft -f` is additive, so
/// a file that merely omitted the rule would leave it live and duplicate the
/// rest. What removes it is the replace idiom at the head of the file, which
/// the render always emits and the parser refuses to read a ruleset without.
#[test]
fn a_denied_rule_is_gone_from_the_rendered_file() {
    let kept = open_rule(80, NftablesProtocol::Tcp);
    let denied = restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8");
    let host = FakeFirewallHost::with_rules(&[kept.clone(), denied.clone()]);

    deny_port(&host, distro(), &ports(), &denied).expect("denied");

    let text = live(&host);
    assert!(
        !text.contains("3306"),
        "the denied rule must be gone: {text}"
    );
    assert!(text.contains("tcp dport 80 accept"));
    assert_eq!(text, rendered(&[kept]));
    assert_eq!(host.applies(), vec![ruleset_path()]);
}

/// An allow followed by its deny leaves the byte-identical file the host
/// started with — which is the property the whole store is built on.
#[test]
fn an_allow_and_its_deny_converge_on_the_original_file() {
    let rule = open_rule(8080, NftablesProtocol::Udp);
    let host = FakeFirewallHost::with_rules(slice::from_ref(&rule));

    deny_port(&host, distro(), &ports(), &rule).expect("denied");

    assert_eq!(live(&host), rendered(&[]));
}

/// Denying the last TCP rule for the SSH port succeeds and brings the
/// unconditional accept back.
///
/// Fail-open for SSH, by design (R2): a firewall change must not be able to
/// lock an operator out of the host with no way back in. The rule really is
/// gone; what returns is the fallback.
#[test]
fn denying_the_last_ssh_rule_returns_the_fallback() {
    let rule = restricted_rule(22, NftablesProtocol::Tcp, "203.0.113.0/24");
    let host = FakeFirewallHost::with_rules(slice::from_ref(&rule));

    deny_port(&host, distro(), &ports(), &rule).expect("denied");

    let text = live(&host);
    assert!(
        !text.contains("203.0.113.0/24"),
        "the rule must be gone: {text}"
    );
    assert!(
        text.lines()
            .any(|line| line.trim() == "tcp dport 22 accept"),
        "the ssh fallback must return: {text}"
    );
}

/// A file this agent did not write is never overwritten by a deny either.
#[test]
fn a_deny_never_overwrites_a_foreign_ruleset() {
    let host = FakeFirewallHost::new();
    host.put_file(&ruleset_path(), "# somebody else's ruleset\n");

    let outcome = deny_port(
        &host,
        distro(),
        &ports(),
        &open_rule(80, NftablesProtocol::Tcp),
    );

    assert_eq!(outcome, Err(FirewallError::ForeignRuleset));
    assert_eq!(live(&host), "# somebody else's ruleset\n");
}
