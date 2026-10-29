//! The one error a bound on a spawned program produces.

use std::time::Duration;

use crate::utils::output_stream::OutputStream;

/// A bound the bounded spawn core enforces was reached, and the child's whole
/// process group was killed because of it.
///
/// Every area wraps exactly this type, so that one match arm per area says
/// "the host did not answer" and no area invents its own spelling of it. It is
/// an ERROR and never a case of [`crate::command_outcome::CommandOutcome`], on
/// purpose: a caller that matches on an exit status cannot read a kill as a
/// refusal, and a parser is never handed an answer that was cut short — a
/// firewall rule read as absent from a truncated listing is a rule that gets
/// added or removed.
///
/// **What the kill does and does not do.** It ends the program and every
/// descendant that stayed in the program's process group. It does not cancel
/// work the program handed to somebody else: a job queued with the service
/// manager may still run, and a statement the database server already accepted
/// may still execute. The `Display` text says so, so no caller reports a live
/// rollback it did not perform. A descendant that left the group with its own
/// `setsid` is outside it as well.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum BoundExceeded {
    /// The program did not finish inside its call class's deadline.
    #[error(
        "{program} did not finish within {deadline:?}; it was killed together with its process \
         group (work it handed to the service manager or to a database server may still run)"
    )]
    TimedOut {
        /// The absolute path of the program that was started.
        program: String,
        /// The deadline that passed.
        deadline: Duration,
    },
    /// The program wrote more than the cap to one of its output streams.
    #[error(
        "{program} wrote more than {cap} bytes to {stream}; it was killed and none of its output \
         was kept"
    )]
    OutputTooLarge {
        /// The absolute path of the program that was started.
        program: String,
        /// The stream that crossed its cap.
        stream: OutputStream,
        /// The cap, in bytes, that was crossed.
        cap: u64,
    },
}

#[cfg(test)]
#[path = "../tests/utils/bound_exceeded_tests.rs"]
mod tests;
