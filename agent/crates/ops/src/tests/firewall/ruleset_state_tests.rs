//! What the rule store accepts as a file it wrote, and what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;
use std::slice;

use maran_templates::nftables::nftables_bans_table::NftablesBansTable;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::ensure_bans_table::{BANNED_V4_SET, BANNED_V6_SET, BANS_TABLE, TABLE_FAMILY};
use crate::firewall::fake_firewall_host::{
    open_range, open_rule, policy, ports, rendered, restricted_rule,
};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::output_policy::OutputPolicy;
use crate::firewall::model::ruleset_state::RulesetState;

/// The lines of `text` that say something, the way the parser reads them.
fn effective(text: &str) -> Vec<&str> {
    text.lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .collect()
}

/// The rendered ruleset with `line` taken out of it.
fn without_line(text: &str, line: &str) -> String {
    let kept: Vec<&str> = text
        .lines()
        .filter(|candidate| candidate.trim() != line)
        .collect();

    format!("{}\n", kept.join("\n"))
}

/// The rendered file starts with the replace idiom, and a file without it is
/// refused.
///
/// This is the unit-level tripwire for the whole class of bug this area was
/// re-designed around. `nft -f` is ADDITIVE: measured on nftables v1.0.9,
/// re-applying a re-rendered ruleset with the 3306 rule removed leaves the
/// rule LIVE and duplicates every other one unless the file deletes its own
/// table first. Both halves are asserted here — that the render still emits
/// the idiom, and that the parser still refuses a ruleset whose idiom has been
/// replaced by something else — because either alone would let the other
/// regress silently.
///
/// The second half swaps `delete` for `flush` rather than deleting the two
/// lines, and the choice is what makes this test able to fail on its own. A
/// ruleset with the lines REMOVED shifts every later line, so the chain
/// preamble comparison refuses it whether or not the idiom is checked at all —
/// the two checks would mask each other and this one would be decoration
/// (`a_ruleset_missing_only_the_delete_is_foreign` covers that shape). Keeping
/// the line count means only the idiom check can see the difference. `flush`
/// is also the realistic regression: it looks like the same intent and it is
/// not, since it leaves the table's SETS and their elements in place.
#[test]
fn the_rendered_file_opens_with_the_replace_idiom() {
    let text = rendered(&[]);

    let lines = effective(&text);
    assert_eq!(
        &lines[..3],
        [
            "table inet maran {}",
            "delete table inet maran",
            "table inet maran {"
        ]
        .as_slice(),
        "the rendered ruleset must open with create-if-absent, delete, redeclare"
    );

    let flushed = text.replace("delete table inet maran", "flush table inet maran");
    assert_eq!(
        RulesetState::parse(&flushed, &ports()),
        Err(FirewallError::ForeignRuleset),
        "a ruleset whose replace idiom has been swapped for something else must never be read back"
    );
}

/// Losing only the delete is refused too — the idiom is three lines, not one.
#[test]
fn a_ruleset_missing_only_the_delete_is_foreign() {
    let text = without_line(&rendered(&[]), "delete table inet maran");

    assert_eq!(
        RulesetState::parse(&text, &ports()),
        Err(FirewallError::ForeignRuleset)
    );
}

/// A file with somebody else's first line is never read as a rule store.
#[test]
fn a_ruleset_this_agent_did_not_write_is_foreign() {
    let text = "# hand written by the operator\ntable inet maran {\n}\n";

    assert_eq!(
        RulesetState::parse(text, &ports()),
        Err(FirewallError::ForeignRuleset)
    );
}

/// The chain preamble is checked independently of the idiom: editing the
/// policy is refused even though the idiom is untouched.
///
/// The two checks are separate on purpose, so that neither masks the other —
/// this one fails with the idiom fully intact.
#[test]
fn a_ruleset_whose_chain_policy_was_edited_is_foreign() {
    let text = rendered(&[]).replace("policy drop", "policy accept");

    assert_eq!(
        RulesetState::parse(&text, &ports()),
        Err(FirewallError::ForeignRuleset)
    );
}

/// A rule line the template never renders is refused rather than guessed at.
#[test]
fn a_ruleset_with_an_unrecognised_rule_line_is_foreign() {
    let text = rendered(&[]).replace("tcp dport 8443 accept", "tcp dport 8443 counter accept");

    assert_eq!(
        RulesetState::parse(&text, &ports()),
        Err(FirewallError::ForeignRuleset)
    );
}

/// A port with a leading zero is refused, as its neighbouring `SourceCidr` is.
///
/// `08443` and `8443` are two spellings of one number, and a rule with two
/// spellings is a rule that can be added under one and left behind under the
/// other — which is exactly why `SourceCidr`, parsed three tokens later in the
/// same line, refuses a leading-zero octet. One rule, one answer.
#[test]
fn a_port_with_a_leading_zero_is_foreign() {
    let text = rendered(&[]).replace("tcp dport 8443 accept", "tcp dport 08443 accept");

    assert_eq!(
        RulesetState::parse(&text, &ports()),
        Err(FirewallError::ForeignRuleset)
    );
}

