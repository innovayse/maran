//! The agent-managed rule set, read back out of the file it was rendered to.

use maran_templates::nftables::nftables_protocol::NftablesProtocol;
use maran_templates::nftables::nftables_ruleset::NftablesRuleset;
use maran_templates::nftables::nftables_ssh_port::NftablesSshPort;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::model::checked_rule_lines::checked_rule_lines;
use crate::firewall::model::firewall_rule::FirewallRule;
use crate::firewall::model::managed_rules::managed_rules;
use crate::firewall::model::output_policy::OutputPolicy;
use crate::firewall::model::parse_rule::parse_rule;
use crate::firewall::model::render_reference_ruleset::render_reference_ruleset;
use crate::firewall::model::ruleset_ports::RulesetPorts;

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
    /// line that does not parse, a missing panel accept, or a tail (the
    /// address sets, the output chain, the closing braces) other than the
    /// template's. Nothing is overwritten on any of them — see the variant for
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
        let rule_lines = checked_rule_lines(text)?;

        let parsed = rule_lines
            .iter()
            .map(|line| parse_rule(line).ok_or(FirewallError::ForeignRuleset))
            .collect::<Result<Vec<_>, _>>()?;

        Ok(Self {
            rules: managed_rules(&parsed, ports)?,
        })
    }

    /// `text` with everything after its last rule line replaced by what the
    /// current template renders there for `policy`: the input chain's closing
    /// brace, the address sets, the output chain and the table's closing brace.
    ///
    /// This is how a host whose file predates the output chain, or whose
    /// addresses or uid floor moved since it was rendered, is brought up to date
    /// WITHOUT knowing the host's SSH and panel ports (the agent does not hold
    /// them between requests): the rules are carried over byte for byte and only
    /// the part that holds no operator input is rewritten. The same checks as
    /// [`RulesetState::parse`] run first, so a file this agent did not write is
    /// still [`FirewallError::ForeignRuleset`] and is never touched.
    ///
    /// # Errors
    ///
    /// [`FirewallError::ForeignRuleset`] as above; [`FirewallError::RenderFailed`]
    /// when the template fails.
    pub fn with_current_host_facts(
        text: &str,
        policy: &OutputPolicy,
    ) -> Result<String, FirewallError> {
        checked_rule_lines(text)?;

        let current = render_reference_ruleset(policy)?;
        let current_lines: Vec<&str> = current.lines().collect();
        let own_lines: Vec<&str> = text.lines().collect();
        let (Some(own_last), Some(current_last)) = (
            own_lines
                .iter()
                .rposition(|line| parse_rule(line.trim()).is_some()),
            current_lines
                .iter()
                .rposition(|line| parse_rule(line.trim()).is_some()),
        ) else {
            return Err(FirewallError::ForeignRuleset);
        };

        let mut refreshed: Vec<&str> = own_lines.get(..=own_last).unwrap_or_default().to_vec();
        refreshed.extend_from_slice(current_lines.get(current_last + 1..).unwrap_or_default());
        let mut refreshed = refreshed.join("\n");
        refreshed.push('\n');

        Ok(refreshed)
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
    /// `policy` is the host's customer uid floor and own addresses for the
    /// output chain; it is not part of the stored rules, so the caller reads it
    /// fresh from the host every time it renders.
    ///
    /// # Errors
    ///
    /// Returns [`FirewallError::RenderFailed`] when the template fails, which
    /// can only happen if it and its render type have drifted apart.
    pub fn render(
        &self,
        ports: &RulesetPorts,
        policy: &OutputPolicy,
    ) -> Result<String, FirewallError> {
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
            // A RANGE is never routed into an SSH port's group, whatever its
            // lower bound is. The template renders `{{ ssh.port }}` on every
            // line of a port's block and never the rule's own ports, so a range
            // that reached one would render as the single SSH port — the range
            // silently collapsed to one port, with the operator told it was
            // installed. It renders as an ordinary allow instead, which leaves
            // that port's unconditional accept exactly where it was: the
            // fail-open direction R2 asks for.
            let ssh_group = (rule.protocol == NftablesProtocol::Tcp
                && rule.ports.upper().is_none())
            .then(|| {
                ssh_ports
                    .iter_mut()
                    .find(|ssh| ssh.port == rule.ports.lower().value())
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
            customer_uid_min: policy.customer_uid_min(),
            self_v4_prefixes: policy
                .self_v4_prefixes()
                .iter()
                .map(|prefix| (prefix.network(), prefix.length()))
                .collect(),
            self_v4: policy.self_v4().to_vec(),
            self_v6: policy.self_v6().to_vec(),
        }
        .render_config()
        .map_err(FirewallError::from)
    }
}

#[cfg(test)]
#[path = "../../tests/firewall/ruleset_state_tests.rs"]
mod tests;
