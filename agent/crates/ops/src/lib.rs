#![warn(missing_docs)]
// The compiler, not a grep, is the gate: `unsafe` exists in this workspace only
// in maran-agent-core::privs (rules/rust.md "unsafe"). `forbid` cannot be lowered
// by an `#[allow]` further down, so adding unsafe here does not compile at all.
#![forbid(unsafe_code)]
//! maran-ops — the agent's domain operations, one module per area
//! (`accounts/`, `sites/`, `php/`, `db/`, `ftp/`, `files/`, `cron/`,
//! `firewall/`, `ssl/`, `backup/`, `monitor/`). Every operation is
//! idempotent and re-validates its inputs via `maran-agent-core`
//! (rules/rust.md "Validation first"). Modules land with their backend
//! counterparts, starting with Plan 2.

pub mod accounts;
pub mod files;
pub mod php;
pub mod safe_write;
pub mod sites;
pub mod ssl;
