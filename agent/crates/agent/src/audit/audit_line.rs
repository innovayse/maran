//! The audit line itself: its text and its emission.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::audit_outcome::AuditOutcome;

/// The tracing target every audit line is written under. Fixed, so an operator can
/// filter on it and a test can capture exactly these events.
const AUDIT_TARGET: &str = "maran_audit";

/// One audit line, built only from typed sources: the agent's own method constant,
/// a closed outcome, an integer, and the validated account and site. No field is
/// free text, so none can carry a space, a newline or an `=` (threat note, section 2).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AuditLine {
    /// The method, as the agent's own list spells it, without the leading slash.
    method: &'static str,
    /// How the rpc ended.
    outcome: AuditOutcome,
    /// Wall time from the request reaching the layer to the response ending.
    duration_ms: u64,
    /// The validated account, when a handler recorded one.
    account: Option<AccountName>,
    /// The validated site, when a handler recorded one.
    site: Option<Domain>,
}

impl AuditLine {
    /// Builds a line. `list_entry` is a path from `MUTATING_RPCS` (the leading slash
    /// is dropped), never the request's own bytes.
    #[must_use]
    pub fn new(
        list_entry: &'static str,
        outcome: AuditOutcome,
        duration_ms: u64,
        account: Option<AccountName>,
        site: Option<Domain>,
    ) -> Self {
        Self {
            method: list_entry.strip_prefix('/').unwrap_or(list_entry),
            outcome,
            duration_ms,
            account,
            site,
        }
    }

    /// The text of the line:
    /// `event=rpc method=<Service/Method> outcome=<..> duration_ms=<n> account=<name|-> site=<domain|->`.
    #[must_use]
    pub fn render(&self) -> String {
        format!(
            "event=rpc method={} outcome={} duration_ms={} account={} site={}",
            self.method,
            self.outcome,
            self.duration_ms,
            self.account.as_ref().map_or("-", AccountName::as_str),
            self.site.as_ref().map_or("-", Domain::as_str),
        )
    }

    /// Writes the line at INFO on the audit target as ONE pre-built message, so the
    /// formatter adds no decoration of a caller's making. It has no result: a journal
    /// that cannot be written to never changes the response the peer receives.
    pub fn emit(&self) {
        tracing::info!(target: AUDIT_TARGET, "{}", self.render());
    }
}

#[cfg(test)]
#[path = "../tests/audit/audit_line_tests.rs"]
mod tests;