/// A port with a leading `+` is refused, for the same reason a leading zero
/// is.
///
/// `u32::from_str` accepts `+8443`, so without the digit check `+8443` and
/// `8443` would be two accepted spellings of one port — the ambiguity the
/// leading-zero refusal exists to prevent, arriving through a different
/// character.
#[test]
fn a_port_with_a_leading_plus_is_foreign() {
    let text = rendered(&[]).replace("tcp dport 8443 accept", "tcp dport +8443 accept");

    assert_eq!(
        RulesetState::parse(&text, &ports()),
        Err(FirewallError::ForeignRuleset)
    );
}

/// A host with no operator rules reports none, and its file still parses.
#[test]
fn a_ruleset_with_no_operator_rules_reports_none() {
    let state = RulesetState::parse(&rendered(&[]), &ports()).expect("our own render parses");

    assert!(state.rules().is_empty());
}

/// The unconditional SSH accept is a property of the ruleset, not a rule
/// somebody added: it is not reported, so the panel cannot offer a delete
/// button for something `deny_port` would render straight back.
#[test]
fn the_unconditional_ssh_accept_is_not_reported_as_a_rule() {
    let state = RulesetState::parse(&rendered(&[]), &ports()).expect("our own render parses");

    assert!(
        !state
            .rules()
            .iter()
            .any(|rule| ports().ssh_ports.contains(&rule.ports.lower())),
        "the ssh fallback must not be reported as an operator rule"
    );
}

/// Every rule survives a render and a parse unchanged, in order — which is
/// what makes an allow followed by its deny converge on the file the host
/// started with.
#[test]
fn rules_round_trip_through_render_and_parse() {
    let rules = [
        restricted_rule(22, NftablesProtocol::Tcp, "203.0.113.0/24"),
        open_rule(80, NftablesProtocol::Tcp),
        open_rule(443, NftablesProtocol::Udp),
        restricted_rule(5432, NftablesProtocol::Tcp, "2001:db8::/32"),
        restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8"),
    ];

    let state = RulesetState::parse(&rendered(&rules), &ports()).expect("our own render parses");

    assert_eq!(state.rules(), rules.as_slice());
}

/// The state's own arithmetic: adding a rule and removing it again leaves the
/// state it started from.
#[test]
fn adding_and_removing_a_rule_leaves_the_state_unchanged() {
    let rule = open_rule(8080, NftablesProtocol::Tcp);
    let start = RulesetState::empty();

    let widened = start.with(&rule);

    assert!(widened.contains(&rule));
    assert_eq!(widened.without(&rule), start);
}

/// The table and set names this area addresses are the ones the bans template
/// declares.
///
/// Nothing in the type system ties the two together — the constants are
/// strings in `ops` and the declarations are text in a template — so a rename
/// on either side would otherwise produce an agent that loads a table it then
/// cannot address, and every ban would fail on a host that looks healthy.
#[test]
fn the_bans_table_and_set_names_match_the_template() {
    let text = NftablesBansTable {}
        .render_config()
        .expect("the bans table renders");

    for declared in [
        format!("table {TABLE_FAMILY} {BANS_TABLE}"),
        format!("set {BANNED_V4_SET}"),
        format!("set {BANNED_V6_SET}"),
    ] {
        assert!(
            text.contains(&declared),
            "the bans template must declare `{declared}`"
        );
    }
}

/// A range survives the round trip through the file with both bounds intact.
///
/// The rule store IS the rendered file, so a bound that is written and not read
/// back is a rule that changes meaning on the next mutation — the range would
/// come back as a single port and be re-rendered as one.
#[test]
fn a_range_is_read_back_out_of_the_file_with_both_of_its_bounds() {
    let rule = open_range(30_000, 30_099, NftablesProtocol::Tcp);
    let text = rendered(slice::from_ref(&rule));

    let state = RulesetState::parse(&text, &ports()).expect("our own render parses");

    assert_eq!(state.rules(), &[rule]);
    assert_eq!(
        state.render(&ports(), &policy()).unwrap(),
        text,
        "parse and render are inverses"
    );
}

/// A range whose lower bound is an SSH port is an ordinary rule, not that
/// port's block — so it is reported as a rule AND the fallback is still not.
#[test]
fn a_range_that_starts_at_an_ssh_port_is_a_rule_and_the_fallback_is_still_not() {
    let rule = open_range(22, 30, NftablesProtocol::Tcp);
    let text = rendered(slice::from_ref(&rule));

    let state = RulesetState::parse(&text, &ports()).expect("our own render parses");

    assert_eq!(
        state.rules(),
        &[rule],
        "the range is the only rule: the ssh fallback and the panel accept are not rules"
    );
}

