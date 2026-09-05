//! The agent-managed rule set, read back out of the file it was rendered to.

use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_templates::nftables::nftables_protocol::NftablesProtocol;
use maran_templates::nftables::nftables_ruleset::NftablesRuleset;
use maran_templates::nftables::nftables_ssh_port::NftablesSshPort;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::ruleset_ports::RulesetPorts;

/// How many effective lines the replace idiom occupies.
///
/// `table inet maran {}` (a no-op create), `delete table inet maran`, and the
/// real `table inet maran {`. Three lines, and the reason
/// [`RulesetState::parse`] refuses a file without them is not tidiness:
/// `nft -f` is ADDITIVE. Measured on nftables v1.0.9 — re-applying a
/// re-rendered ruleset with the 3306 rule removed leaves `3306 rules: 0,
/// loopback rules: 1` WITH the idiom and `3306 rules: 1, loopback rules: 2`
/// without it. Without those three lines a deny reports success while the
/// port stays open and every other rule is duplicated, which is the exact bug
/// this ruleset's first design shipped.
const REPLACE_IDIOM_LINES: usize = 3;

/// How many effective lines close the rendered file — `}` for the chain and
/// `}` for the table.
const CLOSING_LINES: usize = 2;

/// The fewest rule lines a ruleset this agent rendered can hold.
///
/// The SSH accept (the unconditional fallback, or the operator's own rules in
/// its place) and the panel port's accept. Both are unconditional, so a
/// rendered ruleset never has fewer.
const MINIMUM_RULE_LINES: usize = 2;

/// The SSH port the reference render is asked for.
///
/// Nothing read out of the reference depends on it: the lines taken from it
/// are the marker, the replace idiom, the chain preamble and the two closing
/// braces, and not one of them mentions a port. It differs from
/// [`REFERENCE_PANEL_PORT`] so that the two unconditional rule lines cannot
/// coincide, which is what keeps the cut between "preamble" and "rules"
/// unambiguous.
const REFERENCE_SSH_PORT: u16 = 1;

/// The panel port the reference render is asked for. See
/// [`REFERENCE_SSH_PORT`].
const REFERENCE_PANEL_PORT: u16 = 2;

/// The keyword `nft` expects between a protocol and a port number.
const DPORT: &str = "dport";

/// The verdict every rule this agent renders ends in.
const ACCEPT: &str = "accept";

/// The keyword `nft` expects before a source network.
const SADDR: &str = "saddr";

/// Token count of a rule line open to every source: `tcp dport 80 accept`.
const OPEN_RULE_TOKENS: usize = 4;

/// Token count of a source-restricted rule line:
/// `tcp dport 3306 ip saddr 10.0.0.0/8 accept`.
const RESTRICTED_RULE_TOKENS: usize = 7;

/// The rules the agent manages in `table inet maran`, in the order the file
/// holds them.
///
/// **This type is the rule store.** There is no database of firewall rules in
/// the agent and no second copy of one: the rendered file at
/// `AgentPaths::nftables_ruleset_path()` IS the store, this type is how it is
/// read, and [`RulesetState::render`] is how it is written back. Parse and
/// render are inverses, which is what makes an allow followed by a deny
/// converge on the byte-identical file the host started with.
///
/// **Only a file this agent rendered is ever read back.** The marker line,
/// the replace idiom and the chain preamble are all compared against what
/// this agent's own template renders right now, so a template change can
/// never leave the parser accepting the previous shape, and a file written by
/// anybody else is [`FirewallError::ForeignRuleset`] with nothing
/// overwritten.
///
/// **What that guard does NOT preserve is comments.** An operator's `#` note
/// inside the ruleset file is dropped on the way in and not written back on
/// the way out, so the next mutation removes it. That is declared rather than
/// hidden — the rendered header's first line says hand edits are overwritten
/// on the next apply — but it is worth stating beside the guard, because
/// "a file this agent did not write is never overwritten" invites the reading
/// that everything an operator puts in a file it DID write survives. An
/// operator's added RULE does survive, as long as it is one this agent could
/// itself have rendered: it is adopted as a managed rule and re-rendered. The
/// note explaining why they added it is not.
///
/// **The unconditional SSH accept is not a rule.** A lone bare
/// `tcp dport <ssh port> accept` in the SSH block is the fallback the template
/// renders when the operator has authored no TCP rule for that port, so it
/// contributes no [`FirewallRule`] here — otherwise every host would report a
/// rule nobody created, and denying it would report success while the
/// template rendered it straight back (R2: removing the last TCP ssh-port
/// rule returns the fallback, fail-open for SSH, by design).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RulesetState {
    /// The operator-managed rules, in the order the file holds them: the SSH
    /// block first, then everything else.
    rules: Vec<FirewallRule>,
}

