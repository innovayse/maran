//! Which ceiling stopped a run started by hand.

/// The ceiling that ended a run before the program ended on its own.
///
/// A run that reached neither is not a
/// [`CronRunEnd::Stopped`](crate::cron::CronRunEnd::Stopped), never a third
/// variant here: "no ceiling" is the absence of one, and a variant for it would
/// let a caller hold a limit and an exit status that contradict each other.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CronRunLimit {
    /// The run lasted as long as the runtime ceiling, so the service manager
    /// stopped it.
    Runtime,
    /// A stream went past the output ceiling, so the run was stopped and none of
    /// its output was kept: a prefix of it would read as the whole.
    Output,
}
