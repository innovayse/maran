//! The once-only emitter of one request's audit line.

use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Instant;

use http::HeaderMap;
use tonic::Code;

use super::audit_line::AuditLine;
use super::audit_outcome::AuditOutcome;
use super::audit_slot::AuditSlot;

/// The response header and trailer that carry the gRPC status.
const GRPC_STATUS: &str = "grpc-status";

/// Owns everything needed to write one request's line, and writes it exactly once:
/// at end of stream, on an error frame, or when it is dropped, whichever comes first.
/// The `AtomicBool` decides who emits, so end-of-stream followed by a drop (or the
/// reverse) writes a single line (threat note, section 4).
pub struct AuditEmission {
    /// The method, as the agent's own `MUTATING_RPCS` or `DISCLOSING_RPCS` entry spells it.
    list_entry: &'static str,
    /// The slot the handler fills.
    slot: AuditSlot,
    /// When the request reached the layer.
    started: Instant,
    /// The non-OK gRPC status the response head or trailers carried, if any.
    transport: Option<Code>,
    /// Set by the first emitter; a later one finds it set and writes nothing.
    emitted: AtomicBool,
}

impl AuditEmission {
    /// Starts the clock for one audited request.
    #[must_use]
    pub fn new(list_entry: &'static str, slot: AuditSlot) -> Self {
        Self {
            list_entry,
            slot,
            started: Instant::now(),
            transport: None,
            emitted: AtomicBool::new(false),
        }
    }

    /// Reads `grpc-status` from response headers or trailers. Only the status is
    /// read, never a data frame; a non-OK code is kept, an OK one is ignored.
    pub fn note_status(&mut self, headers: &HeaderMap) {
        if let Some(value) = headers.get(GRPC_STATUS) {
            let code = Code::from_bytes(value.as_bytes());
            if code != Code::Ok {
                self.transport = Some(code);
            }
        }
    }

    /// Writes the line unless it was already written. The settled outcome wins; an
    /// unsettled rpc is the transport status when one was seen, `unset` otherwise.
    pub fn emit(&self) {
        if self
            .emitted
            .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
            .is_err()
        {
            return;
        }
        let outcome = self
            .slot
            .settled_outcome()
            .or(self.transport.map(AuditOutcome::Transport))
            .unwrap_or(AuditOutcome::Unset);
        let elapsed = u64::try_from(self.started.elapsed().as_millis()).unwrap_or(u64::MAX);
        AuditLine::new(
            self.list_entry,
            outcome,
            elapsed,
            self.slot.recorded_account(),
            self.slot.recorded_site(),
        )
        .emit();
    }
}

impl Drop for AuditEmission {
    /// A body (or a pending response future) dropped before its end still writes the
    /// line. The emission takes only the slot's mutex, held for a field read, and a
    /// poisoned one degrades; nothing here can panic.
    fn drop(&mut self) {
        self.emit();
    }
}