impl RulesetState {
    /// The state of a host whose ruleset file has never been written.
    ///
    /// No rules, which renders as the unconditional SSH and panel accepts and
    /// nothing else. An absent file is an ordinary state — it is what every
    /// host is in before the installer seeds it — so it is a value here and
    /// not an error.
    #[must_use]
    pub fn empty() -> Self {
        Self { rules: Vec::new() }
    }

    /// Reads back a ruleset file this agent rendered.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::ForeignRuleset`] when `text` is not a file
    /// this agent's current template would produce: a different first line, a
    /// missing or altered replace idiom, an altered chain preamble, a rule
    /// line that does not parse, a missing panel accept, or missing closing
    /// braces. Nothing is overwritten on any of them — see the variant for
    /// why "I wrote this and cannot read it back" gets the same answer as "I
    /// did not write this".
    ///
    /// Returns [`FirewallError::RenderFailed`] when this agent's own template
    /// cannot be rendered, which is what the accepted shape is derived from.
    ///
    /// # Why this needs the ports
    ///
    /// The unconditional accepts are byte-identical to an operator's own
    /// any-source TCP allow — `tcp dport 2222 accept` reads the same whether
    /// the agent rendered it because sshd listens there or because somebody
    /// asked for it. While there was exactly ONE ssh port, position settled it:
    /// the ssh block came first and the panel accept was the line after it.
    /// With a list it cannot, because a second ssh port's fallback sits exactly
    /// where the panel accept used to be — and reading it as the panel accept
    /// reports the panel port back to the caller as a rule nobody created.
    /// Knowing which ports are derived is the whole of what is needed, and it
    /// is a host fact the caller already holds.
    pub fn parse(text: &str, ports: &RulesetPorts) -> Result<Self, FirewallError> {
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
        if actual.get(REPLACE_IDIOM_LINES..preamble) != expected.get(REPLACE_IDIOM_LINES..preamble)
        {
            return Err(FirewallError::ForeignRuleset);
        }

        let Some(body) = actual.get(preamble..) else {
            return Err(FirewallError::ForeignRuleset);
        };
        let Some(region) = body
            .len()
            .checked_sub(CLOSING_LINES)
            .and_then(|end| body.split_at_checked(end))
        else {
            return Err(FirewallError::ForeignRuleset);
        };
        let (rule_lines, closing) = region;
        if Some(closing) != expected.get(expected.len().saturating_sub(CLOSING_LINES)..) {
            return Err(FirewallError::ForeignRuleset);
        }

        let parsed = rule_lines
            .iter()
            .map(|line| parse_rule(line).ok_or(FirewallError::ForeignRuleset))
            .collect::<Result<Vec<_>, _>>()?;

        Ok(Self {
            rules: managed_rules(&parsed, ports)?,
        })
    }

    /// The operator-managed rules, in file order.
    #[must_use]
    pub fn rules(&self) -> &[FirewallRule] {
        &self.rules
    }

    /// Whether `rule` is already recorded.
    #[must_use]
    pub fn contains(&self, rule: &FirewallRule) -> bool {
        self.rules.contains(rule)
    }

    /// This state with `rule` appended.
    ///
    /// Appended rather than inserted in any particular place: the render
    /// re-groups the SSH rules ahead of the rest anyway, so the only ordering
    /// the file has is "SSH block, then allows in the order they were added".
    #[must_use]
    pub fn with(&self, rule: &FirewallRule) -> Self {
        let mut rules = self.rules.clone();
        rules.push(rule.clone());

        Self { rules }
    }

    /// This state with every rule equal to `rule` removed.
    ///
    /// Every one rather than the first, because a file that somehow holds the
    /// same rule twice must converge on holding it zero times — a deny that
    /// left a duplicate behind would report success while the port stayed
    /// open.
    #[must_use]
    pub fn without(&self, rule: &FirewallRule) -> Self {
        Self {
            rules: self
                .rules
                .iter()
                .filter(|held| *held != rule)
                .cloned()
                .collect(),
        }
    }