/// A policy for a host whose customer range starts at 500 and which answers on
/// one public address of each family.
fn busy_policy() -> OutputPolicy {
    OutputPolicy::new(
        500,
        [
            "203.0.113.7".parse::<IpAddr>().unwrap(),
            "2001:db8::1".parse::<IpAddr>().unwrap(),
        ],
    )
}

/// The render carries the output chain: the uid floor and the host's own
/// addresses reach the file, and the chain only ever rejects port 25.
#[test]
fn the_render_carries_the_output_chain_for_the_hosts_policy() {
    let text = RulesetState::empty()
        .render(&ports(), &busy_policy())
        .unwrap();
    let lines = effective(&text);

    assert!(lines.contains(&"elements = { 127.0.0.0/8, 203.0.113.7 }"));
    assert!(lines.contains(&"elements = { ::1, 2001:db8::1 }"));
    assert!(
        lines.contains(&"meta skuid >= 500 ip daddr @self_v4 tcp dport 25 reject with tcp reset")
    );
    assert!(
        lines.contains(&"meta skuid >= 500 ip6 daddr @self_v6 tcp dport 25 reject with tcp reset")
    );
    // Inverse control: the chain accepts everything it does not name.
    assert!(lines.contains(&"type filter hook output priority 0; policy accept;"));
}

/// Loopback and the unspecified address are never carried by the policy: the
/// template writes loopback itself and an overlap would fail the whole load.
#[test]
fn the_policy_drops_loopback_and_unspecified_and_sorts_the_rest() {
    let policy = OutputPolicy::new(
        1000,
        [
            "10.0.0.9".parse::<IpAddr>().unwrap(),
            "127.0.0.1".parse::<IpAddr>().unwrap(),
            "0.0.0.0".parse::<IpAddr>().unwrap(),
            "::1".parse::<IpAddr>().unwrap(),
            "10.0.0.2".parse::<IpAddr>().unwrap(),
            "10.0.0.9".parse::<IpAddr>().unwrap(),
        ],
    );

    assert_eq!(
        policy
            .self_v4()
            .iter()
            .map(ToString::to_string)
            .collect::<Vec<_>>(),
        ["10.0.0.2", "10.0.0.9"]
    );
    assert!(policy.self_v6().is_empty());
}

/// A file rendered under one policy is still read back after the host's uid
/// floor or addresses changed: they are host facts and not stored rules.
#[test]
fn a_file_rendered_for_other_host_facts_is_still_ours() {
    let rule = restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8");
    let text = RulesetState::empty()
        .with(&rule)
        .render(&ports(), &busy_policy())
        .unwrap();

    let state = RulesetState::parse(&text, &ports()).expect("a changed policy is not foreign");

    assert_eq!(state.rules(), &[rule]);
    // Re-rendering under the host's present policy replaces the old values.
    let again = state.render(&ports(), &policy()).unwrap();
    assert_ne!(again, text);
    assert!(effective(&again).contains(&"elements = { 127.0.0.0/8 }"));
}

/// An output chain that is not the template's is a file this agent did not
/// write; the removed chain and the changed port are each refused alone.
#[test]
fn an_edited_or_missing_output_chain_is_foreign() {
    let text = rendered(&[]);

    let other_port = text.replace("tcp dport 25 reject", "tcp dport 26 reject");
    assert_eq!(
        RulesetState::parse(&other_port, &ports()),
        Err(FirewallError::ForeignRuleset)
    );

    let accepting = text.replace("reject with tcp reset", "accept");
    assert_eq!(
        RulesetState::parse(&accepting, &ports()),
        Err(FirewallError::ForeignRuleset)
    );

    let no_rule = without_line(
        &text,
        "meta skuid >= 1000 ip6 daddr @self_v6 tcp dport 25 reject with tcp reset",
    );
    assert_eq!(
        RulesetState::parse(&no_rule, &ports()),
        Err(FirewallError::ForeignRuleset)
    );

    // Accepting control: the untouched render still parses.
    assert!(RulesetState::parse(&text, &ports()).is_ok());
}

/// A file rendered before the output chain existed is still read, and is
/// re-rendered with the chain by the next mutation.
#[test]
fn a_file_from_before_the_output_chain_is_read_and_upgraded_on_render() {
    let rule = restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8");
    let current = rendered(slice::from_ref(&rule));
    let cut = current
        .find("\n    # Every address this host answers on")
        .unwrap();
    let legacy = format!("{}\n}}\n", &current[..cut]);

    let state = RulesetState::parse(&legacy, &ports()).expect("the legacy shape parses");

    assert_eq!(state.rules(), &[rule]);
    assert_eq!(state.render(&ports(), &policy()).unwrap(), current);
}

