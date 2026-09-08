//! The daemon's stop path: what `systemctl stop` actually does to a root process.
//!
//! The agent's operations are destructive by nature — it creates and deletes
//! system accounts, drops databases, and swaps a customer's home directory into
//! place — so the moment it is stopped is a moment that can leave the machine in
//! a state nobody asked for. Without a handler, `SIGTERM`'s default action ends
//! the process instantly, mid-syscall: measured at 3.5 ms, exit 143, with a
//! half-created account as one of the outcomes.
//!
//! What this module provides is a **bounded** drain, and the bound is the design
//! rather than a safety valve. `sites.TailSiteLog` is a server-streaming rpc
//! that polls for as long as its client reads, so a drain that waited for every
//! in-flight request would wait for an open log viewer — for ever — and every
//! stop would end at `TimeoutStopSec` in a `SIGKILL` anyway. A stop that is
//! slower AND still ends in a kill is worse than the instant death it replaced.

pub mod drain_deadline;
pub mod shutdown_signal;

pub use drain_deadline::{DRAIN_BUDGET, drain_deadline};
pub use shutdown_signal::shutdown_signal;
