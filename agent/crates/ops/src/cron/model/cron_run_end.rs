//! How a run started by hand ended.

use crate::cron::model::cron_run_limit::CronRunLimit;

/// The one way a run ended: with a status of its own, with none, or stopped by a
/// ceiling.
///
/// One value and not an exit status beside an optional limit: a status and a
/// limit that are both present, or both absent while a ceiling did stop the run,
/// describe no run that can happen, and two independent options could say them.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CronRunEnd {
    /// The program ended on its own and the client reported its status.
    Exited {
        /// The program's exit status.
        code: i32,
    },
    /// The program ended on its own but the client reported no status of its
    /// own (it died of a signal): a killed run has no status, and 0 would read
    /// as a success.
    WithoutStatus,
    /// A ceiling stopped the run, so it has no status.
    Stopped(CronRunLimit),
}
