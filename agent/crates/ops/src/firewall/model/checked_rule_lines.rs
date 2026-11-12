//! Proves a ruleset file is one this agent rendered.

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::mask_host_facts::mask_host_facts;
use crate::firewall::model::output_policy::OutputPolicy;
use crate::firewall::model::parse_rule::parse_rule;
use crate::firewall::model::render_reference_ruleset::render_reference_ruleset;

/// How many effective lines the replace idiom occupies.
///
/// `table inet maran {}` (a no-op create), `delete table inet maran`, and the
/// real `table inet maran {`. Three lines, and the reason
/// [`RulesetState::parse`](crate::firewall::model::ruleset_state::RulesetState::parse) refuses a file without them is not tidiness:
/// `nft -f` is ADDITIVE. Measured on nftables v1.0.9 — re-applying a
/// re-rendered ruleset with the 3306 rule removed leaves `3306 rules: 0,
/// loopback rules: 1` WITH the idiom and `3306 rules: 1, loopback rules: 2`
/// without it. Without those three lines a deny reports success while the
/// port stays open and every other rule is duplicated, which is the exact bug
/// this ruleset's first design shipped.
const REPLACE_IDIOM_LINES: usize = 3;

/// The tail of a file rendered before the output chain existed: the chain's
/// closing brace and the table's.
///
/// Accepted, once, so that a host upgraded from such a release can still
/// allow and deny ports: the first mutation reads it and re-renders it with the
/// output chain, after which this shape is never written again. Everything
/// before the tail is still compared in full, so it is the old SHAPE that is
/// recognised and not an arbitrary file.
const LEGACY_TAIL: [&str; 2] = ["}", "}"];

/// The uid floor the reference render is asked for. Masked in every comparison.
const REFERENCE_UID_MIN: u32 = 1000;

/// Proves `text` is a file this agent rendered and returns its rule lines.
///
/// The marker, the replace idiom, the chain preamble and the tail are compared
/// with this agent's own render of an empty ruleset, so a template change can never
/// leave the parser accepting the previous shape. The rule lines are the leading
/// run of lines that read as rules.
///
/// # Errors
///
/// [`FirewallError::ForeignRuleset`] for anything that is not the template's own
/// shape; [`FirewallError::RenderFailed`] when the reference cannot be rendered.
pub(crate) fn checked_rule_lines(text: &str) -> Result<Vec<&str>, FirewallError> {
    let reference = reference_render()?;
    // The marker. Compared against the render's own first line rather
    // than against a copy of it kept here, so the two cannot drift: a
    // header edited in the template changes what this parser accepts in
    // the same commit.
    if text.lines().next() != reference.lines().next() {
        return Err(FirewallError::ForeignRuleset);
    }

    let expected = effective_lines(&reference);
    let actual = effective_lines(text);
    let preamble = preamble_length(&expected);

    // The replace idiom, checked on its own and before the rest, because
    // it is the one part of the preamble whose absence is a live firewall
    // bug rather than a formatting difference. See REPLACE_IDIOM_LINES.
    //
    // It is a check of its own and not part of the comparison below, and
    // the two are genuinely independent rather than one masking the other:
    // an idiom swapped for something else of the same length (`flush`
    // instead of `delete`, say) passes the preamble comparison and only
    // this check refuses it, while an edited chain policy passes this one
    // and only the comparison refuses that. Each dies alone under a
    // mutation, which is what a protection has to do to count as one.
    if actual.get(..REPLACE_IDIOM_LINES) != expected.get(..REPLACE_IDIOM_LINES) {
        return Err(FirewallError::ForeignRuleset);
    }

    // The rest of the chain preamble: the chain declaration, the policy,
    // the loopback accept, the connection-tracking rules and the two ICMP
    // accepts. An edit to any of them is a firewall this agent did not
    // write, whatever the first line says.
    if actual.get(REPLACE_IDIOM_LINES..preamble) != expected.get(REPLACE_IDIOM_LINES..preamble) {
        return Err(FirewallError::ForeignRuleset);
    }

    let Some(body) = actual.get(preamble..) else {
        return Err(FirewallError::ForeignRuleset);
    };

    // The rules are the leading run of lines that read as rules; what
    // follows is the tail the template renders after the input chain —
    // its closing brace, the address sets and the output chain, then the
    // table's closing brace. The tail has to be the reference's own, with
    // the two host-dependent values masked (and checked to be of the shape
    // they stand for), so an edited output chain is a file this agent did
    // not write.
    let rule_count = body
        .iter()
        .take_while(|line| parse_rule(line).is_some())
        .count();
    let (rule_lines, tail) = body.split_at(rule_count);

    let expected_rules = expected
        .get(preamble..)
        .unwrap_or_default()
        .iter()
        .take_while(|line| parse_rule(line).is_some())
        .count();
    let expected_tail = expected
        .get(preamble + expected_rules..)
        .unwrap_or_default();
    if tail != LEGACY_TAIL {
        let masked = mask_host_facts(tail).ok_or(FirewallError::ForeignRuleset)?;
        if Some(masked) != mask_host_facts(expected_tail) {
            return Err(FirewallError::ForeignRuleset);
        }
    }

    Ok(rule_lines.to_vec())
}

/// Renders the ruleset this agent produces for a host with no rules at all.
///
/// The accepted file shape is derived from this rather than written out again
/// here, so the parser cannot go on accepting last release's header after the
/// template's changed.
///
/// # Errors
///
/// Returns [`FirewallError::RenderFailed`] when the template fails.
fn reference_render() -> Result<String, FirewallError> {
    // Masked out of every comparison (see `mask_host_facts`), so any host facts
    // serve; the documented default keeps the reference a real render.
    render_reference_ruleset(&OutputPolicy::new(REFERENCE_UID_MIN, []))
}

/// The lines of `text` that say something: trimmed, non-empty, and not a
/// comment.
///
/// Comments are dropped rather than compared because they are the part of the
/// file most likely to be re-worded, and re-wording an explanation must not
/// make every host's ruleset unreadable. The marker line is compared
/// separately and in full, so the file still has to announce itself.
fn effective_lines(text: &str) -> Vec<&str> {
    text.lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .collect()
}

/// How many leading effective lines of a rendered ruleset come before its
/// first rule.
///
/// Found by asking where the first line that parses as a rule is, rather than
/// by counting the preamble's lines here — a count would be a second copy of
/// the template's shape, and a stale one the first time a line is added to
/// the chain.
fn preamble_length(expected: &[&str]) -> usize {
    expected
        .iter()
        .position(|line| parse_rule(line).is_some())
        .unwrap_or(expected.len())
}
