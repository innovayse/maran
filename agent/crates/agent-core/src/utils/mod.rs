//! Cross-crate helpers, one file per named purpose.
//!
//! The same shape the SPA uses in `frontend/src/utils/`: a folder of small,
//! single-purpose units — `formatDate.ts`, not `utils.ts`. What is banned is the
//! catch-all (`util.rs`, `helpers.rs`, `misc.rs`), because a folder whose name is
//! "everything else" has no rule for what belongs in it, and within a year it is
//! the largest file nobody dares change.
//!
//! A helper earns a place here when a SECOND crate needs it. Until then it stays
//! private beside its only caller, where it can be changed without a thought for
//! anyone else.
//!
//! **The bounded spawn core lives here.** Every process the agent starts is run
//! through it: `run_bounded` is the core, `spawn_argv` is its default path (null
//! input, default caps), `run_for_window` is the variant where outliving a window
//! is success, and `run_unthreaded` is the variant for a forked child that may
//! start no thread. The request, its input and output choices, the call class
//! that decides the deadline, and the two typed errors (`SpawnError`,
//! `BoundExceeded`) are public; the files marked private below are the core's own
//! parts and are not a surface.

pub mod apply_child_environment;
pub mod available_bytes;
pub mod bound_exceeded;
pub mod call_class;
pub mod child_input;
pub mod child_output;
pub mod current_uid;
pub mod directory;
pub mod output_stream;
pub mod pgrep_live_uid_arguments;
pub mod pkill_answer;
pub mod pkill_uid_arguments;
pub mod run_bounded;
pub mod run_for_window;
pub mod run_unthreaded;
pub mod spawn_argv;
pub mod spawn_error;
pub mod spawn_request;
pub mod system_account;
pub mod system_accounts;
pub mod window_outcome;

// The core's own parts. Private: `ops` reaches the core through the entry points
// above, and nothing outside this folder may start a child any other way.
mod caps;
mod child_event;
mod deadline;
mod has_exited;
mod kill_group_and_reap;
mod launch_child;
mod poll_interval;
mod start_reader;
mod start_writer;
mod supervise_child;
mod supervised;

// Test support shared by the core's tests that look at real processes, mounted
// once because its users are the tests of several units.
#[cfg(test)]
#[path = "../tests/support/process_probe.rs"]
mod process_probe;