    /// Renders this state as the complete ruleset file, for `ports`.
    ///
    /// The routing of rules into the template's lists is the whole of R2's
    /// lockout guard and it happens here: a rule joins an SSH port's group
    /// only when it is TCP **and** its port is THAT port. A UDP rule for the
    /// same number is an ordinary allow, so it can never displace an
    /// unconditional SSH accept — which was a reviewed lockout hole, since a
    /// UDP rule taking a fallback's place closes the TCP port the operator is
    /// connected on.
    ///
    /// A host can serve SSH on several ports at once, so the groups are per
    /// port: each keeps its own fallback until an explicit rule for it exists,
    /// and an explicit rule for one port never suppresses another's.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::RenderFailed`] when the template fails, which
    /// can only happen if it and its render type have drifted apart.
    pub fn render(&self, ports: &RulesetPorts) -> Result<String, FirewallError> {
        // One group per SSH port, in the order the caller gave them, each
        // holding only the TCP rules for THAT port. A rule belongs to at most
        // one group, so removing an explicit rule for one SSH port returns
        // that port's fallback and leaves every other port exactly as it was.
        let mut ssh_ports: Vec<NftablesSshPort> = ports
            .ssh_ports
            .iter()
            .map(|port| NftablesSshPort {
                port: port.value(),
                rules: Vec::new(),
            })
            .collect();
        let mut allows = Vec::new();

        for rule in &self.rules {
            let ssh_group = (rule.protocol == NftablesProtocol::Tcp)
                .then(|| {
                    ssh_ports
                        .iter_mut()
                        .find(|ssh| ssh.port == rule.port.value())
                })
                .flatten();

            match ssh_group {
                Some(ssh) => ssh.rules.push(rule.to_allow()),
                None => allows.push(rule.to_allow()),
            }
        }

        NftablesRuleset {
            ssh_ports,
            panel_port: ports.panel_port.value(),
            allows,
        }
        .render_config()
        .map_err(|_| FirewallError::RenderFailed)
    }
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
    NftablesRuleset {
        // Exactly one SSH port, whatever the host really has. The lines read
        // out of the reference are the marker, the replace idiom, the chain
        // preamble and the two closing braces, and not one of them mentions a
        // port — but the COUNT of unconditional accepts is used to cut
        // preamble from rules, so the reference has to fix it at one.
        ssh_ports: vec![NftablesSshPort {
            port: REFERENCE_SSH_PORT,
            rules: Vec::new(),
        }],
        panel_port: REFERENCE_PANEL_PORT,
        allows: Vec::new(),
    }
    .render_config()
    .map_err(|_| FirewallError::RenderFailed)
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

/// Reads one rendered rule line back into a [`FirewallRule`].
///
/// `None` for anything this agent's template does not render, which the
/// caller turns into [`FirewallError::ForeignRuleset`]. Every value goes
/// through its validated type — [`Port`] and [`SourceCidr`] — so a rule that
/// comes out of this function is a rule that could be rendered back
/// unchanged, and the address-family keyword has to agree with the network it
/// precedes.
fn parse_rule(line: &str) -> Option<FirewallRule> {
    let tokens: Vec<&str> = line.split_whitespace().collect();

    let protocol = match *tokens.first()? {
        "tcp" => NftablesProtocol::Tcp,
        "udp" => NftablesProtocol::Udp,
        _ => return None,
    };
    if *tokens.get(1)? != DPORT {
        return None;
    }
    let port = parse_port(tokens.get(2)?)?;

    let source = match tokens.len() {
        OPEN_RULE_TOKENS => SourceCidr::any_v4(),
        RESTRICTED_RULE_TOKENS => {
            if *tokens.get(4)? != SADDR {
                return None;
            }
            let source = SourceCidr::parse(tokens.get(5)?).ok()?;
            if *tokens.get(3)? != FirewallRule::keyword_for(&source) {
                return None;
            }

            source
        }
        _ => return None,
    };

    if *tokens.last()? != ACCEPT {
        return None;
    }

    Some(FirewallRule {
        port,
        protocol,
        source,
    })
}

/// Reads a rendered port number back.
///
/// A leading zero is refused, and that is not pedantry: `SourceCidr`, parsed
/// three lines below this in the same rule line, deliberately refuses a
/// leading-zero octet because a value with two spellings is a rule that can be
/// added under one and left behind under the other. `str::parse` would accept
/// `08443` as 8443, so one half of a rule line would enforce a single spelling
/// and the other would not — and the file would be silently rewritten into the
/// canonical form on the next mutation. One rule, one answer.
///
/// `"0"` itself needs no special case: it has no leading zero to strip, and
/// [`Port::parse`] refuses it anyway.
fn parse_port(token: &str) -> Option<Port> {
    // Checked by hand rather than left to `u32::from_str`, which accepts a
    // leading `+` — so `+8443` would be a second spelling of one port, which
    // is the very thing the paragraph above refuses a leading zero for.
    // `SourceCidr`'s own prefix parser folds its digits by hand for exactly
    // this reason; this is the same guard on the other half of the line.
    if token.is_empty() || !token.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }

