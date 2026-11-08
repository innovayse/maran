//! The answer of an rpc whose operation this build does not carry yet.

use crate::audit::audit_slot::AuditSlot;
use crate::proto::{AgentError, ErrorCode};

/// The typed refusal the mail data-plane rpcs answer with until their operations
/// land: [`ErrorCode::NotImplemented`], which the contract defines for a request
/// that is well formed and understood but has no code path in this build.
///
/// It also settles the request's audit slot with that code, so a mutating rpc
/// that was refused leaves its one journal line (`outcome=NOT_IMPLEMENTED`) and
/// not `outcome=unset`. A read-only rpc has no slot installed and the call
/// settles a detached one, which is harmless.
///
/// The refusal touches nothing and validates nothing: the request is not even
/// read, so a field of it cannot reach a message or a log line.
#[must_use]
pub fn refuse_data_plane(extensions: &http::Extensions) -> AgentError {
    AuditSlot::of(extensions).refused(ErrorCode::NotImplemented);
    AgentError {
        code: ErrorCode::NotImplemented as i32,
        message: "mail data operations (domains, mailboxes, aliases, queue) are not available in \
                  this agent build"
            .to_owned(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/mail/refuse_data_plane_tests.rs"]
mod tests;
