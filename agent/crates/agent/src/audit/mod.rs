//! The audit trail of the root agent (issue #106): one structured journal line per
//! mutating rpc, written by a `tower` layer in front of every service.
//!
//! The line is the agent's own record of what it was asked and what it answered,
//! kept apart from the panel's database (`docs/superpowers/notes/2026-11-06-agent-audit-lines-threat-note.md`).
//! Handlers never log it themselves: they fill the request's [`audit_slot::AuditSlot`]
//! with validated values, and the layer writes the line when the response ends.

pub mod audit_emission;
pub mod audit_layer;
pub mod audit_line;
pub mod audit_outcome;
pub mod audit_rpc_class;
pub mod audit_service;
pub mod audit_slot;
pub mod audited_body;
pub mod mutating_rpcs;
pub mod read_only_rpcs;

/// Test-only: a tracing writer that keeps what the audit target emitted, mounted once
/// for every audit test file that has to read the journal line back.
#[cfg(test)]
#[path = "../tests/audit/line_capture.rs"]
pub(crate) mod line_capture;

/// Test-only: a response body that plays a scripted list of frames, mounted once for
/// the body and service tests.
#[cfg(test)]
#[path = "../tests/audit/scripted_body.rs"]
pub(crate) mod scripted_body;
