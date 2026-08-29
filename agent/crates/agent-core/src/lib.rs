#![warn(missing_docs)]
//! maran-agent-core — security primitives shared by the agent's crates:
//! input validation (name regexes, path containment via `resolve_in_home`)
//! in `validation/`, and the workspace's ONLY permitted home of `unsafe`
//! syscall/setuid wrappers in `privs/` (rules/rust.md "unsafe"). The `privs/`
//! module lands with the task that first needs to drop privileges.

pub mod validation;
