#![warn(missing_docs)]
//! maran-agent — the root daemon.
//!
//! Serves the typed gRPC contract from `proto/agent/v1/` over a unix domain
//! socket; the command set is closed, and no rpc executes a caller-supplied
//! program, shell string or template (rules/architecture.md "Agent").
//!
//! The crate is a library with a thin binary on top so that integration tests
//! can start a real server in-process, on a temporary socket, and drive it
//! through the generated client.

pub mod config;
pub mod error;
pub mod peercred;
pub mod server;
pub mod services;

/// Types generated from the proto contract — never edited by hand
/// (rules/proto.md). Included once here so the whole crate shares one copy.
///
/// The lints are lifted for this module alone, and only these two. Including
/// generated code puts it inside our crate, so the crate's own rules land on
/// output nobody here wrote: `missing_docs` demands doc comments on prost's
/// message structs and their fields, and `result_large_err` objects to the size
/// of tonic's `Status`. Neither is actionable — the fix would be to edit
/// generated code, which rules/proto.md forbids — and silencing them here keeps
/// both rules in full force everywhere they can be obeyed.
#[allow(missing_docs, clippy::result_large_err)]
pub mod proto {
    tonic::include_proto!("maran.agent.v1");
}
