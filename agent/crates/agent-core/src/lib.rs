#![warn(missing_docs)]
//! maran-agent-core — security primitives shared by the agent's crates:
//! input validation (account names, path containment via `resolve_in_home`)
//! in `validation/`, the agent-owned filesystem locations every area shares
//! (`agent_paths`), and the workspace's ONLY permitted home of `unsafe`
//! syscall/setuid wrappers in `privs/` (rules/rust.md "unsafe").
//!
//! No `#![forbid(unsafe_code)]` here, and that is the single deliberate exception
//! in the workspace: `privs/` needs it. The allow is scoped to the two modules
//! inside `privs/` that hold syscalls, so `unsafe` anywhere else in this crate
//! still fails to compile under the workspace's `unsafe_code = "deny"`.

pub mod agent_paths;
pub mod command_outcome;
pub mod privs;
pub mod utils;
pub mod validation;
