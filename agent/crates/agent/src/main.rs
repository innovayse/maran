#![warn(missing_docs)]
//! maran-agent — the root daemon. Serves the typed gRPC contract from
//! `proto/agent/v1/` over a unix domain socket; the command set is closed
//! (rules/architecture.md "Agent"). The server wiring (`server.rs`,
//! `peercred.rs`, `services/`) lands with Plan 2; until then this entry
//! point only anchors the workspace so the fmt/clippy/test gates run.

/// Process entry point. Boots nothing yet: gRPC serving arrives with Plan 2.
fn main() {}