    if token.len() > 1 && token.starts_with('0') {
        return None;
    }

    Port::parse(token.parse::<u32>().ok()?).ok()
}

/// Separates the operator's rules from the unconditional accepts.
///
/// The rendered region is one block per SSH port, then the panel port's
/// accept, then everything else. The SSH region is the leading run of TCP
/// lines whose port sshd listens on — sound because a TCP rule for an SSH port
/// can never be rendered anywhere else, so everything after the panel line is
/// for some other port or protocol.
///
/// **It takes the ports rather than inferring them, and that is the whole of
/// what changed when one SSH port became a list.** The first line's port
/// used to delimit the block; with several ports the next block's fallback
/// sits exactly where the panel accept does, and inferring would report the
/// panel port as a rule nobody created.
///
/// A lone bare accept in a port's block is that port's FALLBACK and not a
/// rule: it is what the template renders when the operator has authored
/// nothing for it. Reporting it would show every host a rule nobody created,
/// and denying it would report success while the template rendered it straight
/// back. Each port is judged on its own block, so an explicit rule for one SSH
/// port leaves every other port's fallback exactly where it was.
///
/// # Errors
///
/// Returns [`FirewallError::PortsDisagree`] when the region is shorter than
/// the unconditional accepts, or does not carry the panel port's bare accept
/// where the render puts it. Not `ForeignRuleset`: the file has already proved
/// itself this agent's by its marker, its idiom and its chain preamble, so what
/// is wrong is the ports the caller named — which is also what a host whose
/// sshd and panel share a port would look like, and no host is, because two
/// daemons cannot bind one port.
fn managed_rules(
    region: &[FirewallRule],
    ports: &RulesetPorts,
) -> Result<Vec<FirewallRule>, FirewallError> {
    // Every refusal below is `PortsDisagree` and not `ForeignRuleset`. By the
    // time control reaches here the marker, the replace idiom, the chain
    // preamble and the closing braces have all matched this agent's own
    // render, so the file IS ours; what has not lined up is the caller's idea
    // of which ports are unconditionally accepted.
    if region.len() < MINIMUM_RULE_LINES {
        return Err(FirewallError::PortsDisagree);
    }

    // The SSH region: the leading run of TCP rules for ports sshd listens on,
    // in the order the template rendered the ports. Everything after it must
    // begin with the panel accept.
    let ssh_region = region
        .iter()
        .take_while(|rule| {
            rule.protocol == NftablesProtocol::Tcp && ports.ssh_ports.contains(&rule.port)
        })
        .count();

    let Some(panel) = region.get(ssh_region) else {
        return Err(FirewallError::PortsDisagree);
    };
    if panel.protocol != NftablesProtocol::Tcp
        || panel.port != ports.panel_port
        || !panel.is_open_to_anyone()
    {
        return Err(FirewallError::PortsDisagree);
    }

    let mut rules = Vec::new();
    for port in &ports.ssh_ports {
        // This port's own block, wherever the template put it. A block of
        // exactly one any-source rule IS the unconditional fallback and
        // contributes nothing: reporting it would show every host a rule
        // nobody created, and denying it would report success while the
        // template rendered it straight back (R2).
        let block: Vec<&FirewallRule> = region
            .get(..ssh_region)
            .unwrap_or_default()
            .iter()
            .filter(|rule| rule.port == *port)
            .collect();

        let fallback_only = block.len() == 1 && block.iter().all(|rule| rule.is_open_to_anyone());
        if fallback_only {
            continue;
        }

        rules.extend(block.into_iter().cloned());
    }

    rules.extend_from_slice(region.get(ssh_region + 1..).unwrap_or_default());

    Ok(rules)
}

#[cfg(test)]
#[path = "../../tests/firewall/ruleset_state_tests.rs"]
mod tests;
