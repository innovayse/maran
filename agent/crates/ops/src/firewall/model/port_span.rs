//! What one firewall rule opens: a single port, or a range of them.

use std::fmt;

use maran_agent_core::validation::web::port::Port;

use crate::firewall::firewall_error::FirewallError;

/// The character `nft` writes between the two bounds of a port range.
const RANGE_SEPARATOR: char = '-';

/// The ports one rule opens: a lower bound, and an optional inclusive upper
/// bound that makes it a range.
///
/// **This is a validated type, and it is the only way a range reaches the
/// rendered ruleset.** The rendered file is a grammar `nft` parses as root, so
/// the pair has to be checked before it is rendered rather than after — and it
/// is checked here, in the type the render is built from, rather than in a
/// handler, so there is no path into a [`FirewallRule`](super::firewall_rule::FirewallRule)
/// that skips the check (rules/rust.md "Validation first").
///
/// What it refuses, and why each refusal is a real failure rather than
/// tidiness:
///
/// - **An inverted pair** (`30099-30000`). `nft -f` refuses it, and because an
///   apply is one transaction, the refusal aborts the WHOLE ruleset load: the
///   host keeps its previous policy and the operator is told only that `nft`
///   said no. Refusing it here names the actual mistake.
/// - **Equal bounds** (`30000-30000`). `nft` accepts that one, which is what
///   makes it the dangerous half: it would be a second spelling of the single
///   port `30000`, and a rule has no identity beyond its own text here — a
///   later deny for `30000` would match nothing and report success while the
///   port stayed open. One value, one spelling, exactly as this area's port
///   parser refuses a leading zero for.
///
/// What it cannot express, which is the stronger half of the argument: an
/// upper bound with **no lower bound**. The lower bound is a [`Port`] this
/// type is constructed from, so "a `port_to` with no `port`" has no
/// representation to check for — on the wire an absent `port` decodes to 0 and
/// `Port::parse` refuses it before this type is reached. A control character
/// or any other byte is equally unrepresentable: both bounds are `u16`s that
/// came through [`Port`], so nothing a caller composes can reach the rendered
/// line (rules/security.md §4).
///
/// **What it deliberately does NOT refuse is a range that spans one of the
/// host's own ports** — an SSH port or the panel port. Opening a wider range
/// than intended cannot lock anybody out: the unconditional accepts are
/// rendered from [`RulesetPorts`](super::ruleset_ports::RulesetPorts) whatever
/// the operator's rules say, and a range never displaces one, because only a
/// single-port TCP rule is ever routed into an SSH port's block. A refusal
/// there would also have to be re-derived on every listing, since the host's
/// SSH ports can change under a rule that was legal when it was written.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PortSpan {
    /// The port the rule opens, or the lower bound of the range.
    lower: Port,
    /// The inclusive upper bound, or `None` for a single port.
    upper: Option<Port>,
}

impl PortSpan {
    /// The span of exactly one port.
    #[must_use]
    pub fn single(lower: Port) -> Self {
        Self { lower, upper: None }
    }

    /// The span for a lower bound and an optional upper one.
    ///
    /// The one constructor a range can come into existence through, so every
    /// range in this agent has been through the two refusals below.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::InvalidRange`] when `upper` is present and is
    /// not strictly above `lower` — an inverted pair, or a pair whose bounds
    /// are equal. See the type's own documentation for what each of them would
    /// otherwise cost.
    pub fn new(lower: Port, upper: Option<Port>) -> Result<Self, FirewallError> {
        if let Some(upper) = upper
            && upper.value() <= lower.value()
        {
            return Err(FirewallError::InvalidRange);
        }

        Ok(Self { lower, upper })
    }

    /// Reads back the `dport` token of a rendered rule line.
    ///
    /// `None` for anything this agent's template does not render, which the
    /// ruleset parser turns into
    /// [`FirewallError::ForeignRuleset`]. A rendered range that would be
    /// refused on the way in — inverted, or with equal bounds — is refused on
    /// the way out too, so parse and render stay inverses of each other and a
    /// hand-edited file cannot introduce a span this agent would not itself
    /// have written.
    ///
    /// A leading zero and a leading `+` are refused in BOTH bounds, for the
    /// reason this file's parser refused them in a single port: `SourceCidr`,
    /// parsed from the same rule line, refuses a leading-zero octet because a
    /// value with two spellings is a rule that can be added under one and left
    /// behind under the other. `str::parse` accepts `08443` as 8443 and
    /// `+8443` as 8443, so half a rule line would enforce one spelling and the
    /// other would not, and the file would be silently rewritten into the
    /// canonical form on the next mutation. One rule, one answer, on both
    /// bounds of a range.
    #[must_use]
    pub(crate) fn parse_rendered(token: &str) -> Option<Self> {
        let (lower, upper) = match token.split_once(RANGE_SEPARATOR) {
            Some((lower, upper)) => (lower, Some(parse_port(upper)?)),
            None => (token, None),
        };

        Self::new(parse_port(lower)?, upper).ok()
    }

    /// The port the rule opens, or the lower bound of the range.
    #[must_use]
    pub fn lower(&self) -> Port {
        self.lower
    }

    /// The inclusive upper bound, or `None` when the span is a single port.
    #[must_use]
    pub fn upper(&self) -> Option<Port> {
        self.upper
    }
}

impl fmt::Display for PortSpan {
    /// Writes the `dport` argument `nft` expects — `8080`, or `30000-30099`.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{}", self.lower.value())?;

        if let Some(upper) = self.upper {
            write!(formatter, "{RANGE_SEPARATOR}{}", upper.value())?;
        }

        Ok(())
    }
}

/// Reads one rendered port number back.
///
/// A leading zero and a leading `+` are refused; see
/// [`PortSpan::parse_rendered`] for why one spelling per value matters on a
/// line this agent has to be able to re-render byte for byte.
///
/// `"0"` itself needs no special case: it has no leading zero to strip, and
/// [`Port::parse`] refuses it anyway.
fn parse_port(token: &str) -> Option<Port> {
    // Checked by hand rather than left to `u32::from_str`, which accepts a
    // leading `+`. `SourceCidr`'s own prefix parser folds its digits by hand
    // for exactly this reason; this is the same guard on the other half of the
    // line.
    if token.is_empty() || !token.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }

    if token.len() > 1 && token.starts_with('0') {
        return None;
    }

    Port::parse(token.parse::<u32>().ok()?).ok()
}

#[cfg(test)]
#[path = "../../tests/firewall/port_span_tests.rs"]
mod tests;
