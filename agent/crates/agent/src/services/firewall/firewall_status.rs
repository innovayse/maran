//! The one mapping from firewall operation failures onto the wire error.

use maran_ops::firewall::FirewallError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a firewall operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **`tool_output` carries `nft`'s own standard error, and only for the two
/// variants that hold one.** That is a deliberate difference from the database
/// and cron areas, where the field is always empty. This surface is admin-only,
/// an `nft` refusal is unintelligible without the message it came with, and the
/// message is the operator's only way to tell "port 0 is not a port" from "this
/// kernel has no `inet` family". What no variant can carry is a rule, an
/// address or a path — [`FirewallError`] has no field for one — so a refusal
/// cannot echo back a value the caller planted (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &FirewallError) -> AgentError {
    let (code, tool_output) = match error {
        // `firewall.proto`: "allowing an identical rule again returns
        // AlreadyExists". An idempotency outcome, not a fault.
        FirewallError::AlreadyExists => (ErrorCode::AlreadyExists, String::new()),
        // `firewall.proto`: "unbanning an address with no active ban returns
        // NotFound", and denying a port no rule opens is the same answer.
        FirewallError::NotFound => (ErrorCode::NotFound, String::new()),
        // The case rules/proto.md defines ERROR_CODE_VALIDATION_FAILED as:
        // "rendered config failed its validator; state rolled back". There is
        // nothing to roll back here and that is stronger, not weaker —
        // `nft --check` runs against the STAGED file, before the rename, so the
        // live path still holds the previous content and the kernel still holds
        // the previous rules.
        FirewallError::RuleRefusedByNft { stderr } => (ErrorCode::ValidationFailed, stderr.clone()),
        // A valid file the tool would not load, or a listing that would not
        // run: a fault of the machine, with `nft`'s own stderr for the
        // operator.
        FirewallError::NftFailed { stderr } => (ErrorCode::SystemFailure, stderr.clone()),
        // The host's state and the request disagree, and this code refuses to
        // say which of them is wrong — because the agent cannot tell. Either
        // the caller sent the wrong ports, or the rendered file went stale when
        // sshd moved; the second is likelier, since the ports on the wire come
        // from the installer's own detection and the file is the older
        // artifact.
        //
        // VALIDATION_FAILED and not INVALID_INPUT, which is the same
        // distinction `files/file_status.rs` draws for `EscapesHome`: the
        // request was well formed — every port passed `Port::parse` — and the
        // AGENT's own check is what refused it, after looking at the host.
        // INVALID_INPUT would assert the input is wrong and send an operator to
        // change a panel setting that may be the correct half.
        //
        // The variant's own message carries the recovery, because this is the
        // one firewall state no rpc gets out of.
        FirewallError::PortsDisagree => (ErrorCode::ValidationFailed, String::new()),
        // Faults of this machine or of its state, not of the request.
        // ForeignRuleset is the narrowest: the file at the ruleset path was not
        // written by this agent at all — a different first line, a missing
        // replace idiom, an edited chain preamble — so nothing was overwritten
        // and an operator has to go and look at that file. It is deliberately
        // not INVALID_INPUT: no value the panel sends can produce it, now that
        // the one input which CAN disagree with a file of ours has a variant of
        // its own above.
        FirewallError::ForeignRuleset
        | FirewallError::UnreadableNftOutput
        | FirewallError::RulesetUnreadable
        | FirewallError::RenderFailed
        | FirewallError::StagingFailed => (ErrorCode::SystemFailure, String::new()),
        // FirewallError is #[non_exhaustive] (rules/rust.md), so a variant added
        // in the ops crate lands here rather than failing this build. It maps to
        // a system failure: the panel then reports a fault instead of silently
        // treating an unclassified failure as "not found" and carrying on.
        _ => (ErrorCode::SystemFailure, String::new()),
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output,
    }
}

#[cfg(test)]
#[path = "../../tests/services/firewall/firewall_status_tests.rs"]
mod tests;
