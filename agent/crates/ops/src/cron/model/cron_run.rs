//! What one run started by hand reported.

use std::time::Duration;

use crate::cron::model::cron_run_limit::CronRunLimit;

/// The outcome of starting an entry now (issue #158).
///
/// Every field is what the program itself produced, as the account. None of it
/// is evidence of what the agent did: the program is the customer's, and it is
/// exactly as trustworthy as the command that wrote it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CronRun {
    /// How long the run took, from starting the unit to its client returning.
    pub duration: Duration,
    /// The program's exit status. `None` when a ceiling stopped the run, or when
    /// the client reported no status of its own (it died of a signal): a killed
    /// run has no status, and 0 would read as a success.
    pub exit_code: Option<i32>,
    /// What the program wrote to standard output, decoded lossily. Empty when
    /// the output ceiling stopped the run.
    pub stdout: String,
    /// What the program wrote to standard error, decoded lossily. Empty when the
    /// output ceiling stopped the run.
    pub stderr: String,
    /// The ceiling that stopped the run, or `None` when the program ended on its
    /// own.
    pub limit: Option<CronRunLimit>,
}
