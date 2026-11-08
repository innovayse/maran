//! The one mapping from DNS failures onto the wire error.

use maran_ops::dns::DnsError;

use crate::proto::{AgentError, ErrorCode};

/// Maps an error of the DNS area onto the contract's typed error.
///
/// The code is chosen by what the panel should do next, one variant to one code
/// in this one place. `message` is operator-facing English; `tool_output` stays
/// empty, because what a program printed before it failed is neither complete
/// nor safe to forward, and the checker's refusal already travels in the
/// message of [`DnsError::ConfigRejected`], by design, as operator text.
#[must_use]
pub fn to_agent_error(error: &DnsError) -> AgentError {
    let code = match error {
        // The daemon's own checker refused the rendered configuration; the
        // contract's code for exactly this is "state rolled back", and the
        // rollback is what `enable_dns` performs.
        DnsError::ConfigRejected { .. } => ErrorCode::ValidationFailed,
        // The same code for a zone: the checker refused the rendered file and the
        // previous one was kept. A zone that broke a rule of its own (a CNAME
        // beside data, a ceiling) is the caller's to fix, so it is a request
        // fault.
        DnsError::ZoneRejected { .. } => ErrorCode::ValidationFailed,
        DnsError::ZoneContent(_) => ErrorCode::InvalidInput,
        // The filesystem the zone is written on is nearly full: nothing was
        // written, and the panel reports it as the host's problem.
        DnsError::ZoneBelowFloor { .. } => ErrorCode::BelowFreeSpaceFloor,
        // A deadline or an output cap ended a program and its process group was
        // killed: a fault of the host, never a verdict on the request. Named on
        // its own line so the census of bound-carrying errors can see the arm.
        DnsError::Bound(_) => ErrorCode::SystemFailure,
        // Faults of this machine.
        DnsError::ServiceRefused { .. }
        | DnsError::NotListening
        | DnsError::SpawnFailed { .. }
        | DnsError::Render
        | DnsError::ConfigUnreadable
        | DnsError::ConfigWrite { .. }
        // A daemon that cannot be asked is the fault of the host, and it is also
        // what a switched-off daemon looks like: the message names the control
        // channel, and the panel knows whether it enabled DNS.
        | DnsError::ControlRefused { .. }
        | DnsError::ZoneStateUnreadable
        | DnsError::ZoneDirectoryUnreadable
        | DnsError::ZoneUnmeasurable => ErrorCode::SystemFailure,
        // `DnsError` is #[non_exhaustive] (rules/rust.md), so a variant added in
        // the ops crate lands here rather than failing this build. A system
        // failure makes the panel report a fault instead of treating an
        // unclassified failure as "not found" and carrying on.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/dns/dns_status_tests.rs"]
mod tests;