/// The masked host facts are checked to be what they stand for: a uid floor that
/// is not a number, an operator with nothing after it, and an element list that
/// is not addresses are each a file this agent did not write.
#[test]
fn a_masked_host_fact_that_is_not_the_fact_it_stands_for_is_foreign() {
    let text = rendered(&[]);
    let skuid = "meta skuid >= 1000 ip daddr @self_v4 tcp dport 25 reject with tcp reset";

    for bad_floor in ["4294967296", "-1", "1e3", "any", "1000;accept", ""] {
        let edited = text.replace(skuid, &skuid.replace("1000", bad_floor));
        assert_eq!(
            RulesetState::parse(&edited, &ports()),
            Err(FirewallError::ForeignRuleset),
            "floor {bad_floor:?}"
        );
    }

    for bad_elements in [
        "elements = { }",
        "elements = { 127.0.0.0/8, evil }",
        "elements = { 127.0.0.0/8 }; add rule inet maran output accept",
        "elements = { 127.0.0.0/33 }",
        "elements = { 127.0.0.0/ }",
        "elements = 127.0.0.0/8",
    ] {
        let edited = text.replace("elements = { 127.0.0.0/8 }", bad_elements);
        assert_eq!(
            RulesetState::parse(&edited, &ports()),
            Err(FirewallError::ForeignRuleset),
            "{bad_elements:?}"
        );
    }

    // Accepting controls: a different but well-formed floor, networks and a v6 list.
    let edited = text
        .replace(skuid, &skuid.replace("1000", "4294967295"))
        .replace(
            "elements = { 127.0.0.0/8 }",
            "elements = { 127.0.0.0/8, 10.0.0.0/8, 203.0.113.7 }",
        )
        .replace("elements = { ::1 }", "elements = { ::1, 2001:db8::/32 }");
    assert!(RulesetState::parse(&edited, &ports()).is_ok());
}

/// A file from before the output chain, or rendered for other host facts, is
/// brought up to date without the ports: the rules stay byte for byte.
#[test]
fn the_tail_is_refreshed_for_the_present_host_without_touching_the_rules() {
    let rule = restricted_rule(3306, NftablesProtocol::Tcp, "10.0.0.0/8");
    let current = rendered(slice::from_ref(&rule));
    let cut = current
        .find("\n    # Every address this host answers on")
        .unwrap();
    let legacy = format!("{}\n}}\n", &current[..cut]);

    let upgraded = RulesetState::with_current_host_facts(&legacy, &policy()).unwrap();

    assert_eq!(upgraded, current);
    assert!(upgraded.contains("chain output"));

    // A file already current is returned unchanged (the reconcile compares).
    assert_eq!(
        RulesetState::with_current_host_facts(&current, &policy()).unwrap(),
        current
    );

    // Moved host facts reach the file; the rule is still the only rule.
    let moved = RulesetState::with_current_host_facts(&current, &busy_policy()).unwrap();
    assert!(effective(&moved).contains(&"elements = { 127.0.0.0/8, 203.0.113.7 }"));
    assert_eq!(
        RulesetState::parse(&moved, &ports()).unwrap().rules(),
        slice::from_ref(&rule)
    );
}

/// Inverse control: a file this agent did not write is never refreshed.
#[test]
fn a_foreign_file_is_not_refreshed() {
    let text = rendered(&[]);

    for foreign in [
        "table inet other {}\n".to_owned(),
        text.replace("policy drop", "policy accept"),
        text.replace("tcp dport 25 reject", "tcp dport 26 reject"),
    ] {
        assert_eq!(
            RulesetState::with_current_host_facts(&foreign, &policy()),
            Err(FirewallError::ForeignRuleset)
        );
    }
}

/// Issue #535: a routed block is a range of the IPv4 set, and a file carrying one
/// is still a file this agent reads back (the element list may hold networks).
#[test]
fn a_local_range_renders_into_the_ipv4_set_and_the_file_reads_back() {
    let host = maran_agent_core::utils::host_addresses::HostAddresses {
        addresses: vec![
            "203.0.113.7".parse::<IpAddr>().unwrap(),
            "198.51.100.9".parse::<IpAddr>().unwrap(),
        ],
        local_prefixes: vec![
            maran_agent_core::utils::ipv4_prefix::Ipv4Prefix::new(
                "198.51.100.0".parse().unwrap(),
                24,
            )
            .unwrap(),
        ],
    };
    let policy = OutputPolicy::for_host(500, &host);

    let text = RulesetState::empty().render(&ports(), &policy).unwrap();

    assert!(effective(&text).contains(&"elements = { 127.0.0.0/8, 198.51.100.0/24, 203.0.113.7 }"));
    assert_eq!(
        RulesetState::parse(&text, &ports()).unwrap(),
        RulesetState::empty()
    );
}
