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
//!
//! What the bound bounds, precisely, because the sentence above reads as more
//! than it is. It bounds how long the daemon keeps ANSWERING — not how long the
//! process lives, and not how long root work runs. Every unit of host work runs
//! inside `spawn_blocking`, a dropped `JoinHandle` DETACHES such a task rather
//! than cancelling it, and no tokio API can cancel one; so when the budget
//! expires the requests are abandoned and the `useradd`, the `rename`, the
//! database load carry on. `main` then drops the runtime, which joins every
//! blocking thread with no timeout, so the exit waits for exactly the work the
//! expiry was supposed to have given up on. A stop is bounded by the unit's
//! `TimeoutStopSec=45` and the `SIGKILL` behind it; the budget only decides how
//! early the socket goes away. See [`DRAIN_BUDGET`] for what a kill costs per
//! operation and what the next start does and does not repair.

pub mod drain_deadline;
pub mod stop_signals;

pub use drain_deadline::{DRAIN_BUDGET, drain_deadline};
pub use stop_signals::StopSignals;
