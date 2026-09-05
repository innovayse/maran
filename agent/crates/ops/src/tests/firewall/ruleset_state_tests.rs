//! What the rule store accepts as a file it wrote, and what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::nftables::nftables_bans_table::NftablesBansTable;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;

use crate::firewall::ensure_bans_table::{BANNED_V4_SET, BANNED_V6_SET, BANS_TABLE, TABLE_FAMILY};
use crate::firewall::fake_firewall_host::{open_rule, ports, rendered, restricted_rule};
use crate::firewall::firewall_error::FirewallError;
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
            .any(|rule| ports().ssh_ports.contains(&rule.port)),
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
